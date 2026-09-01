using System;

namespace PsrClone
{
    /// <summary>Recorder and report options, mirroring psr.exe's Settings dialog.</summary>
    public sealed class RecorderSettings
    {
        /// <summary>Enable capture of screenshots (psr.exe: "Enable screen capture").</summary>
        public bool CaptureScreenshots = true;

        /// <summary>Maximum number of most-recent screen captures to keep (default 500).</summary>
        public int MaxScreenshots = 500;

        /// <summary>Record aggregated keyboard input as steps.</summary>
        public bool RecordKeyboard = true;

        /// <summary>Idle time (ms) after last keystroke before a keyboard burst is flushed.</summary>
        public int KeyboardFlushIdleMs = 1200;

        // ---- Report contents. All default off, so reports stay lean unless opted in. ----

        /// <summary>Include the per-step date/time stamp in the report.</summary>
        public bool IncludeStepTimestamps = false;

        /// <summary>Include the "Additional Details" text recap (the only place the program name is shown).</summary>
        public bool IncludeAdditionalDetails = false;

        /// <summary>Include the "Recording Environment" table (machine, user, OS, screen layout).</summary>
        public bool IncludeEnvironment = false;
    }
}
