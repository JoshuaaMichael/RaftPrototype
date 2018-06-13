﻿using System;
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
        private IConsensus<string, string> node;
        private SynchronizationContext mainThread;

        private List<Tuple<string, string>> log;
        private static object updateWindowLockObject = new object();

        private RaftBootstrapConfig config;
        private string servername;
        private string serverip;
        private int serverport;
        private ERaftLogType logLevel;
        private bool useEncryption;
        private int index;
        private string configurationFile;
        private string logfile;

        private static Mutex mutex = new Mutex();
        private bool onClosing;
        private bool isStopped;
        private const int MAX_ATTEMPTS = 1;

        public RaftNode(string serverName, string configFile, string logFile, bool isInstansiated = false)
        {
            //set local attributes
            servername = serverName;
            configurationFile = configFile;
            logfile = logFile;
            log = new List<Tuple<string, string>>();
            isStopped = false;


            mainThread = SynchronizationContext.Current;
            if (mainThread == null) { mainThread = new SynchronizationContext(); }


            onClosing = false;

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
            Text = string.Format("{0} - {1}", this.Text, servername);
            btStart.Enabled = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            LoadConfig();

            ERaftLogType[] logLevels = Enum.GetValues(typeof(ERaftLogType)).Cast<ERaftLogType>().ToArray();
            string[] logLevelStrings = new string[logLevels.Length];
            for (int i = 0; i < logLevels.Count(); i++)
            {
                string temp = logLevels[i].ToString().ToLower();
                logLevelStrings[i] = (temp[0] + "").ToUpper() + temp.Substring(1);
            }
            cbDebugLevel.DataSource = logLevelStrings;
            cbDebugLevel.SelectedIndex = (int) logLevel;

            SetupLogging();

            Task task = new TaskFactory().StartNew(new Action<object>((test) =>
            {
                StartNode();
            }), TaskCreationOptions.None);
        }

        public void LoadConfig()
        {
            string json = File.ReadAllText(configurationFile);
            config = JsonConvert.DeserializeObject<RaftBootstrapConfig>(json);

            index = int.Parse(servername.Replace("Node", ""))-1;
            serverport = config.nodePorts[index];
            serverip = config.nodeIPAddresses[index];
            logLevel = config.logLevel;
            useEncryption = config.useEncryption;
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
                    Task<EJoinClusterResponse> joinTask = node.JoinCluster(config.clusterName, 
                        config.clusterPassword, 
                        config.maxNodes, 
                        MAX_ATTEMPTS,
                        useEncryption
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
                        node.Dispose();
                        log.Clear();
                        if (MessageBox.Show("Failed to join cluster, do you want to retry?", "Error " + servername, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) == DialogResult.Retry)
                        {
                            continue;
                        }
                        else
                        {
                            mainThread.Send((object state) =>
                            {
                                lock (updateWindowLockObject)
                                {
                                    btStart.Enabled = true;
                                }
                            }, null);
                            return;
                        }
                    }
                }

                //update the main UI
                mainThread.Send((object state) =>
                {
                    lock (updateWindowLockObject)
                    {
                        UpdateNodeWindow();
                    }
                }, null);
            }
            catch (Exception e)
            {
                MessageBox.Show(servername + "\n" + e.ToString());
            }
        }

        private void CreateNode()
        {
            //Instantiate node
            node = new RaftConsensus<string, string>(config.nodeNames[index], config.nodePorts[index]);
            //Add peer to the node
            AddPeers(config, index);

            //Subscribe to the node UAS start/stop event
            node.OnStopUAS += HandleUASStop;
            node.OnStartUAS += HandleUASStart;
            node.OnNewCommitedEntry += HandleNewCommitEntry;
        }

        private void SetupLogging()
        {
            //string path = string.Format(@"{0}", Environment.CurrentDirectory);
            //string debug = Path.Combine(Environment.CurrentDirectory, "debug.log");
            //string debug = Path.Combine("C:\\Users\\Tori\\Downloads\\debug.log");

            RaftLogging.Instance.LogFilename = logfile;
            RaftLogging.Instance.EnableBuffer(50);
            RaftLogging.Instance.LogLevel = logLevel;
            RaftLogging.Instance.OnNewLogEntry += HandleInfoLogUpdate;
        }

        private void HandleInfoLogUpdate(object sender, Tuple<ERaftLogType, string> e)
        {
            try
            {
                if (mutex.WaitOne())
                {
                    if (!onClosing)
                    {
                        mainThread.Post((object state) =>
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
                mutex.ReleaseMutex();
            }
        }

        #endregion

        private void UpdateNodeWindow()
        {
            try
            {
                lbNodeName.Text = servername;
                if (node != null && node.IsUASRunning())
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
                    if (isStopped)
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
                logDataGrid.DataSource = log;
                if (log.Count() != 0)
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
            mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void HandleUASStop(object sender, EStopUasReason e)
        {
            mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void HandleNewCommitEntry(object sender, Tuple<string, string> e)
        {
            string n = servername;
            log.Add(e);
            mainThread.Post((object state) =>
            {
                UpdateNodeWindow();
            }, null);
        }

        private void Stop_Click(object sender, EventArgs e)
        {
            this.isStopped = true;

            node.Dispose();
            lock (updateWindowLockObject)
            {
                UpdateNodeWindow();
            }
        }

        private void Start_Click(object sender, EventArgs e)
        {
            btStart.Enabled = false;
            isStopped = false;
            log.Clear();

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

            Task<ERaftAppendEntryState> append = node.AppendEntry(tbKey.Text, tbValue.Text);
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
            if ( logEntryLine.IndexOf(servername) == 15)
            {
                return true;
            }
            return false;
        }

        private void SetNodeStatus(string logentry)
        {
            string str1 = logentry.Substring(29, logentry.IndexOf(')') - 29);//read log entry to get the status == 'FOLLOWER"

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
                node.ManualAddPeer(config.nodeNames[i], ipEndpoint);
            }
        }

        private void AddPeers(RaftBootstrapConfig config)
        {
            for (int i = 0; i < config.maxNodes; i++)
            {
                //Add the list of nodes into the PeerList
                if (string.Equals(config.nodeNames[i], servername))
                {
                    continue;
                }
                IPEndPoint ipEndpoint = new IPEndPoint(IPAddress.Parse(config.nodeIPAddresses[i]), config.nodePorts[i]);
                node.ManualAddPeer(config.nodeNames[i], ipEndpoint);
            }
        }
        
        #endregion

        #region closing

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                mutex.WaitOne();
                onClosing = true;
                RaftLogging.Instance.OnNewLogEntry -= HandleInfoLogUpdate;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
            base.OnClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            node.Dispose();
            mainThread = null;
            base.OnFormClosed(e);
        }

        #endregion


    }
}

