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

                if (args.Length == 3 )
                {
                    string serverName = args[0];
                    string configFile = args[1];
                    string logFile = args[2];
                    Application.Run(new RaftNode(serverName, configFile, logFile));
                }
                else if (args.Length == 1)
                {
                    string configFile = args[0];
                    Application.Run(new RaftStartNode(configFile));
                }
                else //ALl other cases running the program to bootstrap
                {
                    Application.Run(new RaftBootStrap());
                }
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                RaftLogging.Instance.FlushBuffer();
            }
        }
    }
}