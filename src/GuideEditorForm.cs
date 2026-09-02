using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PsrClone
{
    public enum EditorSaveMode
    {
        Mht,
        Folder
    }

    /// <summary>
    /// The post-recording review pass that turns a raw recording into a guide: title and
    /// intro, per-step text, insert/delete/reorder, and non-destructive crop / redact /
    /// pixelate on each screenshot. Nothing here touches the clean bitmaps; all image
    /// edits are stored on <see cref="RecordedStep"/> and applied by
    /// <see cref="ReportWriter.RenderPreview"/> at render time.
    /// </summary>
    public sealed class GuideEditorForm : Form
    {
        private enum Tool { None, Crop, Redact, Pixelate }

        private readonly List<RecordedStep> _steps;
        private readonly DateTime _started;
        private readonly DateTime _stopped;

        private TextBox _txtTitle;
        private TextBox _txtIntro;
        private ListBox _list;
        private Button _btnUp, _btnDown, _btnDelete, _btnInsert;
        private CheckBox _btnCrop, _btnRedact, _btnPixelate;
        private Button _btnUndoRedaction, _btnResetImage;
        private Label _hint;
        private ImageCanvas _canvas;
        private TextBox _txtStep;
        private LinkLabel _lnkResetText;
        private Label _lblStepText;
        private Button _btnSaveMht, _btnSaveFolder, _btnCancel;

        private Tool _tool = Tool.None;
        private RecordedStep _current;
        private bool _syncing;

        /// <summary>Populated when the dialog closes with OK.</summary>
        public GuideDocument Result { get; private set; }
        public EditorSaveMode SaveMode { get; private set; }

        public GuideEditorForm(IList<RecordedStep> steps, DateTime started, DateTime stopped)
        {
            _steps = new List<RecordedStep>(steps);
            _started = started;
            _stopped = stopped;

            Text = "Edit Guide \u2014 PSR Clone v" + BuildInfo.Version;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 800);
            MinimumSize = new Size(940, 620);
            ShowInTaskbar = true;
            KeyPreview = true;

            BuildLayout();
            PopulateList();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else ShowStep(null);

            _txtTitle.Focus();
        }

        // -----------------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------------

        private void BuildLayout()
        {
            // ---- header: title + intro ----
            var header = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(10, 8, 10, 4) };

            var lblTitle = new Label { Text = "Guide title:", AutoSize = true, Location = new Point(10, 12) };
            _txtTitle = new TextBox
            {
                Location = new Point(110, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI", 10.5f)
            };
            _txtTitle.Width = header.Width - 120;

            var lblIntro = new Label { Text = "Description:", AutoSize = true, Location = new Point(10, 44) };
            _txtIntro = new TextBox
            {
                Location = new Point(110, 40),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 54,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = GuideDocument.DefaultIntro
            };
            _txtIntro.Width = header.Width - 120;

            header.Controls.Add(lblTitle);
            header.Controls.Add(_txtTitle);
            header.Controls.Add(lblIntro);
            header.Controls.Add(_txtIntro);
            header.Resize += (s, e) =>
            {
                _txtTitle.Width = header.ClientSize.Width - 120;
                _txtIntro.Width = header.ClientSize.Width - 120;
            };

            // ---- footer: save / cancel ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            _btnCancel = new Button { Text = "Cancel", Width = 90, Height = 30 };
            _btnSaveFolder = new Button { Text = "Save to folder\u2026", Width = 130, Height = 30 };
            _btnSaveMht = new Button { Text = "Save as .mht\u2026", Width = 130, Height = 30 };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnSaveFolder.Click += (s, e) => Commit(EditorSaveMode.Folder);
            _btnSaveMht.Click += (s, e) => Commit(EditorSaveMode.Mht);
            footer.Controls.Add(_btnSaveMht);
            footer.Controls.Add(_btnSaveFolder);
            footer.Controls.Add(_btnCancel);
            footer.Resize += (s, e) =>
            {
                int y = 10;
                int x = footer.ClientSize.Width - 10;
                x -= _btnCancel.Width; _btnCancel.Location = new Point(x, y);
                x -= _btnSaveFolder.Width + 8; _btnSaveFolder.Location = new Point(x, y);
                x -= _btnSaveMht.Width + 8; _btnSaveMht.Location = new Point(x, y);
            };
            CancelButton = _btnCancel;

            // ---- left: step list + list buttons ----
            var left = new Panel { Dock = DockStyle.Left, Width = 320, Padding = new Padding(10, 0, 4, 6) };
            var lblSteps = new Label { Text = "Steps", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

            var listButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            _btnUp = MakeButton("\u25B2 Up", 70);
            _btnDown = MakeButton("\u25BC Down", 80);
            _btnDelete = MakeButton("Delete", 80);
            _btnInsert = MakeButton("Insert text step", 130);
            _btnUp.Click += (s, e) => MoveStep(-1);
            _btnDown.Click += (s, e) => MoveStep(+1);
            _btnDelete.Click += (s, e) => DeleteCurrent();
            _btnInsert.Click += (s, e) => InsertTextStep();
            listButtons.Controls.Add(_btnUp);
            listButtons.Controls.Add(_btnDown);
            listButtons.Controls.Add(_btnDelete);
            listButtons.Controls.Add(_btnInsert);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                HorizontalScrollbar = false
            };
            _list.SelectedIndexChanged += (s, e) => { if (!_syncing) ShowStep(SelectedStep()); };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteCurrent(); e.Handled = true; }
            };

            left.Controls.Add(_list);
            left.Controls.Add(listButtons);
            left.Controls.Add(lblSteps);

            // ---- centre: image toolbar, canvas, step text ----
            var centre = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 10, 6) };

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
            _btnCrop = MakeToggle("Crop", 70);
            _btnRedact = MakeToggle("Redact (black)", 110);
            _btnPixelate = MakeToggle("Pixelate", 80);
            _btnUndoRedaction = MakeButton("Undo last redaction", 140);
            _btnResetImage = MakeButton("Reset image", 100);
            _btnCrop.CheckedChanged += (s, e) => { if (_syncing) return; if (_btnCrop.Checked) SetTool(Tool.Crop); else if (_tool == Tool.Crop) SetTool(Tool.None); };
            _btnRedact.CheckedChanged += (s, e) => { if (_syncing) return; if (_btnRedact.Checked) SetTool(Tool.Redact); else if (_tool == Tool.Redact) SetTool(Tool.None); };
            _btnPixelate.CheckedChanged += (s, e) => { if (_syncing) return; if (_btnPixelate.Checked) SetTool(Tool.Pixelate); else if (_tool == Tool.Pixelate) SetTool(Tool.None); };
            _btnUndoRedaction.Click += (s, e) => UndoRedaction();
            _btnResetImage.Click += (s, e) => ResetImage();
            toolbar.Controls.Add(_btnCrop);
            toolbar.Controls.Add(_btnRedact);
            toolbar.Controls.Add(_btnPixelate);
            toolbar.Controls.Add(_btnUndoRedaction);
            toolbar.Controls.Add(_btnResetImage);

            _hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(70, 70, 70),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var textPanel = new Panel { Dock = DockStyle.Bottom, Height = 132 };
            _lblStepText = new Label { Text = "Step text:", AutoSize = true, Location = new Point(0, 6) };
            _lnkResetText = new LinkLabel { Text = "Reset to recorded text", AutoSize = true, Location = new Point(90, 6) };
            _lnkResetText.LinkClicked += (s, e) => ResetStepText();
            _txtStep = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Location = new Point(0, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = new Font("Segoe UI", 10f)
            };
            _txtStep.TextChanged += (s, e) => OnStepTextChanged();
            textPanel.Controls.Add(_lblStepText);
            textPanel.Controls.Add(_lnkResetText);
            textPanel.Controls.Add(_txtStep);
            textPanel.Resize += (s, e) =>
            {
                _txtStep.Width = textPanel.ClientSize.Width;
                _txtStep.Height = textPanel.ClientSize.Height - 28;
            };

            _canvas = new ImageCanvas { Dock = DockStyle.Fill, BackColor = Color.FromArgb(225, 225, 225) };
            _canvas.RegionSelected += OnRegionSelected;

            centre.Controls.Add(_canvas);
            centre.Controls.Add(_hint);
            centre.Controls.Add(toolbar);
            centre.Controls.Add(textPanel);

            Controls.Add(centre);
            Controls.Add(left);
            Controls.Add(header);
            Controls.Add(footer);

            // Dock order matters: header/footer first so the side and centre fit between them.
            header.SendToBack();
            footer.SendToBack();
        }

        private static Button MakeButton(string text, int width)
        {
            return new Button { Text = text, Width = width, Height = 28, Margin = new Padding(0, 4, 6, 0) };
        }

        private static CheckBox MakeToggle(string text, int width)
        {
            return new CheckBox
            {
                Text = text,
                Width = width,
                Height = 28,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 4, 6, 0)
            };
        }

        // -----------------------------------------------------------------------------
        // Step list
        // -----------------------------------------------------------------------------

        private RecordedStep SelectedStep()
        {
            int i = _list.SelectedIndex;
            return (i >= 0 && i < _steps.Count) ? _steps[i] : null;
        }

        private void PopulateList()
        {
            _syncing = true;
            try
            {
                int keep = _list.SelectedIndex;
                _list.BeginUpdate();
                _list.Items.Clear();
                for (int i = 0; i < _steps.Count; i++)
                    _list.Items.Add(ListLabel(i, _steps[i]));
                _list.EndUpdate();
                if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
            }
            finally { _syncing = false; }
        }

        private static string ListLabel(int index, RecordedStep step)
        {
            string body = step.DisplayDescription();
            if (string.IsNullOrEmpty(body)) body = step.Kind == StepKind.Text ? "(empty text step)" : "(no description)";
            body = body.Replace("\r", " ").Replace("\n", " ");
            string tag = step.Kind == StepKind.Text ? "(text) " : (step.Screenshot == null ? "(no image) " : string.Empty);
            string s = (index + 1) + ". " + tag + body;
            return s.Length > 60 ? s.Substring(0, 60) + "\u2026" : s;
        }

        private void RefreshLabel(int index)
        {
            if (index < 0 || index >= _steps.Count) return;
            _syncing = true;
            try { _list.Items[index] = ListLabel(index, _steps[index]); }
            finally { _syncing = false; }
        }

        private void MoveStep(int delta)
        {
            int i = _list.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _steps.Count) return;
            var tmp = _steps[i]; _steps[i] = _steps[j]; _steps[j] = tmp;
            _syncing = true;
            try
            {
                _list.Items[i] = ListLabel(i, _steps[i]);
                _list.Items[j] = ListLabel(j, _steps[j]);
                _list.SelectedIndex = j;
            }
            finally { _syncing = false; }
            UpdateListButtons();
        }

        private void DeleteCurrent()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            // Bitmaps belong to the Recorder (disposed with it), so removal is just list surgery.
            _steps.RemoveAt(i);
            _syncing = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                for (int k = 0; k < _steps.Count; k++) _list.Items.Add(ListLabel(k, _steps[k]));
                _list.EndUpdate();
            }
            finally { _syncing = false; }

            if (_steps.Count == 0) { ShowStep(null); return; }
            _list.SelectedIndex = Math.Min(i, _steps.Count - 1);
        }

        private void InsertTextStep()
        {
            int at = _list.SelectedIndex < 0 ? _steps.Count : _list.SelectedIndex + 1;
            var step = new RecordedStep { Kind = StepKind.Text, Time = DateTime.Now, Comment = string.Empty };
            _steps.Insert(at, step);
            PopulateList();
            _list.SelectedIndex = at;
            _txtStep.Focus();
        }

        private void UpdateListButtons()
        {
            int i = _list.SelectedIndex;
            bool any = i >= 0;
            _btnUp.Enabled = any && i > 0;
            _btnDown.Enabled = any && i < _steps.Count - 1;
            _btnDelete.Enabled = any;
        }

        // -----------------------------------------------------------------------------
        // Current step
        // -----------------------------------------------------------------------------

        private void ShowStep(RecordedStep step)
        {
            _current = step;
            _syncing = true;
            try
            {
                if (step == null)
                {
                    _txtStep.Text = string.Empty;
                    _txtStep.Enabled = false;
                    _lnkResetText.Visible = false;
                }
                else
                {
                    _txtStep.Enabled = true;
                    _txtStep.Text = step.DisplayDescription();
                    _lnkResetText.Visible = step.Kind != StepKind.Text && !string.IsNullOrEmpty(step.CustomDescription);
                    _lblStepText.Text = step.Kind == StepKind.Text ? "Text step:" : "Step text:";
                }
            }
            finally { _syncing = false; }

            SetTool(Tool.None);
            RefreshPreview();
            UpdateListButtons();
            UpdateImageButtons();
        }

        private void OnStepTextChanged()
        {
            if (_syncing || _current == null) return;
            string text = _txtStep.Text;
            if (_current.Kind == StepKind.Text)
            {
                _current.Comment = text;
            }
            else
            {
                // Typing the generated text back in returns the step to its pristine state.
                _current.CustomDescription = string.Equals(text, _current.BuildDescription(), StringComparison.Ordinal)
                    ? null
                    : text;
                _lnkResetText.Visible = !string.IsNullOrEmpty(_current.CustomDescription);
            }
            RefreshLabel(_list.SelectedIndex);
        }

        private void ResetStepText()
        {
            if (_current == null || _current.Kind == StepKind.Text) return;
            _current.CustomDescription = null;
            _syncing = true;
            try { _txtStep.Text = _current.BuildDescription(); }
            finally { _syncing = false; }
            _lnkResetText.Visible = false;
            RefreshLabel(_list.SelectedIndex);
        }

        // -----------------------------------------------------------------------------
        // Image tools
        // -----------------------------------------------------------------------------

        private void UpdateImageButtons()
        {
            bool hasImage = _current != null && _current.Screenshot != null;
            _btnCrop.Enabled = hasImage;
            _btnRedact.Enabled = hasImage;
            _btnPixelate.Enabled = hasImage;
            _btnUndoRedaction.Enabled = hasImage && _current.Redactions.Count > 0;
            _btnResetImage.Enabled = hasImage && _current.HasImageEdits;
        }

        private void SetTool(Tool tool)
        {
            _tool = tool;
            _syncing = true;
            try
            {
                _btnCrop.Checked = tool == Tool.Crop;
                _btnRedact.Checked = tool == Tool.Redact;
                _btnPixelate.Checked = tool == Tool.Pixelate;
            }
            finally { _syncing = false; }

            _canvas.SelectionEnabled = tool != Tool.None && _current != null && _current.Screenshot != null;
            _canvas.Cursor = _canvas.SelectionEnabled ? Cursors.Cross : Cursors.Default;

            switch (tool)
            {
                case Tool.Crop:
                    _hint.Text = "Crop: drag a rectangle around the part of the screenshot to keep.";
                    break;
                case Tool.Redact:
                    _hint.Text = "Redact: drag a rectangle to cover it with a solid black block.";
                    break;
                case Tool.Pixelate:
                    _hint.Text = "Pixelate: drag a rectangle to blur it into a mosaic (not for passwords).";
                    break;
                default:
                    _hint.Text = _current == null
                        ? "No steps. Insert a text step or cancel."
                        : (_current.Screenshot == null
                            ? "This step has no screenshot."
                            : "Pick a tool above, then drag on the image. Edits are applied when you save.");
                    break;
            }
        }

        private void OnRegionSelected(object sender, Rectangle imageRect)
        {
            if (_current == null || _current.Screenshot == null || _tool == Tool.None) return;
            if (imageRect.Width < 8 || imageRect.Height < 8) return;

            // The canvas shows the already-cropped image, so map back to uncropped coordinates.
            Rectangle full = imageRect;
            if (_current.Crop != Rectangle.Empty) full.Offset(_current.Crop.X, _current.Crop.Y);
            full.Intersect(new Rectangle(0, 0, _current.Screenshot.Width, _current.Screenshot.Height));
            if (full.Width < 8 || full.Height < 8) return;

            switch (_tool)
            {
                case Tool.Crop:
                    _current.Crop = full;
                    SetTool(Tool.None);
                    break;
                case Tool.Redact:
                    _current.Redactions.Add(new Redaction(full, RedactionKind.Solid));
                    break;
                case Tool.Pixelate:
                    _current.Redactions.Add(new Redaction(full, RedactionKind.Pixelate));
                    break;
            }

            RefreshPreview();
            UpdateImageButtons();
        }

        private void UndoRedaction()
        {
            if (_current == null || _current.Redactions.Count == 0) return;
            _current.Redactions.RemoveAt(_current.Redactions.Count - 1);
            RefreshPreview();
            UpdateImageButtons();
        }

        private void ResetImage()
        {
            if (_current == null) return;
            _current.Crop = Rectangle.Empty;
            _current.Redactions.Clear();
            SetTool(Tool.None);
            RefreshPreview();
            UpdateImageButtons();
        }

        private void RefreshPreview()
        {
            Bitmap bmp = null;
            try
            {
                if (_current != null && _current.Screenshot != null)
                    bmp = ReportWriter.RenderPreview(_current);
            }
            catch (Exception ex)
            {
                _hint.Text = "Preview failed: " + ex.Message;
            }
            _canvas.SetImage(bmp); // canvas takes ownership and disposes the previous one
            _canvas.Placeholder = _current == null
                ? "No step selected"
                : (_current.Kind == StepKind.Text ? "Text-only step (no image)" : "No screenshot for this step");
        }

        // -----------------------------------------------------------------------------
        // Commit
        // -----------------------------------------------------------------------------

        private void Commit(EditorSaveMode mode)
        {
            if (_steps.Count == 0)
            {
                MessageBox.Show(this, "The guide has no steps. Add at least one step or cancel.",
                    "PSR Clone", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var doc = new GuideDocument(_steps, _started, _stopped)
            {
                Title = string.IsNullOrWhiteSpace(_txtTitle.Text) ? GuideDocument.DefaultTitle : _txtTitle.Text.Trim(),
                Intro = _txtIntro.Text
            };
            doc.Renumber();

            Result = doc;
            SaveMode = mode;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _canvas != null) _canvas.SetImage(null);
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Fit-to-panel image viewer with a rubber-band selection. Selection is reported in
    /// image pixel coordinates via <see cref="RegionSelected"/>. Owns the bitmap it shows.
    /// </summary>
    internal sealed class ImageCanvas : Panel
    {
        private Bitmap _image;
        private bool _dragging;
        private Point _start;
        private Rectangle _sel = Rectangle.Empty; // client coords
        private float _scale = 1f;
        private Point _offset = Point.Empty;

        public bool SelectionEnabled;
        public string Placeholder = string.Empty;

        public event EventHandler<Rectangle> RegionSelected;

        public ImageCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        public void SetImage(Bitmap bmp)
        {
            if (_image != null && !ReferenceEquals(_image, bmp)) _image.Dispose();
            _image = bmp;
            _sel = Rectangle.Empty;
            _dragging = false;
            Invalidate();
        }

        private Rectangle ImageRect()
        {
            if (_image == null) return Rectangle.Empty;
            const int pad = 8;
            int availW = Math.Max(1, ClientSize.Width - pad * 2);
            int availH = Math.Max(1, ClientSize.Height - pad * 2);
            float sx = (float)availW / _image.Width;
            float sy = (float)availH / _image.Height;
            _scale = Math.Min(1f, Math.Min(sx, sy)); // never upscale; small crops stay crisp
            int w = Math.Max(1, (int)Math.Round(_image.Width * _scale));
            int h = Math.Max(1, (int)Math.Round(_image.Height * _scale));
            _offset = new Point(pad + (availW - w) / 2, pad + (availH - h) / 2);
            return new Rectangle(_offset, new Size(w, h));
        }

        private Rectangle ClientToImage(Rectangle client)
        {
            if (_image == null || _scale <= 0f) return Rectangle.Empty;
            int x1 = (int)Math.Floor((client.Left - _offset.X) / _scale);
            int y1 = (int)Math.Floor((client.Top - _offset.Y) / _scale);
            int x2 = (int)Math.Ceiling((client.Right - _offset.X) / _scale);
            int y2 = (int)Math.Ceiling((client.Bottom - _offset.Y) / _scale);
            var r = Rectangle.FromLTRB(x1, y1, x2, y2);
            r.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
            return r;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!SelectionEnabled || _image == null || e.Button != MouseButtons.Left) return;
            if (!ImageRect().Contains(e.Location)) return;
            _dragging = true;
            _start = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _sel = Normalize(_start, Clamp(e.Location));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            _sel = Normalize(_start, Clamp(e.Location));
            Rectangle img = ClientToImage(_sel);
            _sel = Rectangle.Empty;
            Invalidate();
            var h = RegionSelected;
            if (h != null && img.Width > 0 && img.Height > 0) h(this, img);
        }

        private Point Clamp(Point p)
        {
            Rectangle r = ImageRect();
            return new Point(Math.Max(r.Left, Math.Min(r.Right, p.X)), Math.Max(r.Top, Math.Min(r.Bottom, p.Y)));
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            if (_image == null)
            {
                if (!string.IsNullOrEmpty(Placeholder))
                {
                    using (var f = new Font("Segoe UI", 11f))
                    using (var b = new SolidBrush(Color.FromArgb(110, 110, 110)))
                    {
                        var sz = g.MeasureString(Placeholder, f);
                        g.DrawString(Placeholder, f, b, (ClientSize.Width - sz.Width) / 2, (ClientSize.Height - sz.Height) / 2);
                    }
                }
                return;
            }

            Rectangle dest = ImageRect();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_image, dest);
            using (var pen = new Pen(Color.FromArgb(136, 136, 136)))
                g.DrawRectangle(pen, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                using (var fill = new SolidBrush(Color.FromArgb(50, 0, 120, 215)))
                    g.FillRectangle(fill, _sel);
                using (var pen = new Pen(Color.FromArgb(255, 0, 120, 215), 2f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, _sel);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null) { _image.Dispose(); _image = null; }
            base.Dispose(disposing);
        }
    }
}
