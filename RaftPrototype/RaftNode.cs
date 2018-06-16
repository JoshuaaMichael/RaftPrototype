using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TeamDecided.RaftConsensus.Consensus;
using TeamDecided.RaftConsensus.Consensus.Interfaces;
using TeamDecided.RaftConsensus.Consensus.Enums;
using TeamDecided.RaftConsensus.Common.Logging;
using Newtonsoft.Json;

namespace RaftPrototype
{
    public partial class RaftNode : Form
    {
        private IConsensus<string, string> _node;
        private SynchronizationContext _mainThread;

        private readonly List<Tuple<string, string>> _log;
        private static readonly object UpdateWindowLockObject = new object();

        private RaftBootstrapConfig _config;
        public string Servername { get; private set; }
        public string Serverip { get; private set; }
        public int Serverport { get; private set; }
        private ERaftLogType _logLevel;
        private bool _useEncryption;
        public int Index { get; private set; }
        private readonly string _configurationFile;
        private readonly string _logfile;

        private static readonly Mutex Mutex = new Mutex();
        private bool _onClosing;
        private bool _isStopped;
        private const int MAX_ATTEMPTS = 1;

        //public RaftNode(string serverName, string configFile, string logFile, bool isInstansiated = false)
        public RaftNode(int index, string configFile, string logFile, bool isInstansiated = false)
        {
            //set local attributes
            Index = index;
            _configurationFile = configFile;
            _logfile = logFile;
            _log = new List<Tuple<string, string>>();
            _isStopped = false;


            _mainThread = SynchronizationContext.Current;
            if (_mainThread == null) { _mainThread = new SynchronizationContext(); }


            _onClosing = false;

            InitializeComponent();
            Initialize();

            if(isInstansiated)
            {
                cbDebugLevel.Enabled = false;
            }
        }

        #region Setup Node

        private void Initialize()
        {
            LoadConfig();
            Text = string.Format("{0} - {1}", this.Text, Servername);
            btStart.Enabled = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;


            ERaftLogType[] logLevels = Enum.GetValues(typeof(ERaftLogType)).Cast<ERaftLogType>().ToArray();
            string[] logLevelStrings = new string[logLevels.Length];
            for (int i = 0; i < logLevels.Count(); i++)
            {
                string temp = logLevels[i].ToString().ToLower();
                logLevelStrings[i] = (temp[0] + "").ToUpper() + temp.Substring(1);
            }
            cbDebugLevel.DataSource = logLevelStrings;
            cbDebugLevel.SelectedIndex = (int) _logLevel;

            SetupLogging();

            Task task = new TaskFactory().StartNew(new Action<object>((test) =>
            {
                StartNode();
            }), TaskCreationOptions.None);
        }

        public void LoadConfig()
        {
            string json = File.ReadAllText(_configurationFile);
            _config = JsonConvert.DeserializeObject<RaftBootstrapConfig>(json);

            //index = int.Parse(servername.Replace("Node", ""))-1;
            Servername = _config.nodeNames[Index];
            Serverport = _config.nodePorts[Index];
            Serverip = _config.nodeIPAddresses[Index];
            _logLevel = _config.logLevel;
            _useEncryption = _config.useEncryption;
            }

        private void StartNode()
        {
            try
            {
                while (true)
                {
                    //Instantiate node and set up peer information
                    //subscribe to RaftLogging Log Info event
                    CreateNode();

                    //call the leader to join cluster
                    Task<EJoinClusterResponse> joinTask = _node.JoinCluster(_config.clusterName, 
                        _config.clusterPassword, 
                        _config.maxNodes, 
                        MAX_ATTEMPTS,
                        _useEncryption
                        );

                    joinTask.Wait();

                    //check the result of the attempt to join the cluster
                    EJoinClusterResponse result = joinTask.Result;
                    if (result == EJoinClusterResponse.Accept)
                    {
                        break;
                    }
                    else
                    {
                        _node.Dispose();
                        _log.Clear();
                        if (MessageBox.Show("Failed to join cluster, do you want to retry?", "Error " + Servername, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) == DialogResult.Retry)
                        {
                            continue;
                        }
                        else
                        {
                            _mainThread.Send((object state) =>
                            {
                                lock (UpdateWindowLockObject)
                                {
                                    btStart.Enabled = true;
                                }
                            }, null);
                            return;
                        }
                    }
                }

                //update the main UI
                _mainThread.Send((object state) =>
                {
                    lock (UpdateWindowLockObject)
                    {
                        UpdateNodeWindow();
                    }
                }, null);
            }
            catch (Exception e)
            {
                MessageBox.Show(Servername + "\n" + e.ToString());
            }
        }

        private void CreateNode()
        {
            //Instantiate node
            _node = new RaftConsensus<string, string>(_config.nodeNames[Index], _config.nodePorts[Index]);
            //Add peer to the node
            AddPeers(_config, Index);

            //Subscribe to the node UAS start/stop event
            _node.OnStopUAS += HandleUASStop;
            _node.OnStartUAS += HandleUASStart;
            _node.OnNewCommitedEntry += HandleNewCommitEntry;
        }

        private void SetupLogging()
        {
            //string path = string.Format(@"{0}", Environment.CurrentDirectory);
            //string debug = Path.Combine(Environment.CurrentDirectory, "debug.log");
            //string debug = Path.Combine("C:\\Users\\Tori\\Downloads\\debug.log");

            RaftLogging.Instance.LogFilename = _logfile;
            RaftLogging.Instance.EnableBuffer(50);
            RaftLogging.Instance.LogLevel = _logLevel;
            RaftLogging.Instance.OnNewLogEntry += HandleInfoLogUpdate;
        }

        private void HandleInfoLogUpdate(object sender, Tuple<ERaftLogType, string> e)
        {
            try
            {
                if (Mutex.WaitOne())
                {
                    if (!_onClosing)
                    {
                        _mainThread.Post((object state) =>
                        {
                            if (CheckLogEntry(e.Item2))
                            {
                                try
                                {
                                    tbLog.AppendText(e.Item2);
                                    SetNodeStatus(e.Item2);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Exception was thrown during async Post() update of GUI\n{0}", ex);
                                }
                            }
                        }, null);
                    }
                }
            }
            finally
            {
                Mutex.ReleaseMutex();
            }
        }

        #endregion

        private void UpdateNodeWindow()
        {
            try
            {
                lbNodeName.Text = Servername;
                if (_node != null && _node.IsUASRunning())
                {
                    lbServerState.Text = "Active";
                    lbServerState.ForeColor = System.Drawing.Color.Green;
                    gbAppendEntry.Enabled = true;
                    btStart.Enabled = false;
                    btStartNode.Enabled = false;
                    btStop.Enabled = true;
                    btStopNode.Enabled = true;
                }
                else
                {
                    if (_isStopped)
                    {
                        lbServerState.Text = "Offline";
                        lbServerState.ForeColor = System.Drawing.Color.Red;
                        btStart.Enabled = true;
                        btStartNode.Enabled = true;
                        btStop.Enabled = false;
                        btStopNode.Enabled = false;
                    }
                    else
                    {
                        lbServerState.Text = "Inactive";
                        lbServerState.ForeColor = System.Drawing.Color.Orange;
                        btStart.Enabled = false;
                        btStartNode.Enabled = false;
                        btStop.Enabled = true;
                        btStopNode.Enabled = true;
                    }

                    gbAppendEntry.Enabled = false;
                }

                tbKey.Clear();
                tbValue.Clear();
                logDataGrid.DataSource = null;
                logDataGrid.DataSource = _log;
                if (_log.Count() != 0)
                {
                    logDataGrid.Columns[0].HeaderText = "Key";
                    logDataGrid.Columns[1].HeaderText = "Value";
                }
            }
            catch 
            {
                Console.WriteLine("There was a problem updating the form, probably closing the form.");
            }
        }

        #region event methods

        private void HandleUASStart(object sender, EventArgs e)
        {
            _mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void HandleUASStop(object sender, EStopUasReason e)
        {
            _mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void HandleNewCommitEntry(object sender, Tuple<string, string> e)
        {
            string n = Servername;
            _log.Add(e);
            _mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void Stop_Click(object sender, EventArgs e)
        {
            this._isStopped = true;

            _node.Dispose();
            lock (UpdateWindowLockObject)
            {
                UpdateNodeWindow();
            }
        }

        private void Start_Click(object sender, EventArgs e)
        {
            btStart.Enabled = false;
            _isStopped = false;
            _log.Clear();

            //run the configuration setup on background thread stop GUI from blocking
            Task task = new TaskFactory().StartNew(new Action<object>((test) =>
            {
                StartNode();
            }), TaskCreationOptions.None);
        }

        private void AppendMessage_Click(object sender, EventArgs e)
        {
            if (tbKey.Text == "" || tbValue.Text == "")
            {
                MessageBox.Show("Key or Value should not be blank\n(not a limitation, it's just dumb, sorry :P)", "Warning - Blank key or value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Task<ERaftAppendEntryState> append = _node.AppendEntry(tbKey.Text, tbValue.Text);
            tbKey.Clear();
            tbValue.Clear();
        }

        private void cbDebugLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            RaftLogging.Instance.LogLevel = ((ERaftLogType)cbDebugLevel.SelectedIndex);
        }

        #endregion

        #region utilities

        private bool CheckLogEntry(string logEntryLine)
        {
            int servername_start_char = 15;
            if ( logEntryLine.IndexOf(Servername) == servername_start_char)
            {
                return true;
            }
            return false;
        }

        private void SetNodeStatus(string logentry)
        {
            int state_string = 29;
            string str1 = logentry.Substring(state_string, logentry.IndexOf(')') - state_string);//read log entry to get the status == 'FOLLOWER"

            if (lServerStatus.Text != str1)
            {
                lServerStatus.Text = str1;
            }
        }

        #endregion

        #region consensus stuff

        private void AddPeers(RaftBootstrapConfig config, int id)
        {
            for (int i = 0; i < config.maxNodes; i++)
            {
                //Add the list of nodes into the PeerList
                if (i == id)
                {
                    continue;
                }
                IPEndPoint ipEndpoint = new IPEndPoint(IPAddress.Parse(config.nodeIPAddresses[i]), config.nodePorts[i]);
                _node.ManualAddPeer(config.nodeNames[i], ipEndpoint);
            }
        }

        private void AddPeers(RaftBootstrapConfig config)
        {
            for (int i = 0; i < config.maxNodes; i++)
            {
                //Add the list of nodes into the PeerList
                if (string.Equals(config.nodeNames[i], Servername))
                {
                    continue;
                }
                IPEndPoint ipEndpoint = new IPEndPoint(IPAddress.Parse(config.nodeIPAddresses[i]), config.nodePorts[i]);
                _node.ManualAddPeer(config.nodeNames[i], ipEndpoint);
            }
        }
        
        #endregion

        #region closing

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                Mutex.WaitOne();
                _onClosing = true;
                RaftLogging.Instance.OnNewLogEntry -= HandleInfoLogUpdate;
            }
            finally
            {
                Mutex.ReleaseMutex();
            }
            base.OnClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _node.Dispose();
            _mainThread = null;
            base.OnFormClosed(e);
        }


        #endregion

    }
}

