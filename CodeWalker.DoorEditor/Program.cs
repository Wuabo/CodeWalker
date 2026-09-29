using System;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
            GTAFolder.UpdateSettings();
        }
    }
}
