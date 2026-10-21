using System.Windows;

namespace ATCRoom.MapCreator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application app = new() { ShutdownMode = ShutdownMode.OnMainWindowClose };
            // A file dropped on the exe (or given on the command line) is opened at once.
            app.Run(new MainWindow(args.FirstOrDefault()));
        }
    }
}
