using System;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorAudio
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new DoorAudioForm());
            GTAFolder.UpdateSettings();
        }
    }
}
