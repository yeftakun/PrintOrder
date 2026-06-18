using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
// yefta ganteng -yuda
namespace PrintOrder
{
    internal sealed class LoginForm : PairingDialogBase
    {
        private readonly DialogTextInput _identifierField = new DialogTextInput();
        private readonly DialogTextInput _passwordField = new DialogTextInput(isSecret: true);
        private readonly Label _errorLabel = new Label();

        public string Identifier => _identifierField.Value.Trim();
        public string Password => _passwordField.Value;

        public LoginForm(string? lastIdentifier)
            : base("Pair Akun", new Size(720, 424))
        {
            BuildLayout(lastIdentifier);
        }

        private void BuildLayout(string? lastIdentifier)
        {
            Body.Padding = new Padding(64, 30, 64, 24);

            var header = new Panel
            {
                Height = 46,
                BackColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            header.Controls.Add(new PairingBodyIcon(PairingBodyIconKind.Account)
            {
                Location = new Point(0, 5),
                Size = new Size(36, 36),
                IconColor = UiTheme.Accent
            });

            header.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 15.5F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(54, 1),
                Size = new Size(420, 40),
                Text = "Hubungkan Akun",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var identifierLabel = CreateFieldLabel("Username atau Email");
            var passwordLabel = CreateFieldLabel("Password");
            var hintLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Gunakan akun mitra yang terdaftar untuk menghubungkan client ini ke server PrintOrder.",
                TextAlign = ContentAlignment.MiddleLeft
            };

            _identifierField.PlaceholderText = "Masukkan username atau email";
            _identifierField.Value = string.IsNullOrWhiteSpace(lastIdentifier) ? string.Empty : lastIdentifier.Trim();
            _passwordField.PlaceholderText = "Masukkan password";

            ConfigureErrorLabel(_errorLabel);
            _errorLabel.BackColor = Color.White;
            _errorLabel.Visible = false;

            var buttonBar = CreateButtonBar("Pair Akun", Submit);

            Body.Controls.Add(header);
            Body.Controls.Add(identifierLabel);
            Body.Controls.Add(_identifierField);
            Body.Controls.Add(passwordLabel);
            Body.Controls.Add(_passwordField);
            Body.Controls.Add(hintLabel);
            Body.Controls.Add(_errorLabel);
            Body.Controls.Add(buttonBar);

            Body.Resize += (_, _) =>
            {
                var width = Body.ClientSize.Width - Body.Padding.Horizontal;
                header.SetBounds(Body.Padding.Left, Body.Padding.Top, width, 46);
                identifierLabel.SetBounds(Body.Padding.Left, header.Bottom + 10, width, 24);
                _identifierField.SetBounds(Body.Padding.Left, identifierLabel.Bottom + 4, width, 46);
                passwordLabel.SetBounds(Body.Padding.Left, _identifierField.Bottom + 12, width, 24);
                _passwordField.SetBounds(Body.Padding.Left, passwordLabel.Bottom + 4, width, 46);
                hintLabel.SetBounds(Body.Padding.Left, _passwordField.Bottom + 8, width, 22);
                _errorLabel.Bounds = hintLabel.Bounds;
                buttonBar.SetBounds(Body.Padding.Left, Body.ClientSize.Height - Body.Padding.Bottom - 50, width, 50);
            };

            Shown += (_, _) =>
            {
                if (_identifierField.ValueLength == 0)
                {
                    _identifierField.FocusInput();
                    return;
                }

                _passwordField.FocusInput();
            };
        }

        protected override void Submit()
        {
            ClearError();

            if (Identifier.Length == 0)
            {
                ShowError(_errorLabel, "Username atau email wajib diisi.");
                _identifierField.FocusInput();
                return;
            }

            if (Password.Length == 0)
            {
                ShowError(_errorLabel, "Password wajib diisi.");
                _passwordField.FocusInput();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ClearError()
        {
            _errorLabel.Text = string.Empty;
            _errorLabel.Visible = false;
        }
    }

    internal sealed class UnpairPinDialog : PairingDialogBase
    {
        private readonly DialogTextInput _pinField = new DialogTextInput(isSecret: true, maxLength: 8);
        private readonly Label _errorLabel = new Label();

        public string Pin => _pinField.Value.Trim();

        public UnpairPinDialog()
            : base("Lepas Pairing", new Size(700, 366))
        {
            BuildLayout();
        }

        private void BuildLayout()
        {
            Body.Padding = new Padding(64, 30, 64, 24);

            var header = new Panel
            {
                Height = 46,
                BackColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            header.Controls.Add(new PairingBodyIcon(PairingBodyIconKind.ShieldLock)
            {
                Location = new Point(0, 2),
                Size = new Size(42, 42),
                IconColor = UiTheme.Accent
            });

            header.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 15.5F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(56, 1),
                Size = new Size(410, 40),
                Text = "Verifikasi PIN",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var descriptionLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 11.2F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Masukkan PIN untuk melepaskan pairing client ini dari akun PrintOrder.",
                TextAlign = ContentAlignment.MiddleLeft
            };

            var pinLabel = CreateFieldLabel("PIN");
            _pinField.PlaceholderText = "Masukkan PIN";
            ConfigureErrorLabel(_errorLabel);
            _errorLabel.BackColor = Color.White;
            _errorLabel.Visible = false;

            var buttonBar = CreateButtonBar("Verifikasi", Submit);

            Body.Controls.Add(header);
            Body.Controls.Add(descriptionLabel);
            Body.Controls.Add(pinLabel);
            Body.Controls.Add(_pinField);
            Body.Controls.Add(_errorLabel);
            Body.Controls.Add(buttonBar);

            Body.Resize += (_, _) =>
            {
                var width = Body.ClientSize.Width - Body.Padding.Horizontal;
                header.SetBounds(Body.Padding.Left, Body.Padding.Top, width, 46);
                descriptionLabel.SetBounds(Body.Padding.Left, header.Bottom + 10, width, 28);
                pinLabel.SetBounds(Body.Padding.Left, descriptionLabel.Bottom + 10, width, 24);
                _pinField.SetBounds(Body.Padding.Left, pinLabel.Bottom + 4, width, 46);
                _errorLabel.SetBounds(Body.Padding.Left, _pinField.Bottom + 6, width, 22);
                buttonBar.SetBounds(Body.Padding.Left, Body.ClientSize.Height - Body.Padding.Bottom - 50, width, 50);
            };

            Shown += (_, _) => _pinField.FocusInput();
        }

        protected override void Submit()
        {
            _errorLabel.Text = string.Empty;
            _errorLabel.Visible = false;

            if (Pin.Length < 4 || Pin.Length > 8 || !Pin.All(char.IsDigit))
            {
                ShowError(_errorLabel, "PIN harus 4-8 digit angka.");
                _pinField.FocusInput();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal abstract class PairingDialogBase : Form
    {
        protected Panel Body { get; } = new Panel();

        protected PairingDialogBase(string title, Size clientSize)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            ClientSize = clientSize;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = title;

            BuildShell(title);
        }

        public DialogResult ShowPairingDialog(Form owner)
        {
            if (owner == null || owner.IsDisposed || owner.WindowState == FormWindowState.Minimized)
            {
                return ShowDialog(owner);
            }

            var clientBounds = owner.RectangleToScreen(owner.ClientRectangle);
            CenterOver(clientBounds);

            using var backdrop = new ModalBackdropPanel(owner);
            owner.Controls.Add(backdrop);
            backdrop.BringToFront();
            backdrop.Refresh();

            try
            {
                return ShowDialog(owner);
            }
            finally
            {
                owner.Controls.Remove(backdrop);
                owner.Activate();
            }
        }

        protected abstract void Submit();

        protected static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        protected static void ConfigureErrorLabel(Label label)
        {
            label.AutoEllipsis = true;
            label.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            label.ForeColor = JobVisuals.Danger;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        protected static void ShowError(Label label, string message)
        {
            label.Text = message;
            label.Visible = true;
        }

        protected Panel CreateButtonBar(string primaryText, Action primaryAction)
        {
            var bar = new Panel
            {
                BackColor = Color.White
            };

            var cancelButton = new RoundedButton
            {
                Text = "Batal",
                Size = new Size(148, 46),
                UseAccentFill = false
            };
            cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var primaryButton = new RoundedButton
            {
                Text = primaryText,
                Size = new Size(148, 46),
                UseAccentFill = true
            };
            primaryButton.Click += (_, _) => primaryAction();

            bar.Controls.Add(cancelButton);
            bar.Controls.Add(primaryButton);
            bar.Resize += (_, _) =>
            {
                var top = Math.Max(0, (bar.Height - primaryButton.Height) / 2);
                primaryButton.Location = new Point(bar.Width - primaryButton.Width, top);
                cancelButton.Location = new Point(primaryButton.Left - 18 - cancelButton.Width, top);
            };

            return bar;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.Enter)
            {
                Submit();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using var path = UiDrawing.CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), 12);
            var oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
        }

        private void BuildShell(string title)
        {
            var shell = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.White,
                BorderColor = UiTheme.Border,
                BorderThickness = 1,
                CornerRadius = 12,
                Padding = new Padding(0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(BuildTitleBar(title), 0, 0);
            layout.Controls.Add(new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Border
            }, 0, 1);

            Body.Dock = DockStyle.Fill;
            Body.BackColor = Color.White;
            layout.Controls.Add(Body, 0, 2);

            shell.Controls.Add(layout);
            Controls.Add(shell);
        }

        private Control BuildTitleBar(string title)
        {
            var titleBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(28, 0, 22, 0)
            };
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));

            titleBar.Controls.Add(new PairingTitleIcon
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 13, 10, 13)
            }, 0, 0);

            titleBar.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                ForeColor = UiTheme.Text,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            }, 1, 0);

            var closeButton = new DialogCloseButton
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 15, 0, 15)
            };
            closeButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            titleBar.Controls.Add(closeButton, 2, 0);

            return titleBar;
        }

        private void CenterOver(Rectangle bounds)
        {
            Location = new Point(
                bounds.Left + Math.Max(0, (bounds.Width - Width) / 2),
                bounds.Top + Math.Max(0, (bounds.Height - Height) / 2));
        }
    }

    internal sealed class ModalBackdropPanel : Control
    {
        private readonly Bitmap? _background;

        public ModalBackdropPanel(Form owner)
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.PageBackground;
            Dock = DockStyle.Fill;
            Enabled = false;

            _background = CaptureBlurredClient(owner);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _background?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_background != null)
            {
                e.Graphics.DrawImage(_background, ClientRectangle);
            }
            else
            {
                using var fallbackBrush = new SolidBrush(UiTheme.PageBackground);
                e.Graphics.FillRectangle(fallbackBrush, ClientRectangle);
            }

            using var veilBrush = new SolidBrush(Color.FromArgb(116, 245, 247, 250));
            e.Graphics.FillRectangle(veilBrush, ClientRectangle);
        }

        private static Bitmap? CaptureBlurredClient(Form owner)
        {
            var size = owner.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            try
            {
                using var capture = new Bitmap(size.Width, size.Height);
                using (var graphics = Graphics.FromImage(capture))
                {
                    graphics.CopyFromScreen(owner.PointToScreen(Point.Empty), Point.Empty, size);
                }

                return CreateSoftBlur(capture);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap CreateSoftBlur(Bitmap source)
        {
            var smallWidth = Math.Max(1, source.Width / 14);
            var smallHeight = Math.Max(1, source.Height / 14);

            using var small = new Bitmap(smallWidth, smallHeight);
            using (var graphics = Graphics.FromImage(small))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight));
            }

            var blurred = new Bitmap(source.Width, source.Height);
            using (var graphics = Graphics.FromImage(blurred))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(small, new Rectangle(0, 0, source.Width, source.Height));
            }

            return blurred;
        }
    }

    internal sealed class DialogTextInput : RoundedPanel
    {
        private readonly TextBox _textBox = new TextBox();
        private readonly PasswordToggleButton? _toggleButton;
        private readonly bool _isSecret;
        private bool _showSecret;

        public DialogTextInput(bool isSecret = false, int maxLength = 0)
        {
            _isSecret = isSecret;
            CornerRadius = 8;
            FillColor = Color.White;
            BorderColor = Color.FromArgb(205, 211, 220);
            Padding = new Padding(15, 0, isSecret ? 50 : 15, 0);

            _textBox.BorderStyle = BorderStyle.None;
            _textBox.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            _textBox.ForeColor = UiTheme.Text;
            _textBox.BackColor = Color.White;
            _textBox.MaxLength = maxLength;
            _textBox.UseSystemPasswordChar = isSecret;
            _textBox.Enter += (_, _) =>
            {
                BorderColor = Color.FromArgb(170, 178, 190);
                Invalidate();
            };
            _textBox.Leave += (_, _) =>
            {
                BorderColor = Color.FromArgb(205, 211, 220);
                Invalidate();
            };

            Controls.Add(_textBox);

            if (isSecret)
            {
                _toggleButton = new PasswordToggleButton();
                _toggleButton.Click += (_, _) =>
                {
                    _showSecret = !_showSecret;
                    _textBox.UseSystemPasswordChar = !_showSecret;
                    _toggleButton.PasswordVisible = _showSecret;
                    _toggleButton.Invalidate();
                };
                Controls.Add(_toggleButton);
            }
        }

        public string Value
        {
            get => _textBox.Text ?? string.Empty;
            set => _textBox.Text = value ?? string.Empty;
        }

        public int ValueLength => _textBox.TextLength;

        public string PlaceholderText
        {
            get => _textBox.PlaceholderText;
            set => _textBox.PlaceholderText = value;
        }

        public void FocusInput()
        {
            _textBox.Focus();
            _textBox.SelectAll();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);

            var textHeight = Math.Min(28, Math.Max(23, Height - 16));
            var textTop = Math.Max(0, (Height - textHeight) / 2);
            var rightInset = _isSecret ? 48 : 15;
            _textBox.SetBounds(15, textTop, Math.Max(10, Width - 15 - rightInset), textHeight);

            if (_toggleButton != null)
            {
                _toggleButton.SetBounds(Width - 42, Math.Max(0, (Height - 32) / 2), 32, 32);
            }
        }
    }

    internal sealed class PasswordToggleButton : Control
    {
        private bool _isHovered;

        public bool PasswordVisible { get; set; }

        public PasswordToggleButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var color = _isHovered ? UiTheme.Text : Color.FromArgb(63, 70, 84);
            var rect = Rectangle.Inflate(ClientRectangle, -5, -6);
            var names = PasswordVisible
                ? new[] { "eye-off", "lucide-eye-off" }
                : new[] { "eye", "lucide-eye" };

            if (JobLucideAssets.TryDrawNamed(e.Graphics, names, rect, color, tint: true))
            {
                return;
            }

            DrawEye(e.Graphics, rect, color, PasswordVisible);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        private static void DrawEye(Graphics graphics, Rectangle rect, Color color, bool crossed)
        {
            using var pen = new Pen(color, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            graphics.DrawArc(pen, rect.Left, rect.Top + rect.Height / 5, rect.Width, rect.Height * 3 / 5, 190, 160);
            graphics.DrawArc(pen, rect.Left, rect.Top + rect.Height / 5, rect.Width, rect.Height * 3 / 5, 10, 160);
            graphics.DrawEllipse(pen, center.X - 4, center.Y - 4, 8, 8);

            if (crossed)
            {
                graphics.DrawLine(pen, rect.Left + 2, rect.Bottom - 2, rect.Right - 2, rect.Top + 2);
            }
        }
    }

    internal sealed class DialogCloseButton : Control
    {
        private bool _isHovered;

        public DialogCloseButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_isHovered)
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(245, 247, 250));
                using var hoverPath = UiDrawing.CreateRoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 8);
                e.Graphics.FillPath(hoverBrush, hoverPath);
            }

            var rect = Rectangle.Inflate(ClientRectangle, -9, -9);
            if (JobLucideAssets.TryDrawNamed(e.Graphics, new[] { "x", "lucide-x" }, rect, UiTheme.Text, tint: true))
            {
                return;
            }

            using var pen = new Pen(UiTheme.Text, 2.1F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(pen, rect.Left + 3, rect.Top + 3, rect.Right - 3, rect.Bottom - 3);
            e.Graphics.DrawLine(pen, rect.Right - 3, rect.Top + 3, rect.Left + 3, rect.Bottom - 3);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }
    }

    internal sealed class PairingTitleIcon : Control
    {
        public PairingTitleIcon()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = Rectangle.Inflate(ClientRectangle, -2, -2);
            if (JobLucideAssets.TryDrawNamed(
                    e.Graphics,
                    new[] { "file-plus-2", "file-plus", "lucide-file-plus-2", "lucide-file-plus" },
                    rect,
                    UiTheme.Text,
                    tint: false))
            {
                return;
            }

            DrawDocumentPlus(e.Graphics, rect);
        }

        private static void DrawDocumentPlus(Graphics graphics, Rectangle rect)
        {
            var docRect = new Rectangle(rect.Left + 2, rect.Top + 1, rect.Width - 12, rect.Height - 3);
            using var path = new GraphicsPath();
            path.AddLine(docRect.Left, docRect.Top, docRect.Right - 10, docRect.Top);
            path.AddLine(docRect.Right - 10, docRect.Top, docRect.Right, docRect.Top + 10);
            path.AddLine(docRect.Right, docRect.Top + 10, docRect.Right, docRect.Bottom);
            path.AddLine(docRect.Right, docRect.Bottom, docRect.Left, docRect.Bottom);
            path.AddLine(docRect.Left, docRect.Bottom, docRect.Left, docRect.Top);
            path.CloseFigure();

            using var fillBrush = new SolidBrush(Color.White);
            using var borderPen = new Pen(UiTheme.Text, 2.1F)
            {
                LineJoin = LineJoin.Round
            };
            graphics.FillPath(fillBrush, path);
            graphics.DrawPath(borderPen, path);

            using var foldBrush = new SolidBrush(UiTheme.Accent);
            graphics.FillPolygon(
                foldBrush,
                new[]
                {
                    new Point(docRect.Right - 10, docRect.Top + 1),
                    new Point(docRect.Right - 1, docRect.Top + 10),
                    new Point(docRect.Right - 10, docRect.Top + 10)
                });

            using var linePen = new Pen(UiTheme.Text, 1.8F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(linePen, docRect.Left + 8, docRect.Top + 14, docRect.Left + 20, docRect.Top + 14);
            graphics.DrawLine(linePen, docRect.Left + 8, docRect.Top + 22, docRect.Left + 25, docRect.Top + 22);
            graphics.DrawLine(linePen, docRect.Left + 8, docRect.Top + 30, docRect.Left + 22, docRect.Top + 30);

            var badge = new Rectangle(rect.Right - 16, rect.Bottom - 17, 16, 16);
            using var badgeBrush = new SolidBrush(UiTheme.Accent);
            graphics.FillEllipse(badgeBrush, badge);

            using var plusPen = new Pen(Color.White, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(plusPen, badge.Left + 8, badge.Top + 4, badge.Left + 8, badge.Bottom - 4);
            graphics.DrawLine(plusPen, badge.Left + 4, badge.Top + 8, badge.Right - 4, badge.Top + 8);
        }
    }

    internal enum PairingBodyIconKind
    {
        Account,
        ShieldLock
    }

    internal sealed class PairingBodyIcon : Control
    {
        private readonly PairingBodyIconKind _kind;

        public Color IconColor { get; set; } = UiTheme.Accent;

        public PairingBodyIcon(PairingBodyIconKind kind)
        {
            _kind = kind;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = Rectangle.Inflate(ClientRectangle, -2, -2);
            if (_kind == PairingBodyIconKind.Account)
            {
                if (JobLucideAssets.TryDrawNamed(e.Graphics, new[] { "user", "circle-user-round", "lucide-user" }, rect, IconColor, tint: true))
                {
                    return;
                }

                DrawUser(e.Graphics, rect, IconColor);
                return;
            }

            if (JobLucideAssets.TryDrawNamed(e.Graphics, new[] { "shield-lock", "shield-check", "lucide-shield-lock" }, rect, IconColor, tint: true))
            {
                return;
            }

            DrawShieldLock(e.Graphics, rect, IconColor);
        }

        private static void DrawUser(Graphics graphics, Rectangle rect, Color color)
        {
            using var pen = new Pen(color, 2.3F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            graphics.DrawEllipse(pen, rect.Left + rect.Width / 2 - 7, rect.Top + 4, 14, 14);
            graphics.DrawArc(pen, rect.Left + 6, rect.Top + 22, rect.Width - 12, rect.Height - 10, 200, 140);
        }

        private static void DrawShieldLock(Graphics graphics, Rectangle rect, Color color)
        {
            var shield = new[]
            {
                new PointF(rect.Left + rect.Width * 0.5F, rect.Top + 2),
                new PointF(rect.Right - 4, rect.Top + rect.Height * 0.23F),
                new PointF(rect.Right - 6, rect.Top + rect.Height * 0.58F),
                new PointF(rect.Left + rect.Width * 0.5F, rect.Bottom - 2),
                new PointF(rect.Left + 4, rect.Top + rect.Height * 0.58F),
                new PointF(rect.Left + 4, rect.Top + rect.Height * 0.23F)
            };

            using var pen = new Pen(color, 2.2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawPolygon(pen, shield);

            var lockBody = new Rectangle(rect.Left + rect.Width / 2 - 8, rect.Top + rect.Height / 2 - 1, 16, 13);
            graphics.DrawRectangle(pen, lockBody);
            graphics.DrawArc(pen, lockBody.Left + 3, lockBody.Top - 10, 10, 14, 200, 140);

            using var dotBrush = new SolidBrush(color);
            graphics.FillEllipse(dotBrush, rect.Left + rect.Width / 2 - 2, lockBody.Top + 5, 4, 4);
        }
    }
}
