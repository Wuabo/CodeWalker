using System;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorTuning
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new DoorTuningForm());
            GTAFolder.UpdateSettings();
        }
    }
}
