using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>
    /// Live end-to-end verification: installs the real global hooks, synthesizes a
    /// mouse click + keystrokes via SendInput, and confirms the recorder captures
    /// steps (screenshots + UI Automation) exactly as it would for a real user.
    /// Invoked with --recordtest. Runs on an STA message-pumping thread.
    /// </summary>
    internal static class RecordTest
    {
        private static int _result = 99;
        private static string _log;

        private static void Log(string s)
        {
            try { File.AppendAllText(_log, s + Environment.NewLine); } catch { }
        }

        public static int Run(string[] args)
        {
            _log = args.Length > 2 ? args[2]
                : Path.Combine(Path.GetTempPath(), "psrclone_recordtest.log");
            try { File.WriteAllText(_log, "record test start " + DateTime.Now + Environment.NewLine); } catch { }

            // A message loop is required for low-level hooks to receive callbacks.
            using (var ctx = new HiddenLoop())
            {
                ctx.Run(() => Execute(args));
            }
            return _result;
        }

        private static void Execute(string[] args)
        {
            var settings = new RecorderSettings { MaxScreenshots = 100 };
            var rec = new Recorder(settings);
            int events = 0;
            rec.StepAdded += (s, step) => { events++; Log("  captured: " + step.BuildDescription()); };
            rec.Start();

            // Give hooks a moment to install.
            Pump(300);

            var scr = Screen.PrimaryScreen.Bounds;
            int cx = scr.X + scr.Width / 2;
            int cy = scr.Y + scr.Height / 2;
            NativeMethods.SetCursorPos(cx, cy);
            Pump(120);

            // Synthetic left click.
            SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
            Pump(60);
            SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
            Pump(250);

            // Synthetic typing: "hi"
            SendKey(0x48); // H
            Pump(40);
            SendKey(0x49); // I
            Pump(60);

            // Allow keyboard idle flush + worker UIA resolution.
            Pump(2200);

            rec.Stop();
            Pump(200);

            int count = rec.Count;
            Log("total steps: " + count);

            if (count < 1)
            {
                _result = 1;
                Log("RECORDTEST FAIL: no steps captured (hooks may be blocked)");
                Application.ExitThread();
                return;
            }

            // Verify at least one screenshot was captured.
            bool anyShot = false;
            foreach (var st in rec.Snapshot()) if (st.Screenshot != null) { anyShot = true; break; }

            string outPath = args.Length > 1 ? args[1]
                : Path.Combine(Path.GetTempPath(), "psrclone_recordtest.mht");
            string report = ReportWriter.Save(outPath, rec.Snapshot(), rec.StartedAt, rec.StoppedAt, settings);

            Log("screenshots captured: " + anyShot + "  report: " + report);
            _result = anyShot ? 0 : 2;
            Application.ExitThread();
        }

        private static void SendMouse(uint flags)
        {
            var inp = new NativeMethods.INPUT[1];
            inp[0].type = NativeMethods.INPUT_MOUSE;
            inp[0].u.mi = new NativeMethods.MOUSEINPUT { dwFlags = flags };
            NativeMethods.SendInput(1, inp, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static void SendKey(ushort vk)
        {
            var down = new NativeMethods.INPUT[1];
            down[0].type = NativeMethods.INPUT_KEYBOARD;
            down[0].u.ki = new NativeMethods.KEYBDINPUT { wVk = vk };
            NativeMethods.SendInput(1, down, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));

            var up = new NativeMethods.INPUT[1];
            up[0].type = NativeMethods.INPUT_KEYBOARD;
            up[0].u.ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP };
            NativeMethods.SendInput(1, up, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        /// <summary>Runs an action inside a WinForms message loop on this (STA) thread.</summary>
        private sealed class HiddenLoop : IDisposable
        {
            public void Run(Action action)
            {
                var form = new Form
                {
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized,
                    FormBorderStyle = FormBorderStyle.None,
                    Opacity = 0,
                    Width = 1,
                    Height = 1
                };
                form.Load += (s, e) =>
                {
                    var t = new System.Windows.Forms.Timer { Interval = 50 };
                    t.Tick += (s2, e2) => { t.Stop(); action(); };
                    t.Start();
                };
                Application.Run(form);
            }

            public void Dispose() { }
        }
    }
}
