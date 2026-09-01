using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace PsrClone
{
    /// <summary>
    /// Writes a standalone MHTML (.mht) document with embedded screenshots, or a folder of
    /// loose files. Every output path renders through <see cref="BuildHtml"/>, and every
    /// image renders through <see cref="RenderPreview"/> (shared with the guide editor so
    /// what you see in the editor is exactly what lands in the file).
    /// </summary>
    public static class ReportWriter
    {
        private const string Boundary = "----=_NextPart_PSRClone_Recording";

        // ---------------------------------------------------------------------------------
        // Public entry points. The IList<RecordedStep> overloads are kept for callers that
        // never touch the editor (SelfTest, RecordTest); they wrap a default GuideDocument.
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Writes the report as a single self-contained .mht. <paramref name="path"/> is
        /// treated as a base path whose extension is normalized to .mht, so callers must
        /// use the returned path rather than assuming the one they passed in.
        /// </summary>
        public static string Save(string path, IList<RecordedStep> steps, DateTime started, DateTime stopped,
            RecorderSettings settings)
        {
            return Save(path, new GuideDocument(steps, started, stopped), settings);
        }

        public static string Save(string path, GuideDocument doc, RecorderSettings settings)
        {
            string mhtPath = Path.ChangeExtension(path, ".mht");
            SaveMhtFile(mhtPath, doc, settings);
            return mhtPath;
        }

        public static void SaveMhtFile(string mhtPath, IList<RecordedStep> steps, DateTime started, DateTime stopped,
            RecorderSettings settings)
        {
            SaveMhtFile(mhtPath, new GuideDocument(steps, started, stopped), settings);
        }

        public static void SaveMhtFile(string mhtPath, GuideDocument doc, RecorderSettings settings)
        {
            string mht = BuildMht(doc, settings);

            var dir = Path.GetDirectoryName(mhtPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(mhtPath, mht, new UTF8Encoding(false));
        }

        // Backwards compatible alias - rewritten for C# 5 compatibility
        public static void SaveMht(string mhtPath, IList<RecordedStep> steps, DateTime started, DateTime stopped,
            RecorderSettings settings)
        {
            SaveMhtFile(mhtPath, steps, started, stopped, settings);
        }

        /// <summary>
        /// Dumps a browsable report as loose files into <paramref name="dir"/>:
        /// a single HTML file plus one JPEG per screenshot (no zip / no MHTML).
        /// Returns the path to the generated HTML file.
        /// </summary>
        public static string SaveFolder(string dir, IList<RecordedStep> steps, DateTime started, DateTime stopped,
            RecorderSettings settings)
        {
            return SaveFolder(dir, new GuideDocument(steps, started, stopped), settings);
        }

        public static string SaveFolder(string dir, GuideDocument doc, RecorderSettings settings)
        {
            Directory.CreateDirectory(dir);

            var images = new List<string>();
            var imageData = new List<byte[]>();
            // Loose files: images are siblings of the .htm, so reference them by bare
            // file name. (The "cid:" scheme only resolves inside an .mht.)
            string html = BuildHtml(doc, images, imageData, settings, string.Empty);

            for (int i = 0; i < images.Count; i++)
                File.WriteAllBytes(Path.Combine(dir, images[i]), imageData[i]);

            string baseName = doc.HasCustomTitle
                ? doc.SuggestedFileBase()
                : "RecordedSteps_" + doc.Started.ToString("yyyyMMdd_HHmmss");
            string htmlPath = Path.Combine(dir, baseName + ".htm");

            File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
            return htmlPath;
        }

        /// <summary>
        /// Opens Windows Explorer showing the folder that contains the specified file.
        /// This avoids launching the default browser to view the .mht.
        /// </summary>
        public static void ShowFolderContainingFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string full = Path.GetFullPath(filePath);
            string folder = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(folder)) return;

            System.Diagnostics.Process.Start("explorer.exe",
                "/select,\"" + full.Replace("\"", "") + "\"");
        }

        /// <summary>
        /// Opens the file with the default associated app (browser for .mht, etc).
        /// </summary>
        public static void OpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }

        // ---------------------------------------------------------------------------------
        // MHTML envelope
        // ---------------------------------------------------------------------------------

        private static string BuildMht(GuideDocument doc, RecorderSettings settings)
        {
            var images = new List<string>();
            var imageData = new List<byte[]>();

            string html = BuildHtml(doc, images, imageData, settings, "cid:");

            var sb = new StringBuilder();

            sb.Append("From: <Saved by PSR Clone>\r\n");
            sb.Append("Subject: ").Append(MimeHeaderSafe(doc.Heading)).Append("\r\n");
            sb.Append("Date: ").Append(doc.Started.ToString("ddd, dd MMM yyyy HH:mm:ss")).Append(" -0000\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append("Content-Type: multipart/related;\r\n");
            sb.Append("\ttype=\"text/html\";\r\n");
            sb.Append("\tboundary=\"").Append(Boundary).Append("\"\r\n");
            sb.Append("X-Generator: PSR Clone\r\n");
            sb.Append("\r\n");
            sb.Append("This is a multi-part message in MIME format.\r\n\r\n");

            sb.Append("--").Append(Boundary).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=\"utf-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: quoted-printable\r\n");
            sb.Append("Content-ID: <text.html>\r\n");
            sb.Append("\r\n");
            sb.Append(QuotedPrintable.Encode(html));
            sb.Append("\r\n");

            for (int i = 0; i < images.Count; i++)
            {
                sb.Append("--").Append(Boundary).Append("\r\n");
                sb.Append("Content-Type: image/jpeg\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n");
                sb.Append("Content-ID: <").Append(images[i]).Append(">\r\n");
                sb.Append("\r\n");
                sb.Append(ToBase64Lines(imageData[i]));
                sb.Append("\r\n");
            }

            sb.Append("--").Append(Boundary).Append("--\r\n");
            return sb.ToString();
        }

        /// <summary>Keeps a user title on one ASCII-ish header line; MIME headers cannot wrap freely.</summary>
        private static string MimeHeaderSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return GuideDocument.DefaultTitle;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\r' || c == '\n') sb.Append(' ');
                else if (c < 32 || c > 126) sb.Append('?');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------
        // HTML
        // ---------------------------------------------------------------------------------

        private static string BuildHtml(
            GuideDocument doc,
            List<string> images,
            List<byte[]> imageData,
            RecorderSettings settings,
            string imageSrcPrefix)
        {
            // Single normalization point: every path into the report renders through here.
            if (settings == null) settings = new RecorderSettings();
            if (doc == null) doc = new GuideDocument();
            IList<RecordedStep> steps = doc.Steps;
            DateTime started = doc.Started;
            DateTime stopped = doc.Stopped;

            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">");
            sb.Append("<title>").Append(Html(doc.Heading)).Append("</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12pt;color:#000;margin:16px;background:#fff;}");
            sb.Append("h1{font-size:18pt;} h2{font-size:14pt;border-bottom:1px solid #ccc;padding-bottom:4px;margin-top:28px;}");
            sb.Append(".intro{color:#333;white-space:pre-wrap;} .step{margin:22px 0;} .stephdr{font-weight:bold;font-size:12pt;} ");
            sb.Append(".shot{border:1px solid #888;margin-top:8px;max-width:100%;} .meta{color:#555;font-size:10pt;} ");
            sb.Append(".comment{color:#a00;font-weight:bold;} .details p{margin:4px 0;} .idx{color:#0645ad;} ");
            sb.Append(".step.text .stepbody{margin:6px 0 0 0;} ");
            sb.Append("table.env{border-collapse:collapse;} table.env td{border:1px solid #ddd;padding:4px 8px;font-size:10pt;}");
            // Print layout: keep each step's heading and screenshot on one page. The image
            // max-height is what makes that achievable: a full-monitor capture is taller than
            // a Letter/A4 page, and break-inside cannot help a block that does not fit at all.
            // Both the modern and legacy page-break spellings are emitted because Edge/Chrome
            // honour the former and Word (which opens .mht by default) honours the latter.
            sb.Append("@page{margin:0.6in;} ");
            sb.Append("@media print{");
            sb.Append("body{margin:0;} ");
            sb.Append(".step{break-inside:avoid;page-break-inside:avoid;} ");
            sb.Append(".shot{max-width:100%;max-height:7.2in;width:auto;height:auto;} ");
            sb.Append("h1,h2{break-after:avoid;page-break-after:avoid;} ");
            sb.Append("table.env{break-inside:avoid;page-break-inside:avoid;}");
            sb.Append("}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>").Append(Html(doc.Heading)).Append("</h1>");
            sb.Append("<p class=\"intro\">").Append(HtmlMultiline(doc.EffectiveIntro)).Append("</p>");
            sb.Append("<p class=\"meta\">Recording session: ")
              .Append(Html(started.ToString("F"))).Append(" &ndash; ")
              .Append(Html(stopped == DateTime.MinValue ? DateTime.Now.ToString("F") : stopped.ToString("F")))
              .Append("</p>");

            sb.Append("<h2>Steps</h2>");

            int shotCounter = 0;
            if (steps.Count == 0)
            {
                sb.Append("<p><i>No steps were recorded.</i></p>");
            }
            else
            {
                foreach (var step in steps)
                {
                    string prefix = "Step " + step.Index + ": ";

                    if (step.Kind == StepKind.Text)
                    {
                        // Author-inserted explanatory step: heading + free text, never an image.
                        sb.Append("<div class=\"step text\">");
                        sb.Append("<div class=\"stephdr\"><span>").Append(Html(prefix.TrimEnd())).Append("</span></div>");
                        sb.Append("<p class=\"stepbody\">").Append(HtmlMultiline(step.DisplayDescription())).Append("</p>");
                        sb.Append("</div>");
                        continue;
                    }

                    sb.Append("<div class=\"step\">");

                    sb.Append("<div class=\"stephdr\">")
                      .Append(step.Kind == StepKind.Comment ? "<span class=\"comment\">" : "<span>")
                      .Append(Html(prefix))
                      .Append(TimePrefix(step, settings.IncludeStepTimestamps))
                      .Append(HtmlMultiline(step.DisplayDescription()))
                      .Append("</span></div>");

                    if (step.Screenshot != null)
                    {
                        byte[] jpg = RenderAnnotated(step);
                        string cid = "screenshot" + (++shotCounter) + ".jpeg";

                        images.Add(cid);
                        imageData.Add(jpg);

                        sb.Append("<img class=\"shot\" src=\"")
                          .Append(imageSrcPrefix)
                          .Append(cid)
                          .Append("\" alt=\"Step ")
                          .Append(step.Index)
                          .Append("\">");
                    }

                    sb.Append("</div>");
                }
            }

            if (settings.IncludeAdditionalDetails)
            {
                sb.Append("<h2>Additional Details</h2><div class=\"details\">");
                sb.Append("<p>The following section lists each recorded step as text.</p>");

                foreach (var step in steps)
                {
                    sb.Append("<p><span class=\"idx\">Step ").Append(step.Index).Append(":</span> ")
                      .Append(TimePrefix(step, settings.IncludeStepTimestamps))
                      .Append(HtmlMultiline(step.DisplayDescription()));

                    if (!string.IsNullOrEmpty(step.ProgramName))
                        sb.Append("<br><span class=\"meta\">Program: ").Append(Html(step.ProgramName)).Append("</span>");

                    sb.Append("</p>");
                }
                sb.Append("</div>");
            }

            if (settings.IncludeEnvironment)
            {
                sb.Append("<h2>Recording Environment</h2>");
                sb.Append("<table class=\"env\">");
                Row(sb, "Operating System", GetOsString());
                Row(sb, "Computer", Environment.MachineName);
                Row(sb, "User", Environment.UserName);
                Row(sb, "Screen resolution", GetScreens());
                Row(sb, "Total steps", steps.Count.ToString());
                Row(sb, "Recorder", "PSR Clone (Problem Steps Recorder replacement)");
                sb.Append("</table>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string k, string v)
        {
            sb.Append("<tr><td><b>").Append(Html(k)).Append("</b></td><td>").Append(Html(v)).Append("</td></tr>");
        }

        /// <summary>
        /// The "(3/4/2026 5:06 AM) " prefix that precedes a step description, or an empty
        /// string when per-step timestamps are switched off. Both emission sites share this
        /// so they cannot drift apart. The text preceding each call site already ends in a
        /// space, so omitting the prefix leaves no double space behind.
        /// </summary>
        private static string TimePrefix(RecordedStep step, bool include)
        {
            if (!include) return string.Empty;
            return "(" + Html(step.Time.ToString("g")) + ") ";
        }

        // ---------------------------------------------------------------------------------
        // Image rendering
        // ---------------------------------------------------------------------------------

        private static byte[] RenderAnnotated(RecordedStep step)
        {
            using (var bmp = RenderPreview(step))
                return ToJpeg(bmp, 82L);
        }

        /// <summary>
        /// Renders the step's screenshot exactly as it will appear in the report: element
        /// highlight and click marker, then redactions, then the crop. Always returns a new
        /// bitmap the caller owns; <see cref="RecordedStep.Screenshot"/> is never modified.
        /// Returns null when the step has no screenshot.
        /// </summary>
        public static Bitmap RenderPreview(RecordedStep step)
        {
            if (step == null || step.Screenshot == null) return null;

            Bitmap full = new Bitmap(step.Screenshot);
            try
            {
                using (var g = Graphics.FromImage(full))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    if (step.Highlight != Rectangle.Empty && step.Highlight.Width > 0 && step.Highlight.Height > 0)
                    {
                        var rect = step.Highlight;
                        rect.Inflate(2, 2);

                        using (var pen = new Pen(Color.FromArgb(255, 0, 120, 215), 3f))
                            g.DrawRectangle(pen, rect);

                        using (var glow = new Pen(Color.FromArgb(90, 0, 120, 215), 7f))
                            g.DrawRectangle(glow, rect);
                    }

                    if (step.Kind != StepKind.Comment && step.Cursor != Point.Empty)
                    {
                        int r = 14;
                        var c = step.Cursor;

                        using (var b = new SolidBrush(Color.FromArgb(70, 255, 210, 0)))
                            g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);

                        using (var pen = new Pen(Color.FromArgb(220, 230, 140, 0), 2f))
                            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                    }

                    // Redactions go on top of the annotations so a marker can never leak
                    // the shape of what was hidden.
                    if (step.Redactions != null)
                    {
                        foreach (var red in step.Redactions)
                            ApplyRedaction(full, g, red);
                    }
                }

                Rectangle crop = step.Crop;
                if (crop != Rectangle.Empty)
                {
                    crop.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                    if (crop.Width >= 1 && crop.Height >= 1
                        && (crop.Width < full.Width || crop.Height < full.Height))
                    {
                        Bitmap cropped = full.Clone(crop, full.PixelFormat);
                        full.Dispose();
                        return cropped;
                    }
                }

                Bitmap result = full;
                full = null; // ownership passes to caller
                return result;
            }
            finally
            {
                if (full != null) full.Dispose();
            }
        }

        private static void ApplyRedaction(Bitmap bmp, Graphics g, Redaction red)
        {
            if (red == null) return;
            Rectangle r = red.Rect;
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width <= 0 || r.Height <= 0) return;

            if (red.Kind == RedactionKind.Solid)
            {
                g.FillRectangle(Brushes.Black, r);
                return;
            }

            // Pixelate: shrink the region to a handful of cells (averaging pixels), then blow
            // it back up with nearest-neighbour sampling so each cell becomes a flat block.
            // Block size scales with the capture so the mosaic reads the same at any DPI.
            int block = Math.Max(10, bmp.Width / 120);
            int cellsW = Math.Max(1, (r.Width + block - 1) / block);
            int cellsH = Math.Max(1, (r.Height + block - 1) / block);

            using (var small = new Bitmap(cellsW, cellsH, PixelFormat.Format24bppRgb))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    sg.PixelOffsetMode = PixelOffsetMode.Half;
                    sg.CompositingQuality = CompositingQuality.HighQuality;
                    sg.DrawImage(bmp, new Rectangle(0, 0, cellsW, cellsH), r, GraphicsUnit.Pixel);
                }

                var oldInterp = g.InterpolationMode;
                var oldOffset = g.PixelOffsetMode;
                var oldSmooth = g.SmoothingMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(small, r, new Rectangle(0, 0, cellsW, cellsH), GraphicsUnit.Pixel);
                g.InterpolationMode = oldInterp;
                g.PixelOffsetMode = oldOffset;
                g.SmoothingMode = oldSmooth;
            }
        }

        private static byte[] ToJpeg(Bitmap bmp, long quality)
        {
            var codec = GetEncoder(ImageFormat.Jpeg);
            var pars = new EncoderParameters(1);
            pars.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, codec, pars);
                return ms.ToArray();
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == format.Guid) return c;
            return null;
        }

        private static string ToBase64Lines(byte[] data)
        {
            string b64 = Convert.ToBase64String(data);
            var sb = new StringBuilder(b64.Length + b64.Length / 76 + 8);

            for (int i = 0; i < b64.Length; i += 76)
            {
                sb.Append(b64, i, Math.Min(76, b64.Length - i));
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------
        // Text helpers
        // ---------------------------------------------------------------------------------

        private static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Escapes like <see cref="Html"/> and turns line breaks into &lt;br&gt;.</summary>
        private static string HtmlMultiline(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return Html(s.Replace("\r\n", "\n").Replace('\r', '\n')).Replace("\n", "<br>");
        }

        private static string GetOsString()
        {
            try { return Environment.OSVersion.VersionString; }
            catch { return "Windows"; }
        }

        private static string GetScreens()
        {
            try
            {
                var parts = new List<string>();
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                    parts.Add(s.Bounds.Width + "x" + s.Bounds.Height);

                return string.Join(", ", parts);
            }
            catch { return ""; }
        }
    }

    internal static class QuotedPrintable
    {
        public static string Encode(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var sb = new StringBuilder();

            int lineLen = 0;
            foreach (byte b in bytes)
            {
                bool literal = (b >= 33 && b <= 126 && b != 61) || b == 32 || b == 9;

                if (b == (byte)'\n')
                {
                    sb.Append("\r\n");
                    lineLen = 0;
                    continue;
                }

                if (b == (byte)'\r') continue;

                string chunk = literal ? ((char)b).ToString() : "=" + b.ToString("X2");

                if (lineLen + chunk.Length > 74)
                {
                    sb.Append("=\r\n");
                    lineLen = 0;
                }

                sb.Append(chunk);
                lineLen += chunk.Length;
            }

            return sb.ToString();
        }
    }
}
