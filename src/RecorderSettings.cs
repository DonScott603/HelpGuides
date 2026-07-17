using System;

namespace PsrClone
{
    /// <summary>Recorder options, mirroring psr.exe's Settings dialog.</summary>
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
    }
}
