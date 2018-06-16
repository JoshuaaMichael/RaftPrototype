﻿using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace RaftPrototype
{
    public partial class RaftStartNode : Form
    {
        private readonly string CONFIG_FILE;
        private RaftBootstrapConfig config;


        public RaftStartNode(string configFile)
        {
            InitializeComponent();
            if (File.Exists(configFile))
            {
                CONFIG_FILE = configFile;
                Initialize();
            }
            else
            {
                Close();
            }
        }

        private void Initialize()
        {
            string json = File.ReadAllText(CONFIG_FILE);
            config = JsonConvert.DeserializeObject<RaftBootstrapConfig>(json);
            cbNodes.DataSource = config.nodeNames;
        }

        private void cbNodes_SelectedIndexChanged(object sender, EventArgs e)
        {
            //MessageBox.Show(string.Format("{0}",cbNodes.SelectedIndex));
            tbIPAddress.Text = config.nodeIPAddresses[cbNodes.SelectedIndex];
            tbPort.Text = config.nodePorts[cbNodes.SelectedIndex].ToString();
        }

        private void StartNode_Click(object sender, EventArgs e)
        {
            //create default start info for the process
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = System.Reflection.Assembly.GetEntryAssembly().Location,
                WorkingDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            //Clean out old log
            File.Delete(string.Format("{0}-debug.log", config.nodeNames[cbNodes.SelectedIndex]));
            int index = cbNodes.SelectedIndex;
            //start up node1 first so that it can become leader
            //startInfo.Arguments = string.Format("{0} {1} {2}", config.nodeNames[cbNodes.SelectedIndex], CONFIG_FILE, string.Format("{0}-debug.log", config.nodeNames[cbNodes.SelectedIndex]));
            startInfo.Arguments = string.Format("{0} {1} {2}", index, CONFIG_FILE, string.Format("{0}-debug.log", config.nodeNames[cbNodes.SelectedIndex]));
            Process.Start(startInfo);
            //sleep to give head start for setting it self up

            Close();
        }

        private void tbIPAddress_MouseEnter(object sender, EventArgs e)
        {
            tbIPAddress.PasswordChar = '\0';
        }


        private void tbIPAddress_MouseLeave(object sender, EventArgs e)
        {
            tbIPAddress.PasswordChar = '*';
        }
    }
}
