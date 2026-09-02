using System;
using System.Collections.Generic;
using System.Text;

namespace PsrClone
{
    /// <summary>
    /// Everything the report writer needs to render one guide: the user-facing title and
    /// intro plus the (already edited and renumbered) step list. When Title and Intro are
    /// left at their defaults the output is identical to the classic psr.exe report.
    /// </summary>
    public sealed class GuideDocument
    {
        public const string DefaultTitle = "Recorded Problem Steps";
        public const string DefaultIntro =
            "This file contains all of the recorded problem steps and information captured to help describe your recorded steps.";

        public string Title = DefaultTitle;
        public string Intro = DefaultIntro;
        public IList<RecordedStep> Steps = new List<RecordedStep>();
        public DateTime Started;
        public DateTime Stopped;

        public GuideDocument() { }

        public GuideDocument(IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            Steps = steps ?? new List<RecordedStep>();
            Started = started;
            Stopped = stopped;
        }

        /// <summary>True when the user supplied a title of their own.</summary>
        public bool HasCustomTitle
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Title)
                    && !string.Equals(Title.Trim(), DefaultTitle, StringComparison.Ordinal);
            }
        }

        /// <summary>"Guide: {title}" for user titles, or the classic heading otherwise.</summary>
        public string Heading
        {
            get { return HasCustomTitle ? "Guide: " + Title.Trim() : DefaultTitle; }
        }

        /// <summary>The intro paragraph, falling back to the classic sentence when blank.</summary>
        public string EffectiveIntro
        {
            get { return string.IsNullOrWhiteSpace(Intro) ? DefaultIntro : Intro.Trim(); }
        }

        /// <summary>Reassigns Index 1..n in list order so deletions and insertions leave no gaps.</summary>
        public void Renumber()
        {
            for (int i = 0; i < Steps.Count; i++) Steps[i].Index = i + 1;
        }

        /// <summary>
        /// A file-system-safe base name derived from the title, e.g. "Guide_Reset_a_password".
        /// Falls back to the classic timestamped name when there is no custom title.
        /// </summary>
        public string SuggestedFileBase()
        {
            if (!HasCustomTitle)
                return "RecordedSteps_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var sb = new StringBuilder("Guide_");
            bool lastUnderscore = false;
            foreach (char c in Title.Trim())
            {
                bool ok = char.IsLetterOrDigit(c) || c == '-';
                if (ok)
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
                if (sb.Length >= 80) break;
            }
            string s = sb.ToString().TrimEnd('_');
            return s.Length > 6 ? s : "Guide_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
    }
}
