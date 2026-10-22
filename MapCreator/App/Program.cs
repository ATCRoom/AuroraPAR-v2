using System.Windows;

namespace ATCRoom.MapCreator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application app = new() { ShutdownMode = ShutdownMode.OnMainWindowClose };
            // Files dropped on the exe (or given on the command line) are opened at once.
            app.Run(new MainWindow(args));
        }
    }
}
