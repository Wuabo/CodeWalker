using System;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            GTAFolder.UpdateSettings();
        }
    }
}
