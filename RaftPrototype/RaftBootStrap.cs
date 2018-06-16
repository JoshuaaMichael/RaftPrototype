using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using TeamDecided.RaftConsensus.Common.Logging;
using Newtonsoft.Json;

namespace RaftPrototype
{
    public partial class RaftBootStrap : Form
    {
        private const int MAXIMUM_NODES = 9;
        private const int MINIMUM_NODES = 3;
        private const int DEFAULT_NODES = 3;
        private const int START_PORT = 5555;

        private const string MAX_NODES_WARNING = "Maximum nine (9) nodes supported in prototype";
        private const string MIN_NODES_WARNING = "Consensus requires minimum three (3) nodes";
        private const string CLUSTER_NAME = "Prototype Cluster";
        private const string CLUSTER_PASSWD = "password123";
        private const string IP_TO_BIND = "127.0.0.1";
        private const string CONFIG_FILE = "./config.json";

        private List<Tuple<string, string, int>> _clusterPeerList ;
        private readonly string _logfile;
        private ProcessStartInfo _startInfo;
        private RaftNode[] _nodes;

        public RaftBootStrap()
        {
            _logfile = Path.Combine(Environment.CurrentDirectory, "debug.log");
            _clusterPeerList = new List<Tuple<string, string, int>>();
            InitializeComponent();
            Initialize();
        }

        private void Initialize()
        {
            SetupDebug();
            SetProcessStartInfo();

            //add defaults to GUI
            tbClusterName.Text = CLUSTER_NAME;
            tbClusterPasswd.Text = CLUSTER_PASSWD;
            tbPort.Text = START_PORT.ToString();
            tbIPAddress.Text = IP_TO_BIND;
            tbIPAddress.Enabled = false;//don't want the user to change this at the moment
            cbInstantiate.Checked = true;//set to instantiate by default to enable debugging
            ERaftLogType[] logLevels = Enum.GetValues(typeof(ERaftLogType)).Cast<ERaftLogType>().ToArray();
            string[] logLevelStrings = new string[logLevels.Length];
            for (int i = 0; i < logLevels.Count(); i++)
            {
                string temp = logLevels[i].ToString().ToLower();
                logLevelStrings[i] = (temp[0] + "").ToUpper() + temp.Substring(1);
            }
            cbDebugLevel.DataSource = logLevelStrings;
            cbDebugLevel.SelectedIndex = 2;

            SetNodeCountSelector();// setup node numeric up down UI

            CreateGridView();//Populate the datagrid with nodeName, ipAddress and port based of GUI information provided
        }

        private void SetupDebug()
        {
            //string path = string.Format(@"{0}", Environment.CurrentDirectory);
            //string debug = Path.Combine(Environment.CurrentDirectory, "debug.log");
            //string debug = Path.Combine("C:\\Users\\Tori\\Downloads\\debug.log");

            RaftLogging.Instance.LogFilename = _logfile;
            RaftLogging.Instance.DeleteExistingLogFile();
            RaftLogging.Instance.LogLevel = ERaftLogType.Info;
        }

        private void SetNodeCountSelector()
        {
            // Set the Minimum, Maximum, and initial Value.
            nNodes.Value = DEFAULT_NODES;
            nNodes.Maximum = MAXIMUM_NODES;
            nNodes.Minimum = MINIMUM_NODES;
        }

        private void CreateRaftNodes_Click(object sender, EventArgs e)
        {
            //Create config file, and save it, then start people
            //Information for config file is represented by information in GUI
            int maxNodes = (int)nNodes.Value;
            RaftBootstrapConfig rbsc = new RaftBootstrapConfig
            {
                clusterName = tbClusterName.Text,
                clusterPassword = tbClusterPasswd.Text,
                maxNodes = maxNodes,
                logLevel = (ERaftLogType) cbDebugLevel.SelectedIndex,
                useEncryption = cbUseEncryption.Checked
            };

            foreach (var peer in _clusterPeerList)
            {
                rbsc.nodeNames.Add(peer.Item1);
                rbsc.nodeIPAddresses.Add(peer.Item2);
                rbsc.nodePorts.Add(peer.Item3);
            }

            // serialize the config object
            string json = JsonConvert.SerializeObject(rbsc, Formatting.Indented);

            //create new config file 
            File.Delete(CONFIG_FILE);
            File.WriteAllText(CONFIG_FILE, json);

            if (cbInstantiate.Checked)
            {
                RaftLogging.Instance.DeleteExistingLogFile();
                //create array of RaftNode
                _nodes = new RaftNode[maxNodes];

                for (int i = 0; i < rbsc.nodeNames.Count; i++)
                {
                    _nodes[i] = new RaftNode(i, CONFIG_FILE, _logfile, true);
                    _nodes[i].FormClosed += RaftNodeClosure;
                    _nodes[i].Show();
                    Enabled = false;
                    if (i == 0)
                    {
                        Thread.Sleep(100);
                    }
                }

                Enabled = false;
                Hide();
            }
            else
            {
                //Clean out old log
                File.Delete(string.Format("{0}-debug.log", rbsc.nodeNames[0]));
                //start up node1 first so that it can become leader
                _startInfo.Arguments = string.Format("{0} {1} {2}", 0, CONFIG_FILE, string.Format("{0}-debug.log", rbsc.nodeNames[0]));
                Process.Start(_startInfo);

                for (int i = rbsc.nodeNames.Count - 1; i > 0; i--)
                {
                    Thread.Sleep(200);
                    File.Delete(string.Format("{0}-debug.log", rbsc.nodeNames[i]));
                    _startInfo.Arguments = string.Format("{0} {1} {2}", i, CONFIG_FILE, string.Format("{0}-debug.log", rbsc.nodeNames[i]));
                    Process.Start(_startInfo);
                }

                Close();
            }
        }

        private void Exit_Button_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Nodes_ValueChanged(object sender, EventArgs e)
        {
            if (nNodes.Value == 9)
            {
                lWarningNodesNumber.ForeColor = Color.Red;
                lWarningNodesNumber.Visible = true;
                lWarningNodesNumber.Text= MAX_NODES_WARNING;
            }
            else if (nNodes.Value == 3)
            {
                lWarningNodesNumber.ForeColor = Color.Red;
                lWarningNodesNumber.Visible = true;
                lWarningNodesNumber.Text = MIN_NODES_WARNING;
            }
            else
            {
                lWarningNodesNumber.Visible = false;
                //statusPanel.Text = string.Format("Configuration file: {0}", Path.Combine(Environment.CurrentDirectory, "config.json"));
            }
            nodeConfigDataView.DataSource = null;
            CreateGridView();
        }

        private void CreateGridView()
        {
            // temporary datasource
            _clusterPeerList = new List<Tuple<string, string, int>>();
            int maxNodes = (int) nNodes.Value;

            for (int i = 0; i< maxNodes; i++)
            {
                string nodeName = string.Format("Node{0}", i + 1);
                string nodeIP = IP_TO_BIND;
                int nodePort = int.Parse(tbPort.Text) + i;

                Tuple<string, string, int> temp = new Tuple<string, string, int> (nodeName, nodeIP, nodePort);
                _clusterPeerList.Add(temp);
            }

            nodeConfigDataView.DataSource = _clusterPeerList;
            nodeConfigDataView.Columns[0].HeaderText = "Name";
            nodeConfigDataView.Columns[1].HeaderText = "IP Address";
            nodeConfigDataView.Columns[2].HeaderText = "Port";

            foreach (DataGridViewColumn col in nodeConfigDataView.Columns)
            {
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private void nodeConfigDataView_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void nodeConfigDataView_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData((DataFormats.FileDrop));
            _startInfo.Arguments = string.Format("{0}", files[0]);
            Process.Start(_startInfo);
            Close();
        }

        private void TbPort_textChangedEventHandler(object sender, EventArgs e)
        {
            CreateGridView();
        }

        private void SetProcessStartInfo()
        {
            _startInfo = new ProcessStartInfo()
            {
                FileName = System.Reflection.Assembly.GetEntryAssembly().Location,
                // ReSharper disable once AssignNullToNotNullAttribute
                WorkingDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        private void RaftNodeClosure(object sender, FormClosedEventArgs e)
        {
            int count = 0;
            if (sender.GetType() == typeof(RaftNode))
            {
                int index = ((RaftNode) sender).Index;
                _nodes[index] = null;
            }

            foreach (RaftNode node in _nodes)
            {
                if (node != null)
                {
                    break;
                }
                count++;
            }

            if (count == _nodes.Length) Close();
        }
    }
}