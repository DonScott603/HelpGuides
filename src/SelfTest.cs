using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace PsrClone
{
    /// <summary>Headless verification of the report pipeline (invoked with --selftest).</summary>
    internal static class SelfTest
    {
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
                    Time = DateTime.Now,
                    Comment = "This is where the problem occurs.",
                    Screenshot = MakeShot(Color.FromArgb(230, 240, 255)),
                    Highlight = new Rectangle(100, 100, 300, 120)
                };
                steps.Add(comment);

                string mht = ReportWriter.Save(outPath, steps, DateTime.Now.AddMinutes(-2), DateTime.Now);

                // Validate the .mht actually written to disk carries every expected marker.
                if (!File.Exists(mht)) return Fail("report not written: " + mht);
                if (!mht.EndsWith(".mht", StringComparison.OrdinalIgnoreCase))
                    return Fail("report is not a .mht: " + mht);

                string content = File.ReadAllText(mht);
                foreach (var marker in new[]
                {
                    "Recorded Problem Steps",
                    "multipart/related",
                    "Content-Transfer-Encoding: base64",
                    "User left click on",
                    "User double click on",
                    "User keyboard input",
                    "User Comment:",
                    "Additional Details"
                })
                {
                    if (content.IndexOf(marker, StringComparison.Ordinal) < 0)
                        return Fail("missing marker: " + marker);
                }
                int imgs = CountOccurrences(content, "Content-Type: image/jpeg");
                if (imgs != 4) return Fail("expected 4 embedded images, got " + imgs);

                var fi = new FileInfo(mht);

                // Validate the folder-dump output too.
                string dumpDir = Path.Combine(Path.GetTempPath(), "psrclone_selftest_dump");
                if (Directory.Exists(dumpDir)) Directory.Delete(dumpDir, true);
                string htm = ReportWriter.SaveFolder(dumpDir, steps, DateTime.Now.AddMinutes(-2), DateTime.Now);
                if (!File.Exists(htm)) return Fail("folder dump: htm not written");
                int jpgs = Directory.GetFiles(dumpDir, "*.jpeg").Length;
                if (jpgs != 4) return Fail("folder dump: expected 4 jpegs, got " + jpgs);
                string htmText = File.ReadAllText(htm);
                if (htmText.IndexOf("Recorded Problem Steps", StringComparison.Ordinal) < 0)
                    return Fail("folder dump: html missing title");
                Directory.Delete(dumpDir, true);

                Console.WriteLine("SELFTEST PASS  ->  " + mht + "  (" + fi.Length + " bytes)");
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex.ToString());
            }
        }

        private static RecordedStep MakeMouse(int i, string action, string name, string type,
            string window, string program, Rectangle hi, Point cursor)
        {
            return new RecordedStep
            {
                Index = i,
                Kind = StepKind.Mouse,
                Time = DateTime.Now,
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
