using System;
using System.Drawing;
using System.Windows.Forms;

namespace PsrClone
{
    /// <summary>The main recorder toolbar, modeled on the psr.exe control strip.</summary>
    public sealed class MainForm : Form
    {
        private readonly RecorderSettings _settings = new RecorderSettings();
        private Recorder _recorder;

        private Button _btnStart;
        private Button _btnPause;
        private Button _btnStop;
        private Button _btnComment;
        private Button _btnSettings;
        private Button _btnHelp;
        private Label _status;
        private Label _count;

        private const string RepoUrl = "https://github.com/SomeGuru/PSR-Clone";

        public MainForm()
        {
            Text = "Steps Recorder (PSR Clone) v" + BuildInfo.Version;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            MaximizeBox = false;
            BackColor = Color.FromArgb(240, 240, 240);
            Font = new Font("Segoe UI", 9f);

            // Lay the buttons out on a single row, each sized to its caption.
            int x = 8;
            const int y = 10, h = 32, gap = 6;

            _btnStart = AddButton("\u25CF  Start Record", ref x, y, h, gap);
            _btnStart.ForeColor = Color.DarkRed;
            _btnStart.Click += (s, e) => StartRecording();

            _btnPause = AddButton("\u2225  Pause", ref x, y, h, gap);
            _btnPause.Enabled = false;
            _btnPause.Click += (s, e) => TogglePause();

            _btnStop = AddButton("\u25A0  Stop", ref x, y, h, gap);
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => StopRecording();

            _btnComment = AddButton("Add Comment", ref x, y, h, gap);
            _btnComment.Enabled = false;
            _btnComment.Click += (s, e) => AddComment();

            _btnSettings = AddButton("Settings", ref x, y, h, gap);
            _btnSettings.Click += (s, e) => ShowSettings();

            _btnHelp = AddButton("Help", ref x, y, h, gap);
            _btnHelp.Click += (s, e) => ShowHelp();

            int totalWidth = x + 2; // trailing margin (x already includes last gap)
            ClientSize = new Size(totalWidth, 78);

            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Top + 24);

            _status = new Label
            {
                Text = "Idle. Press Start Record to begin.",
                AutoSize = false,
                Location = new Point(10, 50),
                Size = new Size(totalWidth - 170, 20),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            _count = new Label
            {
                Text = "Steps: 0",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(totalWidth - 158, 50),
                Size = new Size(150, 20),
                ForeColor = Color.FromArgb(60, 60, 60)
            };

            Controls.Add(_status);
            Controls.Add(_count);

            FormClosing += (s, e) => { if (_recorder != null) _recorder.Dispose(); };
        }

        private Button AddButton(string text, ref int x, int y, int h, int gap)
        {
            int w = TextRenderer.MeasureText(text, Font).Width + 24;
            if (w < 70) w = 70;
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.System,
                UseVisualStyleBackColor = true
            };
            Controls.Add(btn);
            x += w + gap;
            return btn;
        }

        private void StartRecording()
        {
            // Release the previous run's screenshots; the editor is modal, so nothing can
            // still be looking at them once Start is enabled again.
            if (_recorder != null)
            {
                try { _recorder.Dispose(); } catch { }
                _recorder = null;
            }

            _recorder = new Recorder(_settings);
            _recorder.StepAdded += (s, step) =>
            {
                _count.Text = "Steps: " + _recorder.Count;
                _status.Text = "Recording\u2026  Last: " + Truncate(step.BuildDescription(), 60);
            };
            _recorder.Start();

            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            _btnStop.Enabled = true;
            _btnComment.Enabled = true;
            _btnSettings.Enabled = false;
            _status.Text = "Recording\u2026 interact with any window.";
        }

        private void TogglePause()
        {
            if (_recorder == null) return;
            if (_recorder.IsPaused)
            {
                _recorder.Resume();
                _btnPause.Text = "\u2225  Pause";
                _status.Text = "Recording\u2026";
            }
            else
            {
                _recorder.Pause();
                _btnPause.Text = "\u25B6  Resume";
                _status.Text = "Paused.";
            }
        }

        private void StopRecording()
        {
            if (_recorder == null) return;
            _recorder.Stop();
            _btnStart.Enabled = true;
            _btnPause.Enabled = false;
            _btnPause.Text = "\u2225  Pause";
            _btnStop.Enabled = false;
            _btnComment.Enabled = false;
            _btnSettings.Enabled = true;

            var steps = _recorder.Snapshot();
            _status.Text = "Stopped. " + steps.Count + " steps recorded.";

            if (steps.Count == 0)
            {
                MessageBox.Show(this, "No steps were recorded.", "PSR Clone",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Review pass: title, step text, insert/delete, crop, redact. The editor decides
            // the output format too, so the old Yes/No/Cancel prompt is gone.
            GuideDocument doc;
            EditorSaveMode mode;
            using (var editor = new GuideEditorForm(steps, _recorder.StartedAt, _recorder.StoppedAt))
            {
                if (editor.ShowDialog(this) != DialogResult.OK || editor.Result == null)
                {
                    _status.Text = "Stopped. Recording discarded.";
                    return;
                }
                doc = editor.Result;
                mode = editor.SaveMode;
            }

            try
            {
                if (mode == EditorSaveMode.Mht)
                {
                    using (var sfd = new SaveFileDialog())
                    {
                        sfd.Title = "Save guide";
                        sfd.Filter = "Guide (*.mht)|*.mht";
                        sfd.FileName = doc.SuggestedFileBase() + ".mht";
                        if (sfd.ShowDialog(this) != DialogResult.OK) return;

                        Cursor = Cursors.WaitCursor;
                        // Report the path ReportWriter actually wrote: it normalizes the
                        // extension, so sfd.FileName may not name the file on disk.
                        string saved = ReportWriter.Save(sfd.FileName, doc, _settings);
                        Cursor = Cursors.Default;
                        _status.Text = "Saved: " + saved;
                        PromptOpen(saved);
                    }
                }
                else // folder dump
                {
                    using (var fbd = new FolderBrowserDialog())
                    {
                        fbd.Description = "Choose a folder to dump the guide files into";
                        if (fbd.ShowDialog(this) != DialogResult.OK) return;

                        string dir = System.IO.Path.Combine(fbd.SelectedPath, doc.SuggestedFileBase());
                        Cursor = Cursors.WaitCursor;
                        string htm = ReportWriter.SaveFolder(dir, doc, _settings);
                        Cursor = Cursors.Default;
                        _status.Text = "Saved to folder: " + dir;
                        PromptOpen(htm);
                    }
                }
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "Failed to save report:\n" + ex.Message, "PSR Clone",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PromptOpen(string path)
        {
            if (MessageBox.Show(this, "Saved to:\n" + path + "\n\nOpen containing folder?",
                "PSR Clone", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                ReportWriter.ShowFolderContainingFile(path);
            }
        }

        private void ShowHelp()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RepoUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "PSR Clone v" + BuildInfo.Version + " \u2014 a Problem Steps Recorder replacement.\n\n" +
                    "Project & documentation:\n" + RepoUrl + "\n\n(" + ex.Message + ")",
                    "Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddComment()
        {
            if (_recorder == null) return;
            bool wasPaused = _recorder.IsPaused;
            _recorder.Pause();
            Hide();
            System.Threading.Thread.Sleep(150);
            try
            {
                using (var overlay = new CommentOverlayForm())
                {
                    if (overlay.ShowDialog() == DialogResult.OK && overlay.CommentText != null)
                    {
                        _recorder.AddComment(overlay.CommentText, overlay.CapturedBitmap, overlay.HighlightRect);
                        _count.Text = "Steps: " + _recorder.Count;
                    }
                    else if (overlay.CapturedBitmap != null)
                    {
                        overlay.CapturedBitmap.Dispose();
                    }
                }
            }
            finally
            {
                Show();
                if (!wasPaused) _recorder.Resume();
            }
        }

        private void ShowSettings()
        {
            using (var dlg = new SettingsForm(_settings))
            {
                dlg.ShowDialog(this);
            }
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= n ? s : s.Substring(0, n) + "\u2026";
        }
    }
}
