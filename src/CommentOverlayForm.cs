using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>
    /// Full-screen overlay that lets the user drag-select a region and type a comment,
    /// mirroring the psr.exe "Add Comment" flow.
    /// </summary>
    public sealed class CommentOverlayForm : Form
    {
        private readonly Bitmap _clean;         // clean primary-screen capture
        private readonly Rectangle _screen;     // primary screen bounds
        private bool _dragging;
        private Point _start;
        private Rectangle _sel = Rectangle.Empty;

        private TextBox _text;
        private Button _ok;
        private Button _cancel;
        private Label _hint;

        public string CommentText { get; private set; }
        public Rectangle HighlightRect { get; private set; }
        public Bitmap CapturedBitmap { get; private set; }

        public CommentOverlayForm()
        {
            _screen = Screen.FromPoint(Cursor.Position).Bounds;
            _clean = new Bitmap(_screen.Width, _screen.Height);
            using (var g = Graphics.FromImage(_clean))
                g.CopyFromScreen(_screen.X, _screen.Y, 0, 0, _screen.Size, CopyPixelOperation.SourceCopy);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = _screen;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;

            _hint = new Label
            {
                Text = "Drag to highlight an area, then type a comment. Enter = OK, Esc = Cancel.",
                AutoSize = true,
                BackColor = Color.FromArgb(230, 30, 30, 30),
                ForeColor = Color.White,
                Padding = new Padding(8),
                Location = new Point(20, 20)
            };
            Controls.Add(_hint);

            _text = new TextBox
            {
                Multiline = true,
                Width = 360,
                Height = 70,
                Visible = false,
                Font = new Font("Segoe UI", 10f)
            };
            _ok = new Button { Text = "OK", Width = 70, Visible = false };
            _cancel = new Button { Text = "Cancel", Width = 70, Visible = false };
            _ok.Click += (s, e) => Confirm();
            _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_text);
            Controls.Add(_ok);
            Controls.Add(_cancel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.KeyCode == Keys.Enter && e.Control) { Confirm(); }
            };
            _text.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Confirm(); }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            Paint += OnPaint;
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (_text.Visible) return;
            _dragging = true;
            _start = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _sel = Normalize(_start, e.Location);
            Invalidate();
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _sel = Normalize(_start, e.Location);
            ShowCommentBox();
            Invalidate();
        }

        private void ShowCommentBox()
        {
            int tx = Math.Min(_sel.Left, _screen.Width - _text.Width - 20);
            int ty = _sel.Bottom + 8;
            if (ty + _text.Height + 40 > _screen.Height) ty = Math.Max(20, _sel.Top - _text.Height - 40);
            tx = Math.Max(20, tx);

            _text.Location = new Point(tx, ty);
            _ok.Location = new Point(tx, ty + _text.Height + 6);
            _cancel.Location = new Point(tx + 80, ty + _text.Height + 6);
            _text.Visible = _ok.Visible = _cancel.Visible = true;
            Cursor = Cursors.Default;
            _text.Focus();
        }

        private void Confirm()
        {
            CommentText = string.IsNullOrEmpty(_text.Text) ? "(comment)" : _text.Text;
            HighlightRect = (_sel.Width > 2 && _sel.Height > 2) ? _sel : Rectangle.Empty;
            CapturedBitmap = new Bitmap(_clean); // hand a clone to the recorder
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImage(_clean, 0, 0, _screen.Width, _screen.Height);
            using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                // punch through the dim layer to show the selected region clearly
                g.SetClip(_sel);
                g.DrawImage(_clean, 0, 0, _screen.Width, _screen.Height);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(255, 0, 120, 215), 3f))
                    g.DrawRectangle(pen, _sel);
            }
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _clean != null) _clean.Dispose();
            base.Dispose(disposing);
        }
    }
}
