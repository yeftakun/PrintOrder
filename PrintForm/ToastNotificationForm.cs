using System.Drawing.Drawing2D;

namespace PrintForm
{
    internal sealed class ToastNotificationForm : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        private static readonly object SyncRoot = new object();
        private static ToastNotificationForm? CurrentToast;

        private readonly System.Windows.Forms.Timer _closeTimer = new System.Windows.Forms.Timer();

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        private ToastNotificationForm(string title, string message, TimeSpan duration, Rectangle workingArea)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.White;
            ClientSize = new Size(360, 96);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            Location = new Point(
                workingArea.Right - Width - 18,
                workingArea.Bottom - Height - 18);

            var accentBar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(5, Height),
                BackColor = UiTheme.Accent
            };

            var titleLabel = new Label
            {
                AutoSize = false,
                Location = new Point(22, 16),
                Size = new Size(310, 24),
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = title,
                AutoEllipsis = true
            };

            var messageLabel = new Label
            {
                AutoSize = false,
                Location = new Point(22, 42),
                Size = new Size(315, 38),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = message,
                AutoEllipsis = true
            };

            Controls.Add(accentBar);
            Controls.Add(titleLabel);
            Controls.Add(messageLabel);

            Click += (_, _) => Close();
            titleLabel.Click += (_, _) => Close();
            messageLabel.Click += (_, _) => Close();

            _closeTimer.Interval = Math.Max(1000, (int)duration.TotalMilliseconds);
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                Close();
            };
        }

        public static void ShowNotification(Control? owner, string title, string message, TimeSpan duration)
        {
            if (owner != null && !owner.IsDisposed && owner.InvokeRequired)
            {
                try
                {
                    owner.BeginInvoke(new Action(() => ShowNotification(owner, title, message, duration)));
                }
                catch
                {
                    // Abaikan jika form sedang ditutup.
                }

                return;
            }

            lock (SyncRoot)
            {
                CloseCurrentToastNoLock();

                var screen = owner == null || owner.IsDisposed
                    ? Screen.PrimaryScreen
                    : Screen.FromControl(owner);

                var workingArea = screen?.WorkingArea ?? Screen.PrimaryScreen!.WorkingArea;
                var toast = new ToastNotificationForm(title, message, duration, workingArea);

                CurrentToast = toast;
                toast.FormClosed += (_, _) =>
                {
                    lock (SyncRoot)
                    {
                        if (ReferenceEquals(CurrentToast, toast))
                        {
                            CurrentToast = null;
                        }
                    }
                };

                toast.Show(owner?.FindForm());
                toast._closeTimer.Start();
            }
        }

        private static void CloseCurrentToastNoLock()
        {
            var current = CurrentToast;
            if (current == null)
            {
                return;
            }

            try
            {
                if (!current.IsDisposed)
                {
                    current._closeTimer.Stop();
                    current.Close();
                    current.Dispose();
                }
            }
            catch
            {
                // Abaikan.
            }

            CurrentToast = null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var path = UiDrawing.CreateRoundedRectangle(rect, 12);
            var oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var path = UiDrawing.CreateRoundedRectangle(rect, 12);
            using var fillBrush = new SolidBrush(Color.White);
            using var borderPen = new Pen(UiTheme.Border, 1F);

            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closeTimer.Stop();
            _closeTimer.Dispose();

            base.OnFormClosed(e);
        }
    }
}