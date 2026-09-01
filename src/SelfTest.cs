using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace PsrClone
{
    /// <summary>Headless verification of the report pipeline (invoked with --selftest).</summary>
    internal static class SelfTest
    {
        // Fixed so the timestamp assertions below are deterministic. Its "g" rendering
        // ("3/4/2026 5:06 AM") is not a substring of the session header's "F" rendering
        // ("Wednesday, March 4, 2026 5:06:07 AM"), so the two never collide.
        private static readonly DateTime FixedTime = new DateTime(2026, 3, 4, 5, 6, 7);

        public static int Run(string[] args)
        {
            try
            {
                string outPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "psrclone_selftest.mht");
                var steps = new List<RecordedStep>();

                steps.Add(MakeMouse(1, "left click", "Start", "button", "Taskbar", "explorer",
                    new Rectangle(40, 700, 48, 40), new Point(64, 720)));
                steps.Add(MakeMouse(2, "double click", "Documents", "list item", "File Explorer", "explorer",
                    new Rectangle(200, 160, 120, 24), new Point(260, 172)));

                var kb = MakeMouse(3, null, "Search box", "edit", "File Explorer", "explorer",
                    new Rectangle(500, 60, 300, 28), new Point(650, 74));
                kb.Kind = StepKind.Keyboard;
                kb.Action = null;
                kb.TypedText = "hello world{Enter}";
                steps.Add(kb);

                var comment = new RecordedStep
                {
                    Index = 4,
                    Kind = StepKind.Comment,
                    Time = FixedTime,
                    Comment = "This is where the problem occurs.",
                    Screenshot = MakeShot(Color.FromArgb(230, 240, 255)),
                    Highlight = new Rectangle(100, 100, 300, 120)
                };
                steps.Add(comment);

                var allOn = new RecorderSettings
                {
                    IncludeStepTimestamps = true,
                    IncludeAdditionalDetails = true,
                    IncludeEnvironment = true
                };
                var allOff = new RecorderSettings
                {
                    IncludeStepTimestamps = false,
                    IncludeAdditionalDetails = false,
                    IncludeEnvironment = false
                };

                DateTime started = FixedTime.AddMinutes(-2);
                DateTime stopped = FixedTime;

                // ---- Pass 1: every report section switched on ----
                string mht = ReportWriter.Save(outPath, steps, started, stopped, allOn);
                if (!File.Exists(mht)) return Fail("report not written: " + mht);
                if (!mht.EndsWith(".mht", StringComparison.OrdinalIgnoreCase))
                    return Fail("report is not a .mht: " + mht);

                // Only assert on the MIME envelope here. BuildHtml emits the document as a
                // single unbroken line, so QuotedPrintable.Encode hard-wraps it every 74
                // characters and can split an HTML marker across a soft line break. That
                // makes a presence check flaky and, worse, makes an absence check pass
                // spuriously. Report content is asserted against the folder dump below,
                // which is written as raw UTF-8.
                string mhtText = File.ReadAllText(mht);
                foreach (var marker in new[]
                {
                    "Subject: Recorded Problem Steps",
                    "multipart/related",
                    "Content-Transfer-Encoding: base64"
                })
                {
                    if (mhtText.IndexOf(marker, StringComparison.Ordinal) < 0)
                        return Fail("missing envelope marker: " + marker);
                }
                int imgs = CountOccurrences(mhtText, "Content-Type: image/jpeg");
                if (imgs != 4) return Fail("expected 4 embedded images, got " + imgs);

                var fi = new FileInfo(mht);

                string onHtm = DumpAndRead("psrclone_selftest_on", steps, started, stopped, allOn);
                foreach (var marker in new[]
                {
                    "Recorded Problem Steps",
                    "User left click on",
                    "User double click on",
                    "User keyboard input",
                    "User Comment:",
                    "Additional Details",
                    "Recording Environment",
                    FixedTime.ToString("g")
                })
                {
                    if (onHtm.IndexOf(marker, StringComparison.Ordinal) < 0)
                        return Fail("toggles on: missing marker: " + marker);
                }

                // ---- Pass 2: the three report toggles switched off ----
                string offHtm = DumpAndRead("psrclone_selftest_off", steps, started, stopped, allOff);
                foreach (var marker in new[]
                {
                    "Additional Details",
                    "Recording Environment",
                    FixedTime.ToString("g")
                })
                {
                    if (offHtm.IndexOf(marker, StringComparison.Ordinal) >= 0)
                        return Fail("toggles off: marker should be absent: " + marker);
                }

                // ...but the Steps section itself, and the session header, must survive.
                foreach (var marker in new[]
                {
                    "Recorded Problem Steps",
                    "Recording session:",
                    "User left click on",
                    "User double click on",
                    "User keyboard input",
                    "User Comment:"
                })
                {
                    if (offHtm.IndexOf(marker, StringComparison.Ordinal) < 0)
                        return Fail("toggles off: missing marker: " + marker);
                }

                Console.WriteLine("SELFTEST PASS  ->  " + mht + "  (" + fi.Length + " bytes)");
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex.ToString());
            }
        }

        /// <summary>
        /// Renders a folder dump into a temp directory, verifies the loose image count,
        /// returns the raw (un-encoded) HTML text and removes the directory.
        /// </summary>
        private static string DumpAndRead(string name, IList<RecordedStep> steps,
            DateTime started, DateTime stopped, RecorderSettings settings)
        {
            string dir = Path.Combine(Path.GetTempPath(), name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            string htm = ReportWriter.SaveFolder(dir, steps, started, stopped, settings);
            if (!File.Exists(htm)) throw new Exception("folder dump " + name + ": htm not written");

            string text = File.ReadAllText(htm);
            int jpgs = Directory.GetFiles(dir, "*.jpeg").Length;
            Directory.Delete(dir, true);

            // Screenshots are emitted only by the Steps loop, so no toggle may change this.
            if (jpgs != 4)
                throw new Exception("folder dump " + name + ": expected 4 jpegs, got " + jpgs);

            return text;
        }

        private static RecordedStep MakeMouse(int i, string action, string name, string type,
            string window, string program, Rectangle hi, Point cursor)
        {
            return new RecordedStep
            {
                Index = i,
                Kind = StepKind.Mouse,
                Time = FixedTime,
                Action = action,
                ElementName = name,
                ElementType = type,
                WindowName = window,
                ProgramName = program,
                Screenshot = MakeShot(Color.FromArgb(245, 245, 245)),
                Highlight = hi,
                Cursor = cursor
            };
        }

        private static Bitmap MakeShot(Color bg)
        {
            var bmp = new Bitmap(960, 800);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                using (var b = new SolidBrush(Color.FromArgb(0, 90, 158)))
                    g.FillRectangle(b, 0, 0, 960, 48);
                using (var f = new Font("Segoe UI", 12))
                using (var w = new SolidBrush(Color.White))
                    g.DrawString("Simulated Window", f, w, 12, 12);
            }
            return bmp;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int c = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { c++; idx += needle.Length; }
            return c;
        }

        private static int Fail(string msg)
        {
            Console.Error.WriteLine("SELFTEST FAIL: " + msg);
            return 1;
        }
    }
}
