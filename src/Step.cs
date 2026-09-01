using System;
using System.Collections.Generic;
using System.Drawing;

namespace PsrClone
{
    public enum StepKind
    {
        Mouse,
        Keyboard,
        Comment,
        /// <summary>A user-authored text-only step (no screenshot), inserted in the editor.</summary>
        Text
    }

    public enum RedactionKind
    {
        /// <summary>Opaque black block. Irreversible.</summary>
        Solid,
        /// <summary>Coarse mosaic. Softer looking; not suitable for secrets.</summary>
        Pixelate
    }

    /// <summary>A region of a screenshot to obscure, in uncropped screenshot coordinates.</summary>
    public sealed class Redaction
    {
        public Rectangle Rect;
        public RedactionKind Kind;

        public Redaction(Rectangle rect, RedactionKind kind)
        {
            Rect = rect;
            Kind = kind;
        }
    }

    /// <summary>A single recorded step, mirroring the information psr.exe captures.</summary>
    public sealed class RecordedStep
    {
        public int Index;
        public StepKind Kind;
        public DateTime Time = DateTime.Now;

        public string Action;        // "left click", "double click", "right click", "left drag", "mouse wheel"
        public string ElementName;   // UI Automation element name
        public string ElementType;   // UI Automation control type (localized)
        public string WindowName;    // top-level window title
        public string ProgramName;   // process / executable name
        public string TypedText;     // aggregated keyboard input
        public string Comment;       // user comment text (also the body of a Text step)

        public Bitmap Screenshot;    // clean captured monitor bitmap (may be null); never drawn on
        public Rectangle Highlight;  // element rectangle in screenshot coordinates (Empty if unknown)
        public Point Cursor = Point.Empty; // cursor location in screenshot coordinates

        // ---- Editor-applied, non-destructive adjustments. Screenshot stays pristine. ----

        /// <summary>User-rewritten description; null/empty means use <see cref="BuildDescription"/>.</summary>
        public string CustomDescription;

        /// <summary>Region of the screenshot to keep, in screenshot coordinates. Empty = no crop.</summary>
        public Rectangle Crop = Rectangle.Empty;

        /// <summary>Regions to obscure, in uncropped screenshot coordinates.</summary>
        public List<Redaction> Redactions = new List<Redaction>();

        /// <summary>True when the editor has altered the image in any way.</summary>
        public bool HasImageEdits
        {
            get { return Crop != Rectangle.Empty || Redactions.Count > 0; }
        }

        /// <summary>The text shown in the report: the user's rewrite if present, else the generated one.</summary>
        public string DisplayDescription()
        {
            if (!string.IsNullOrEmpty(CustomDescription)) return CustomDescription;
            return BuildDescription();
        }

        /// <summary>Produces the "User left click on ..." style description used in the report.</summary>
        public string BuildDescription()
        {
            switch (Kind)
            {
                case StepKind.Text:
                    return Comment ?? string.Empty;

                case StepKind.Comment:
                    return "User Comment: \"" + (Comment ?? string.Empty) + "\"";

                case StepKind.Keyboard:
                {
                    string target = DescribeTarget();
                    string text = string.IsNullOrEmpty(TypedText)
                        ? string.Empty
                        : " [" + TypedText + "]";
                    return "User keyboard input" + (target.Length > 0 ? " on " + target : string.Empty) + text;
                }

                case StepKind.Mouse:
                default:
                {
                    string target = DescribeTarget();
                    string act = string.IsNullOrEmpty(Action) ? "click" : Action;
                    return "User " + act + (target.Length > 0 ? " on " + target : string.Empty);
                }
            }
        }

        private string DescribeTarget()
        {
            string s = string.Empty;
            if (!string.IsNullOrEmpty(ElementName))
            {
                s = "\"" + ElementName + "\"";
                if (!string.IsNullOrEmpty(ElementType))
                    s += " (" + ElementType + ")";
            }
            if (!string.IsNullOrEmpty(WindowName))
            {
                if (s.Length > 0) s += " in ";
                s += "\"" + WindowName + "\" (window)";
            }
            return s;
        }
    }
}
