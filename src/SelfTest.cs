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

                // ---- Pass 3: the guide-editor path (custom title/intro, text step, delete,
                //      rewritten step text, crop + both redaction kinds, print CSS) ----
                string guideErr = GuidePass(steps, started, stopped, allOff);
                if (guideErr != null) return Fail(guideErr);

                // ---- Pass 4: the default report must be byte-identical whether it goes
                //      through the legacy overload or an untouched GuideDocument ----
                string legacyHtm = DumpAndRead("psrclone_selftest_legacy", steps, started, stopped, allOff);
                string docHtm = DumpAndRead("psrclone_selftest_doc",
                    new GuideDocument(steps, started, stopped), allOff);
                if (!string.Equals(legacyHtm, docHtm, StringComparison.Ordinal))
                    return Fail("default GuideDocument output differs from legacy overload output");

                Console.WriteLine("SELFTEST PASS  ->  " + mht + "  (" + fi.Length + " bytes)");
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex.ToString());
            }
        }

        /// <summary>
        /// Simulates what GuideEditorForm produces and checks every editing feature lands in
        /// the output. Returns null on success, else a failure message.
        /// </summary>
        private static string GuidePass(IList<RecordedStep> source, DateTime started, DateTime stopped,
            RecorderSettings settings)
        {
            // Work on copies so the shared fixture stays pristine for the other passes.
            var edited = new List<RecordedStep>();
            foreach (var s in source) edited.Add(CloneStep(s));

            // Delete step 2 (the double click), rewrite step 1's text, insert a text step
            // after it, crop step 3's image and redact two regions of it.
            edited.RemoveAt(1);
            edited[0].CustomDescription = "Click the Start button in the taskbar.\nWait for the menu.";
            edited.Insert(1, new RecordedStep
            {
                Kind = StepKind.Text,
                Time = FixedTime,
                Comment = "Now open File Explorer. <Tip> use Win+E."
            });
            var shot = edited[2];
            shot.Crop = new Rectangle(100, 0, 700, 400);
            shot.Redactions.Add(new Redaction(new Rectangle(500, 60, 300, 28), RedactionKind.Solid));
            shot.Redactions.Add(new Redaction(new Rectangle(120, 200, 200, 100), RedactionKind.Pixelate));

            var doc = new GuideDocument(edited, started, stopped)
            {
                Title = "Open File Explorer & find Documents",
                Intro = "A short guide.\nSecond line of the description."
            };
            doc.Renumber();

            string dir = Path.Combine(Path.GetTempPath(), "psrclone_selftest_guide");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            string htm = ReportWriter.SaveFolder(dir, doc, settings);
            if (!File.Exists(htm)) return "guide: htm not written";
            string text = File.ReadAllText(htm);
            string[] jpgs = Directory.GetFiles(dir, "*.jpeg");

            // 4 source steps - 1 deleted = 3 with images; the text step adds none.
            if (jpgs.Length != 3) return "guide: expected 3 jpegs, got " + jpgs.Length;
            if (Path.GetFileName(htm) != "Guide_Open_File_Explorer_find_Documents.htm")
                return "guide: unexpected file name " + Path.GetFileName(htm);

            // The cropped step's image must have the crop's dimensions.
            using (var img = new Bitmap(Path.Combine(dir, "screenshot2.jpeg")))
            {
                if (img.Width != 700 || img.Height != 400)
                    return "guide: crop not applied, image is " + img.Width + "x" + img.Height;

                // Solid redaction (500,60)-(800,88) => cropped (400,60)-(700,88): must be black.
                var px = img.GetPixel(550, 74);
                if (px.R > 16 || px.G > 16 || px.B > 16)
                    return "guide: solid redaction missing, pixel is " + px;

                // Un-redacted area must still carry the fixture's light-grey background.
                var bg = img.GetPixel(300, 300);
                if (bg.R < 200)
                    return "guide: crop/redaction clobbered untouched pixels, pixel is " + bg;
            }
            Directory.Delete(dir, true);

            foreach (var marker in new[]
            {
                "<title>Guide: Open File Explorer &amp; find Documents</title>",
                "<h1>Guide: Open File Explorer &amp; find Documents</h1>",
                "A short guide.<br>Second line of the description.",
                "Step 1: Click the Start button in the taskbar.<br>Wait for the menu.",
                "<div class=\"step text\"><div class=\"stephdr\"><span>Step 2:</span></div>",
                "<img class=\"shot\" src=\"screenshot1.jpeg\"",   // loose files: bare names, not cid:
                "Now open File Explorer. &lt;Tip&gt; use Win+E.",
                "Step 3: User keyboard input",
                "Step 4: User Comment:",
                "break-inside:avoid",
                "page-break-inside:avoid",
                "max-height:7.2in",
                "@page{margin:0.6in;}"
            })
            {
                if (text.IndexOf(marker, StringComparison.Ordinal) < 0)
                    return "guide: missing marker: " + marker;
            }

            foreach (var marker in new[]
            {
                "Recorded Problem Steps",
                "User double click on",
                "User left click on",   // rewritten away
                "Step 5:"
            })
            {
                if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return "guide: marker should be absent: " + marker;
            }

            // The .mht Subject must carry the guide heading too.
            string mhtPath = Path.Combine(Path.GetTempPath(), "psrclone_selftest_guide.mht");
            ReportWriter.Save(mhtPath, doc, settings);
            string mhtText = File.ReadAllText(mhtPath);
            if (mhtText.IndexOf("Subject: Guide: Open File Explorer & find Documents", StringComparison.Ordinal) < 0)
                return "guide: .mht Subject header does not carry the title";
            if (CountOccurrences(mhtText, "Content-Type: image/jpeg") != 3)
                return "guide: .mht should embed 3 images";
            File.Delete(mhtPath);

            return null;
        }

        private static RecordedStep CloneStep(RecordedStep s)
        {
            return new RecordedStep
            {
                Index = s.Index,
                Kind = s.Kind,
                Time = s.Time,
                Action = s.Action,
                ElementName = s.ElementName,
                ElementType = s.ElementType,
                WindowName = s.WindowName,
                ProgramName = s.ProgramName,
                TypedText = s.TypedText,
                Comment = s.Comment,
                Screenshot = s.Screenshot, // shared, read-only
                Highlight = s.Highlight,
                Cursor = s.Cursor
            };
        }

        /// <summary>
        /// Renders a folder dump into a temp directory, verifies the loose image count,
        /// returns the raw (un-encoded) HTML text and removes the directory.
        /// </summary>
        private static string DumpAndRead(string name, IList<RecordedStep> steps,
            DateTime started, DateTime stopped, RecorderSettings settings)
        {
            return DumpAndRead(name, new GuideDocument(steps, started, stopped), settings);
        }

        private static string DumpAndRead(string name, GuideDocument doc, RecorderSettings settings)
        {
            string dir = Path.Combine(Path.GetTempPath(), name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            string htm = ReportWriter.SaveFolder(dir, doc, settings);
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
