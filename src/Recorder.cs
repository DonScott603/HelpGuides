using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>
    /// The recording engine. Installs global low-level mouse and keyboard hooks,
    /// captures a screenshot of the monitor under the cursor for each user action,
    /// resolves the target UI element via UI Automation, and builds an ordered list
    /// of <see cref="RecordedStep"/> objects.
    /// </summary>
    public sealed class Recorder : IDisposable
    {
        private readonly RecorderSettings _settings;
        private readonly List<RecordedStep> _steps = new List<RecordedStep>();
        private readonly object _stepsLock = new object();

        private readonly int _ownProcessId = Process.GetCurrentProcess().Id;

        // Hooks (must keep delegate refs alive to avoid GC).
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private NativeMethods.HookProc _mouseProc;
        private NativeMethods.HookProc _keyboardProc;

        // Background processing of raw captured events.
        private readonly BlockingCollection<Tuple<RawEvent, RecordedStep>> _queue =
            new BlockingCollection<Tuple<RawEvent, RecordedStep>>();
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _paused;

        // Mouse gesture tracking.
        private Point _downPoint;
        private DateTime _downTime;
        private Bitmap _downShot;
        private Rectangle _downMonitor;
        private int _lastButton;
        private DateTime _lastClickUpTime = DateTime.MinValue;
        private Point _lastClickPoint;
        private RecordedStep _lastClickStep;
        private readonly int _doubleClickTime = (int)NativeMethods.GetDoubleClickTime();

        // Keyboard aggregation.
        private readonly object _kbLock = new object();
        private StringBuilder _kbBuffer;
        private Bitmap _kbShot;
        private Rectangle _kbMonitor;
        private Point _kbPoint;
        private IntPtr _kbWindow;
        private DateTime _kbLastKey;
        private System.Windows.Forms.Timer _kbTimer;

        public DateTime StartedAt { get; private set; }
        public DateTime StoppedAt { get; private set; }
        public RecorderSettings Settings { get { return _settings; } }

        /// <summary>Raised (marshaled to the UI thread if a SynchronizationContext exists) when a step is added.</summary>
        public event EventHandler<RecordedStep> StepAdded;

        private readonly SynchronizationContext _sync;

        public Recorder(RecorderSettings settings)
        {
            _settings = settings ?? new RecorderSettings();
            _sync = SynchronizationContext.Current;

            _kbTimer = new System.Windows.Forms.Timer();
            _kbTimer.Interval = 300;
            _kbTimer.Tick += (s, e) =>
            {
                lock (_kbLock)
                {
                    if (_kbBuffer != null && _kbBuffer.Length > 0 &&
                        (DateTime.Now - _kbLastKey).TotalMilliseconds >= _settings.KeyboardFlushIdleMs)
                    {
                        FlushKeyboard();
                    }
                }
            };
        }

        private sealed class RawEvent
        {
            public bool IsKeyboard;
            public Point Point;
            public DateTime Time;
            public string Action;       // for mouse
            public Bitmap Shot;
            public Rectangle Monitor;

            // keyboard payload
            public string Text;
            public IntPtr Window;
        }

        public IList<RecordedStep> Snapshot()
        {
            lock (_stepsLock) return new List<RecordedStep>(_steps);
        }

        public int Count { get { lock (_stepsLock) return _steps.Count; } }

        public bool IsRunning { get { return _running; } }
        public bool IsPaused { get { return _paused; } }

        public void Start()
        {
            if (_running) return;
            lock (_stepsLock) _steps.Clear();
            _running = true;
            _paused = false;
            StartedAt = DateTime.Now;

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "PsrCloneWorker" };
            _worker.Start();

            _mouseProc = MouseHookProc;
            _keyboardProc = KeyboardHookProc;
            IntPtr hMod = NativeMethods.GetModuleHandle(null);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
            _kbTimer.Start();
        }

        public void Pause() { _paused = true; FlushKeyboardSafe(); }
        public void Resume() { _paused = false; }

        public void Stop()
        {
            if (!_running) return;
            FlushKeyboardSafe();
            _running = false;
            StoppedAt = DateTime.Now;
            _kbTimer.Stop();

            if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyboardHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }

            _queue.CompleteAdding();
            try { if (_worker != null) _worker.Join(2000); } catch { }
        }

        // ---- Mouse hook ----
        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _running && !_paused)
            {
                int msg = wParam.ToInt32();
                var data = (NativeMethods.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                var pt = new Point(data.pt.x, data.pt.y);
                try { HandleMouse(msg, pt, data.mouseData); } catch { }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void HandleMouse(int msg, Point pt, uint mouseData)
        {
            // Ignore interactions with our own recorder window.
            uint pid;
            NativeMethods.GetTopWindowTitle(pt.X, pt.Y, out pid);
            if (pid == _ownProcessId) return;

            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN: BeginPress(pt, 1); break;
                case NativeMethods.WM_RBUTTONDOWN: BeginPress(pt, 2); break;
                case NativeMethods.WM_MBUTTONDOWN: BeginPress(pt, 3); break;

                case NativeMethods.WM_LBUTTONUP: EndPress(pt, 1); break;
                case NativeMethods.WM_RBUTTONUP: EndPress(pt, 2); break;
                case NativeMethods.WM_MBUTTONUP: EndPress(pt, 3); break;

                case NativeMethods.WM_MOUSEWHEEL:
                {
                    // A keyboard burst is interrupted by any mouse action.
                    FlushKeyboardSafe();
                    short delta = (short)((mouseData >> 16) & 0xFFFF);
                    string dir = delta > 0 ? "up" : "down";
                    var shot = CaptureMonitor(pt, out _downMonitor);
                    Enqueue(new RawEvent { Point = pt, Time = DateTime.Now, Action = "mouse wheel " + dir, Shot = shot, Monitor = _downMonitor });
                    break;
                }
            }
        }

        private void BeginPress(Point pt, int button)
        {
            FlushKeyboardSafe(); // any click ends a typing burst
            _downPoint = pt;
            _downTime = DateTime.Now;
            _lastButton = button;
            if (_downShot != null) { _downShot.Dispose(); _downShot = null; }
            _downShot = CaptureMonitor(pt, out _downMonitor); // capture at press time for fidelity
        }

        private void EndPress(Point pt, int button)
        {
            if (button != _lastButton) return;
            var now = DateTime.Now;
            double dist = Distance(_downPoint, pt);
            string action;

            if (dist > 6)
            {
                action = button == 1 ? "left drag" : button == 2 ? "right drag" : "middle drag";
            }
            else
            {
                string baseName = button == 1 ? "left click" : button == 2 ? "right click" : "middle click";
                // Double-click detection.
                if (button == 1 &&
                    (now - _lastClickUpTime).TotalMilliseconds <= _doubleClickTime &&
                    Distance(_lastClickPoint, pt) <= 6 &&
                    _lastClickStep != null)
                {
                    _lastClickStep.Action = "double click";
                    _lastClickUpTime = DateTime.MinValue;
                    if (_downShot != null) { _downShot.Dispose(); _downShot = null; }
                    return;
                }
                action = baseName;
            }

            var shot = _downShot; _downShot = null;
            var ev = new RawEvent { Point = _downPoint, Time = _downTime, Action = action, Shot = shot, Monitor = _downMonitor };
            var step = Enqueue(ev);
            if (button == 1 && dist <= 6)
            {
                _lastClickUpTime = now;
                _lastClickPoint = pt;
                _lastClickStep = step;
            }
        }

        // ---- Keyboard hook ----
        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _running && !_paused && _settings.RecordKeyboard)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var data = (NativeMethods.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    try { HandleKey(data.vkCode, data.scanCode); } catch { }
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void HandleKey(uint vk, uint scan)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint pid;
            NativeMethods.GetWindowThreadProcessId(fg, out pid);
            if (pid == _ownProcessId) return; // ignore typing into our own UI

            string token = NativeMethods.KeyToText(vk, scan);
            if (string.IsNullOrEmpty(token)) return;

            lock (_kbLock)
            {
                if (_kbBuffer == null || _kbWindow != fg)
                {
                    if (_kbBuffer != null && _kbBuffer.Length > 0) FlushKeyboard();
                    _kbBuffer = new StringBuilder();
                    Point cur = Cursor.Position;
                    _kbShot = CaptureMonitor(cur, out _kbMonitor);
                    _kbPoint = cur;
                    _kbWindow = fg;
                }
                _kbBuffer.Append(token);
                _kbLastKey = DateTime.Now;
            }
        }

        private void FlushKeyboardSafe() { lock (_kbLock) { if (_kbBuffer != null && _kbBuffer.Length > 0) FlushKeyboard(); } }

        // Must be called under _kbLock.
        private void FlushKeyboard()
        {
            string text = _kbBuffer.ToString();
            var shot = _kbShot; _kbShot = null;
            var mon = _kbMonitor;
            var pt = _kbPoint;
            var win = _kbWindow;
            _kbBuffer = null;
            _kbWindow = IntPtr.Zero;

            Enqueue(new RawEvent { IsKeyboard = true, Text = text, Shot = shot, Monitor = mon, Point = pt, Window = win, Time = DateTime.Now });
        }

        // ---- Worker: resolve UIA + build steps ----
        private RecordedStep Enqueue(RawEvent ev)
        {
            // We build the step object here so callers can post-edit (e.g. double-click),
            // but heavy UIA resolution happens on the worker thread.
            var step = new RecordedStep
            {
                Kind = ev.IsKeyboard ? StepKind.Keyboard : StepKind.Mouse,
                Time = ev.Time,
                Action = ev.Action,
                TypedText = ev.Text,
            };
            ev.Time = ev.Time; // keep
            _queue.Add(Tuple.Create(ev, step));
            return step;
        }

        private void WorkerLoop()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { ProcessEvent(item.Item1, item.Item2); }
                catch { }
            }
        }

        private void ProcessEvent(RawEvent ev, RecordedStep step)
        {
            // Resolve UI element + window.
            string name = null, type = null, window = null, program = null;
            Rectangle elemRect = Rectangle.Empty;

            try
            {
                AutomationElement el = AutomationElement.FromPoint(
                    new System.Windows.Point(ev.Point.X, ev.Point.Y));
                if (el != null)
                {
                    try { name = el.Current.Name; } catch { }
                    try { type = el.Current.ControlType != null ? el.Current.ControlType.LocalizedControlType : null; } catch { }
                    try
                    {
                        var r = el.Current.BoundingRectangle;
                        if (!double.IsInfinity(r.Width) && r.Width > 0 && r.Width < 100000)
                            elemRect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                    }
                    catch { }
                    try
                    {
                        AutomationElement top = TreeWalker.ControlViewWalker.GetParent(el);
                        AutomationElement cur = el;
                        while (top != null && top != AutomationElement.RootElement)
                        {
                            cur = top;
                            top = TreeWalker.ControlViewWalker.GetParent(top);
                        }
                        if (cur != null) { try { window = cur.Current.Name; } catch { } }
                        int procId = 0;
                        try { procId = el.Current.ProcessId; } catch { }
                        if (procId != 0)
                        {
                            try { program = Process.GetProcessById(procId).ProcessName; } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            uint pid;
            string winTitle = NativeMethods.GetTopWindowTitle(ev.Point.X, ev.Point.Y, out pid);
            if (string.IsNullOrEmpty(window)) window = winTitle;
            if (string.IsNullOrEmpty(program) && pid != 0)
            {
                try { program = Process.GetProcessById((int)pid).ProcessName; } catch { }
            }

            step.ElementName = Clean(name);
            step.ElementType = Clean(type);
            step.WindowName = Clean(window);
            step.ProgramName = Clean(program);

            if (_settings.CaptureScreenshots && ev.Shot != null)
            {
                step.Screenshot = ev.Shot;
                step.Cursor = new Point(ev.Point.X - ev.Monitor.X, ev.Point.Y - ev.Monitor.Y);
                if (elemRect != Rectangle.Empty)
                {
                    var local = new Rectangle(elemRect.X - ev.Monitor.X, elemRect.Y - ev.Monitor.Y, elemRect.Width, elemRect.Height);
                    local.Intersect(new Rectangle(0, 0, ev.Shot.Width, ev.Shot.Height));
                    step.Highlight = local;
                }
            }
            else if (ev.Shot != null)
            {
                ev.Shot.Dispose();
            }

            AddStep(step);
        }

        private void AddStep(RecordedStep step)
        {
            lock (_stepsLock)
            {
                step.Index = _steps.Count + 1;
                _steps.Add(step);
                EnforceScreenshotCap();
            }
            var handler = StepAdded;
            if (handler != null)
            {
                if (_sync != null) _sync.Post(_ => handler(this, step), null);
                else handler(this, step);
            }
        }

        // Keep only the N most recent screenshots (psr.exe behaviour); text steps are retained.
        private void EnforceScreenshotCap()
        {
            int cap = Math.Max(1, _settings.MaxScreenshots);
            int withShots = 0;
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                if (_steps[i].Screenshot != null)
                {
                    withShots++;
                    if (withShots > cap)
                    {
                        _steps[i].Screenshot.Dispose();
                        _steps[i].Screenshot = null;
                    }
                }
            }
        }

        /// <summary>Adds a manual user comment with an optional annotated screenshot.</summary>
        public void AddComment(string text, Bitmap shot, Rectangle highlight)
        {
            var step = new RecordedStep
            {
                Kind = StepKind.Comment,
                Time = DateTime.Now,
                Comment = text,
                Screenshot = shot,
                Highlight = highlight,
            };
            AddStep(step);
        }

        // ---- helpers ----
        private static double Distance(Point a, Point b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 200) s = s.Substring(0, 200) + "\u2026";
            return s;
        }

        /// <summary>Captures the monitor that contains the given screen point.</summary>
        public static Bitmap CaptureMonitor(Point screenPoint, out Rectangle monitorBounds)
        {
            Screen scr = Screen.FromPoint(screenPoint);
            monitorBounds = scr.Bounds;
            var bmp = new Bitmap(monitorBounds.Width, monitorBounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(monitorBounds.X, monitorBounds.Y, 0, 0, monitorBounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            try { _kbTimer.Dispose(); } catch { }
            lock (_stepsLock)
            {
                foreach (var s in _steps) if (s.Screenshot != null) s.Screenshot.Dispose();
            }
        }
    }
}
