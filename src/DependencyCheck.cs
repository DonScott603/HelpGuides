using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>
    /// Environment / dependency diagnostic (invoked with --check). Confirms every
    /// capability PSR Clone relies on is actually present and working on the current
    /// machine, and performs a real screen-capture test on each monitor. This is the
    /// tool to run when the app misbehaves on a different system.
    /// </summary>
    internal static class DependencyCheck
    {
        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hprocess, out int value);

        public static int Run(string[] args)
        {
            bool quiet = Array.IndexOf(args, "--quiet") >= 0;
            string log = args.Length > 1 && !args[1].StartsWith("--")
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "psrclone_check.log");

            var sb = new StringBuilder();
            bool ok = true;

            sb.AppendLine("PSR Clone " + BuildInfo.Version + " - environment check");
            sb.AppendLine("Time: " + DateTime.Now);
            sb.AppendLine();
            sb.AppendLine("OS:            " + Environment.OSVersion.VersionString +
                          (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"));
            sb.AppendLine("Process:       " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            sb.AppendLine("CLR:           " + Environment.Version);
            sb.AppendLine("DPI awareness: " + DescribeDpiAwareness());
            sb.AppendLine();

            // ---- Required assemblies / capabilities ----
            sb.AppendLine("Dependencies (all in-box on Windows 10/11):");
            ok &= Probe(sb, "System.Drawing (GDI+)", () => { using (var b = new Bitmap(4, 4)) { } });
            ok &= Probe(sb, "System.Windows.Forms", () => { var _ = SystemInformation.VirtualScreen; });
            ok &= Probe(sb, "UI Automation (UIAutomationClient)", () =>
            {
                var p = Cursor.Position;
                var el = System.Windows.Automation.AutomationElement.FromPoint(
                    new System.Windows.Point(p.X, p.Y));
                if (el == null) throw new Exception("FromPoint returned null");
            });
            ok &= Probe(sb, "System.IO.Compression (ZipArchive)", () =>
            {
                using (var ms = new MemoryStream())
                using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create)) { }
            });
            ok &= Probe(sb, "System.IO.Compression.FileSystem (ZipFile)", () =>
            {
                var t = typeof(System.IO.Compression.ZipFile);
                if (t == null) throw new Exception("type missing");
            });
            sb.AppendLine();

            // ---- Real capture test per monitor (the screenshot issue) ----
            sb.AppendLine("Monitors and screen-capture test:");
            try
            {
                foreach (var scr in Screen.AllScreens)
                {
                    string line = "  " + (scr.Primary ? "* " : "  ") + scr.DeviceName +
                                  "  " + scr.Bounds.Width + "x" + scr.Bounds.Height +
                                  " @ (" + scr.Bounds.X + "," + scr.Bounds.Y + ")  ";
                    try
                    {
                        Rectangle mb;
                        using (var bmp = Recorder.CaptureMonitor(
                            new Point(scr.Bounds.X + scr.Bounds.Width / 2, scr.Bounds.Y + scr.Bounds.Height / 2), out mb))
                        {
                            bool blank = IsBlank(bmp);
                            line += blank ? "CAPTURE BLANK (DPI/permission issue)" : "capture OK";
                            if (blank) ok = false;
                        }
                    }
                    catch (Exception ex) { line += "CAPTURE FAILED: " + ex.Message; ok = false; }
                    sb.AppendLine(line);
                }
            }
            catch (Exception ex) { sb.AppendLine("  enumeration failed: " + ex.Message); ok = false; }

            sb.AppendLine();
            sb.AppendLine(ok ? "RESULT: OK - all dependencies present and capture works."
                             : "RESULT: PROBLEMS DETECTED - see notes above.");

            string report = sb.ToString();
            try { File.WriteAllText(log, report); } catch { }

            if (!quiet)
            {
                MessageBox.Show(report + "\n\nSaved to: " + log, "PSR Clone - Environment Check",
                    MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            return ok ? 0 : 1;
        }

        private static bool Probe(StringBuilder sb, string name, Action action)
        {
            try { action(); sb.AppendLine("  [ OK ] " + name); return true; }
            catch (Exception ex) { sb.AppendLine("  [FAIL] " + name + "  -> " + ex.Message); return false; }
        }

        private static bool IsBlank(Bitmap bmp)
        {
            // Sample a grid; if every sampled pixel is identical, treat as blank.
            Color first = bmp.GetPixel(0, 0);
            int stepX = Math.Max(1, bmp.Width / 16);
            int stepY = Math.Max(1, bmp.Height / 16);
            for (int y = 0; y < bmp.Height; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                    if (bmp.GetPixel(x, y) != first) return false;
            return true;
        }

        private static string DescribeDpiAwareness()
        {
            try
            {
                int v;
                if (GetProcessDpiAwareness(System.Diagnostics.Process.GetCurrentProcess().Handle, out v) == 0)
                {
                    switch (v)
                    {
                        case 0: return "Unaware (screens WILL be wrong on scaled monitors)";
                        case 1: return "System-DPI-aware (may be wrong on secondary monitors)";
                        case 2: return "Per-Monitor-aware (correct)";
                    }
                }
            }
            catch { }
            return "unknown";
        }
    }
}
