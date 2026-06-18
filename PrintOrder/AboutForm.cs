using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PrintOrder
{
    internal sealed class AboutForm : Form
    {
        private readonly string _clientId;
        private readonly string _baseUrl;
        private readonly ToolTip _toolTip = new();

        public AboutForm(string? clientId, string baseUrl)
        {
            _clientId = string.IsNullOrWhiteSpace(clientId) ? "-" : clientId.Trim();
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "-" : baseUrl.Trim();

            InitializeLayout();
        }

        private void InitializeLayout()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.PageBackground;
            ClientSize = new Size(560, 320);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Tentang PrintOrder";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(26, 24, 26, 18)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            root.Controls.Add(BuildContent(), 0, 0);
            root.Controls.Add(BuildFooter(), 0, 1);
            Controls.Add(root);
        }

        private Control BuildContent()
        {
            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 4,
                RowCount = 4,
                Margin = Padding.Empty
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));

            for (var i = 0; i < 4; i++)
            {
                details.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            }

            AddInfoRow(details, 0, "Runtime", ".NET 8 Windows");
            AddInfoRow(details, 1, "Client ID", _clientId, CreateSmallButton("Copy", CopyClientId));
            AddInfoRow(details, 2, "Base URL", _baseUrl);
            var configPath = AppConfig.GetConfigFilePath();
            AddInfoRow(details, 3, "Konfigurasi", configPath, CreateSmallButton("Buka", OpenConfigFolder), CreateConfigPathDisplay(configPath));

            content.Controls.Add(CreateDescriptionLabel(), 0, 0);
            content.Controls.Add(details, 0, 1);

            return content;
        }

        private Control BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));

            footer.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = new Padding(0, 10, 12, 0),
                Text = $"Copyright {DateTime.Now.Year} PrintOrder",
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var closeButton = new RoundedButton
            {
                Text = "Tutup",
                UseAccentFill = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Margin = new Padding(0, 8, 0, 0),
                Size = new Size(118, 38)
            };
            closeButton.Click += (_, _) => Close();
            footer.Controls.Add(closeButton, 1, 0);

            return footer;
        }

        private static Label CreateDescriptionLabel()
        {
            return new Label
            {
                AutoEllipsis = true,
                BackColor = UiTheme.PageBackground,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(52, 63, 82),
                Margin = new Padding(4, 0, 4, 8),
                Text = "Desktop client untuk menerima dan mencetak tugas dari server PrintOrder.",
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void AddInfoRow(TableLayoutPanel parent, int index, string label, string value, Control? action = null, string? displayValue = null)
        {
            var labelControl = new Label
            {
                AutoEllipsis = true,
                BackColor = UiTheme.PageBackground,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Margin = new Padding(4, 0, 0, 0),
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var separator = new Label
            {
                BackColor = UiTheme.PageBackground,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = Padding.Empty,
                Text = ":",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var valueControl = new SingleLineTextControl
            {
                BackColor = UiTheme.PageBackground,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = new Padding(10, 0, action == null ? 4 : 12, 0),
                Text = displayValue ?? value
            };
            _toolTip.SetToolTip(valueControl, value);

            parent.Controls.Add(labelControl, 0, index);
            parent.Controls.Add(separator, 1, index);
            parent.Controls.Add(valueControl, 2, index);

            if (action != null)
            {
                action.Anchor = AnchorStyles.Right;
                action.Margin = new Padding(0, 3, 4, 3);
                parent.Controls.Add(action, 3, index);
            }
            else
            {
                parent.Controls.Add(new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = UiTheme.PageBackground,
                    Margin = Padding.Empty
                }, 3, index);
            }
        }

        private static RoundedButton CreateSmallButton(string text, EventHandler click)
        {
            var button = new RoundedButton
            {
                Text = text,
                UseAccentFill = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                CornerRadius = 8,
                Size = new Size(70, 34)
            };
            button.Click += click;
            return button;
        }

        private static string CreateConfigPathDisplay(string path)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData) &&
                path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
            {
                var relative = path[localAppData.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(relative))
                {
                    return $@"%LocalAppData%\{relative}";
                }
            }

            return path;
        }

        private void CopyClientId(object? sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_clientId) && _clientId != "-")
            {
                Clipboard.SetText(_clientId);
            }
        }

        private static void OpenConfigFolder(object? sender, EventArgs e)
        {
            var folder = Path.GetDirectoryName(AppConfig.GetConfigFilePath());
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class SingleLineTextControl : Control
        {
            public SingleLineTextControl()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);

                BackColor = UiTheme.PageBackground;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix |
                    TextFormatFlags.NoPadding);
            }
        }
    }
}
