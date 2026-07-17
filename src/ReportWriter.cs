using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PsrClone
{
    /// <summary>
    /// Renders recorded steps into an MHTML (.mht) document embedded in a .zip file,
    /// closely matching the output of psr.exe.
    /// </summary>
    public static class ReportWriter
    {
        private const string Boundary = "----=_NextPart_PSRClone_Recording";

        public static void Save(string zipPath, IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            string mht = BuildMht(steps, started, stopped);
            string tempMht = Path.Combine(Path.GetTempPath(),
                "PSRClone_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mht");
            File.WriteAllText(tempMht, mht, new UTF8Encoding(false));

            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempMht, "Recording_" +
                    started.ToString("yyyyMMdd_HHmmss") + ".mht", CompressionLevel.Optimal);
            }
            try { File.Delete(tempMht); } catch { }
        }

        /// <summary>Optionally save the raw .mht (used by tests / advanced users).</summary>
        public static void SaveMht(string mhtPath, IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            File.WriteAllText(mhtPath, BuildMht(steps, started, stopped), new UTF8Encoding(false));
        }

        /// <summary>
        /// Dumps a browsable report as loose files into <paramref name="dir"/>: a single
        /// HTML file plus one JPEG per screenshot (no zip / no MHTML packaging).
        /// Returns the path to the generated HTML file.
        /// </summary>
        public static string SaveFolder(string dir, IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            Directory.CreateDirectory(dir);
            var images = new List<string>();
            var imageData = new List<byte[]>();
            string html = BuildHtml(steps, started, stopped, images, imageData);

            for (int i = 0; i < images.Count; i++)
                File.WriteAllBytes(Path.Combine(dir, images[i]), imageData[i]);

            string htmlPath = Path.Combine(dir,
                "RecordedSteps_" + started.ToString("yyyyMMdd_HHmmss") + ".htm");
            File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
            return htmlPath;
        }

        private static string BuildMht(IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            var images = new List<string>();     // content-ids
            var imageData = new List<byte[]>();

            string html = BuildHtml(steps, started, stopped, images, imageData);

            var sb = new StringBuilder();
            sb.Append("From: <Saved by PSR Clone>\r\n");
            sb.Append("Subject: Recorded Problem Steps\r\n");
            sb.Append("Date: ").Append(started.ToString("ddd, dd MMM yyyy HH:mm:ss")).Append(" -0000\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append("Content-Type: multipart/related;\r\n");
            sb.Append("\ttype=\"text/html\";\r\n");
            sb.Append("\tboundary=\"").Append(Boundary).Append("\"\r\n");
            sb.Append("X-Generator: PSR Clone\r\n");
            sb.Append("\r\n");
            sb.Append("This is a multi-part message in MIME format.\r\n\r\n");

            // HTML part
            sb.Append("--").Append(Boundary).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=\"utf-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: quoted-printable\r\n");
            sb.Append("Content-Location: file:///C:/Recording.htm\r\n");
            sb.Append("\r\n");
            sb.Append(QuotedPrintable.Encode(html));
            sb.Append("\r\n");

            // Image parts
            for (int i = 0; i < images.Count; i++)
            {
                sb.Append("--").Append(Boundary).Append("\r\n");
                sb.Append("Content-Type: image/jpeg\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n");
                sb.Append("Content-Location: file:///C:/").Append(images[i]).Append("\r\n");
                sb.Append("\r\n");
                sb.Append(ToBase64Lines(imageData[i]));
                sb.Append("\r\n");
            }

            sb.Append("--").Append(Boundary).Append("--\r\n");
            return sb.ToString();
        }

        private static string BuildHtml(IList<RecordedStep> steps, DateTime started, DateTime stopped,
            List<string> images, List<byte[]> imageData)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">");
            sb.Append("<title>Recorded Problem Steps</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:12pt;color:#000;margin:16px;background:#fff;}");
            sb.Append("h1{font-size:18pt;} h2{font-size:14pt;border-bottom:1px solid #ccc;padding-bottom:4px;margin-top:28px;}");
            sb.Append(".intro{color:#333;} .step{margin:22px 0;} .stephdr{font-weight:bold;font-size:12pt;} ");
            sb.Append(".shot{border:1px solid #888;margin-top:8px;max-width:100%;} .meta{color:#555;font-size:10pt;} ");
            sb.Append(".comment{color:#a00;font-weight:bold;} .details p{margin:4px 0;} .idx{color:#0645ad;} ");
            sb.Append("table.env{border-collapse:collapse;} table.env td{border:1px solid #ddd;padding:4px 8px;font-size:10pt;}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>Recorded Problem Steps</h1>");
            sb.Append("<p class=\"intro\">This file contains all of the recorded problem steps and information ")
              .Append("captured to help describe your recorded steps.</p>");
            sb.Append("<p class=\"meta\">Recording session: ")
              .Append(Html(started.ToString("F"))).Append(" &ndash; ")
              .Append(Html(stopped == DateTime.MinValue ? DateTime.Now.ToString("F") : stopped.ToString("F")))
              .Append("</p>");

            // ----- Steps with screenshots -----
            sb.Append("<h2>Steps</h2>");
            if (steps.Count == 0)
            {
                sb.Append("<p><i>No steps were recorded.</i></p>");
            }

            int shotCounter = 0;
            foreach (var step in steps)
            {
                sb.Append("<div class=\"step\">");
                string prefix = "Step " + step.Index + ": ";
                sb.Append("<div class=\"stephdr\">")
                  .Append(step.Kind == StepKind.Comment ? "<span class=\"comment\">" : "<span>")
                  .Append(Html(prefix))
                  .Append("(").Append(Html(step.Time.ToString("g"))).Append(") ")
                  .Append(Html(step.BuildDescription()))
                  .Append("</span></div>");

                if (step.Screenshot != null)
                {
                    byte[] jpg = RenderAnnotated(step);
                    string cid = "screenshot" + (++shotCounter) + ".jpeg";
                    images.Add(cid);
                    imageData.Add(jpg);
                    sb.Append("<img class=\"shot\" src=\"").Append(cid).Append("\" alt=\"Step ")
                      .Append(step.Index).Append("\">");
                }
                sb.Append("</div>");
            }

            // ----- Additional details (text recap) -----
            sb.Append("<h2>Additional Details</h2>");
            sb.Append("<div class=\"details\">");
            sb.Append("<p>The following section lists each recorded step as text.</p>");
            foreach (var step in steps)
            {
                sb.Append("<p><span class=\"idx\">Step ").Append(step.Index).Append(":</span> ")
                  .Append("(").Append(Html(step.Time.ToString("g"))).Append(") ")
                  .Append(Html(step.BuildDescription()));
                if (!string.IsNullOrEmpty(step.ProgramName))
                    sb.Append("<br><span class=\"meta\">Program: ").Append(Html(step.ProgramName)).Append("</span>");
                sb.Append("</p>");
            }
            sb.Append("</div>");

            // ----- Environment -----
            sb.Append("<h2>Recording Environment</h2>");
            sb.Append("<table class=\"env\">");
            Row(sb, "Operating System", GetOsString());
            Row(sb, "Computer", Environment.MachineName);
            Row(sb, "User", Environment.UserName);
            Row(sb, "Screen resolution", GetScreens());
            Row(sb, "Total steps", steps.Count.ToString());
            Row(sb, "Recorder", "PSR Clone (Problem Steps Recorder replacement)");
            sb.Append("</table>");

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string k, string v)
        {
            sb.Append("<tr><td><b>").Append(Html(k)).Append("</b></td><td>").Append(Html(v)).Append("</td></tr>");
        }

        /// <summary>Draws the element highlight and cursor marker onto a copy of the screenshot.</summary>
        private static byte[] RenderAnnotated(RecordedStep step)
        {
            using (var bmp = new Bitmap(step.Screenshot))
            {
                using (var g = Graphics.FromImage(bmp))
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
                }
                return ToJpeg(bmp, 82L);
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
                return string.Join(", ", parts.ToArray());
            }
            catch { return ""; }
        }
    }

    /// <summary>Minimal quoted-printable encoder for the HTML MIME part.</summary>
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

                string chunk;
                if (literal) chunk = ((char)b).ToString();
                else chunk = "=" + b.ToString("X2");

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
