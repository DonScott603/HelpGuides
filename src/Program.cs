using System;
using System.Windows.Forms;

namespace PsrClone
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Ensure correct physical-pixel behavior across differently-scaled
            // monitors BEFORE any window or capture happens.
            NativeMethods.EnableBestDpiAwareness();

            // Headless self-test entry point used for automated verification.
            if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.Run(args));
                return;
            }
            if (args.Length > 0 && string.Equals(args[0], "--recordtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RecordTest.Run(args));
                return;
            }
            // Dependency / environment diagnostic for triaging other machines.
            if (args.Length > 0 && (string.Equals(args[0], "--check", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.Exit(DependencyCheck.Run(args));
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
