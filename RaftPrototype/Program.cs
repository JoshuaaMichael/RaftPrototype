using System;
using System.Windows.Forms;
using TeamDecided.RaftConsensus.Common.Logging;

namespace RaftPrototype
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                RaftLogging.Instance.EnableBuffer(50);

                if (args.Length == 3 ) //run a node
                {
                    int serverIndex = int.Parse(args[0]);
                    string configFile = args[1];
                    string logFile = args[2];
                    Application.Run(new RaftNode(serverIndex, configFile, logFile));
                }
                else if (args.Length == 1) //select a node based on predefined config 
                {
                    string configFile = args[0];
                    Application.Run(new RaftStartNode(configFile));
                }
                else //all other cases running the program to bootstrap
                {
                    Application.Run(new RaftBootStrap());
                }
            }
            finally
            {
                RaftLogging.Instance.FlushBuffer();
            }
        }
    }
}