using System;
using System.Drawing;

namespace PsrClone
{
    public enum StepKind
    {
        Mouse,
        Keyboard,
        Comment
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
        public string Comment;       // user comment text

        public Bitmap Screenshot;    // captured monitor bitmap (may be null)
        public Rectangle Highlight;  // element rectangle in screenshot coordinates (Empty if unknown)
        public Point Cursor = Point.Empty; // cursor location in screenshot coordinates

        /// <summary>Produces the "User left click on ..." style description used in the report.</summary>
        public string BuildDescription()
        {
            switch (Kind)
            {
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
