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
        private CheckBox _timestamps;
        private CheckBox _details;
        private CheckBox _environment;

        public SettingsForm(RecorderSettings settings)
        {
            _s = settings;
            Text = "Settings";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 312);
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

            var section = new Label
            {
                Text = "Include in report:",
                Location = new Point(16, 146),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _timestamps = new CheckBox
            {
                Text = "Date/time stamps on steps",
                Checked = _s.IncludeStepTimestamps,
                Location = new Point(16, 172),
                AutoSize = true
            };
            _details = new CheckBox
            {
                Text = "Additional Details section",
                Checked = _s.IncludeAdditionalDetails,
                Location = new Point(16, 202),
                AutoSize = true
            };
            _environment = new CheckBox
            {
                Text = "Recording Environment section",
                Checked = _s.IncludeEnvironment,
                Location = new Point(16, 232),
                AutoSize = true
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(180, 272), Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(268, 272), Width = 80 };
            ok.Click += (s, e) =>
            {
                _s.CaptureScreenshots = _capture.Checked;
                _s.RecordKeyboard = _keyboard.Checked;
                _s.MaxScreenshots = (int)_max.Value;
                _s.IncludeStepTimestamps = _timestamps.Checked;
                _s.IncludeAdditionalDetails = _details.Checked;
                _s.IncludeEnvironment = _environment.Checked;
            };

            // Add order defines tab order: the new rows must sit between the numeric and the buttons.
            Controls.Add(_capture);
            Controls.Add(_keyboard);
            Controls.Add(lbl);
            Controls.Add(_max);
            Controls.Add(section);
            Controls.Add(_timestamps);
            Controls.Add(_details);
            Controls.Add(_environment);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
