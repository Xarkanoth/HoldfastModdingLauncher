using System;
using System.Windows.Forms;

namespace HoldfastModdingUninstaller
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new UninstallerForm());
        }
    }
}

