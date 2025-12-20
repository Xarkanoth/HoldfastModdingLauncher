using System;
using System.IO;
using System.Windows.Forms;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    internal static class Program
    {
        private const string FIRST_RUN_FLAG = "first_run.flag";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Check for --debug flag
            bool debugMode = args.Length > 0 && args[0] == "--debug";
            
            // Check if this is first run
            bool isFirstRun = IsFirstRun();
            
            // Show installer on first run
            if (isFirstRun)
            {
                var installerForm = new InstallerForm(isFirstRun: true);
                var result = installerForm.ShowDialog();
                
                if (result == DialogResult.OK)
                {
                    // User clicked Install but stayed in current location - mark as complete and continue
                    MarkFirstRunComplete();
                    Application.Run(new MainForm(debugMode));
                }
                else if (result == DialogResult.Abort)
                {
                    // User installed to Holdfast directory - that version will launch separately
                    // Exit this instance without doing anything else
                    return;
                }
                // If user clicked X (Cancel), exit without creating folders
                else
                {
                    // User cancelled - exit application
                    return;
                }
            }
            else
            {
                // Not first run - run main form normally
                Application.Run(new MainForm(debugMode));
            }
        }

        private static bool IsFirstRun()
        {
            string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FIRST_RUN_FLAG);
            return !File.Exists(flagPath);
        }

        private static void MarkFirstRunComplete()
        {
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FIRST_RUN_FLAG);
                File.WriteAllText(flagPath, DateTime.Now.ToString());
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}

