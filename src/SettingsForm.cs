using System;
using System.Drawing;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>Settings dialog, mirroring the psr.exe Settings options.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly RecorderSettings _s;
        private CheckBox _capture;
        private CheckBox _keyboard;
        private NumericUpDown _max;

        public SettingsForm(RecorderSettings settings)
        {
            _s = settings;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 190);
            Font = new Font("Segoe UI", 9f);

            _capture = new CheckBox
            {
                Text = "Enable screen capture",
                Checked = _s.CaptureScreenshots,
                Location = new Point(16, 16),
                AutoSize = true
            };
            _keyboard = new CheckBox
            {
                Text = "Record keyboard input",
                Checked = _s.RecordKeyboard,
                Location = new Point(16, 46),
                AutoSize = true
            };

            var lbl = new Label
            {
                Text = "Number of recent screen captures to store (1\u20131500):",
                Location = new Point(16, 84),
                AutoSize = true
            };
            _max = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1500,
                Value = Math.Max(1, Math.Min(1500, _s.MaxScreenshots)),
                Location = new Point(16, 106),
                Width = 80
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(180, 150), Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(268, 150), Width = 80 };
            ok.Click += (s, e) =>
            {
                _s.CaptureScreenshots = _capture.Checked;
                _s.RecordKeyboard = _keyboard.Checked;
                _s.MaxScreenshots = (int)_max.Value;
            };

            Controls.Add(_capture);
            Controls.Add(_keyboard);
            Controls.Add(lbl);
            Controls.Add(_max);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
