using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PrintForm
{
    internal sealed class AboutForm : Form
    {
        private readonly string _clientId;
        private readonly string _baseUrl;
        private readonly ToolTip _toolTip = new();
        private Image? _logoImage;

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
            ClientSize = new Size(560, 392);
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
                RowCount = 3,
                Padding = new Padding(24, 15, 24, 15)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var logo = new PictureBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 12, 9),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            _logoImage = TryLoadLogoImage();
            logo.Image = _logoImage;

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

            titleStack.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Margin = Padding.Empty,
                Text = "PrintOrder Client",
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);

            titleStack.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = Padding.Empty,
                Text = $"Versi {ResolveVersion()}",
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            panel.Controls.Add(logo, 0, 0);
            panel.Controls.Add(titleStack, 1, 0);

            return panel;
        }

        private Control BuildContent()
        {
            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.White,
                BorderColor = UiTheme.Border,
                CornerRadius = 12,
                Padding = new Padding(18, 14, 18, 14)
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 4,
                RowCount = 4,
                Margin = Padding.Empty
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));

            for (var i = 0; i < 4; i++)
            {
                details.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            }

            AddInfoRow(details, 0, "Runtime", ".NET 8 Windows");
            AddInfoRow(details, 1, "Client ID", _clientId, CreateSmallButton("Copy", CopyClientId));
            AddInfoRow(details, 2, "Base URL", _baseUrl);
            AddInfoRow(details, 3, "Konfigurasi", AppConfig.GetConfigFilePath(), CreateSmallButton("Buka", OpenConfigFolder));

            content.Controls.Add(CreateDescriptionLabel(), 0, 0);
            content.Controls.Add(details, 0, 1);
            card.Controls.Add(content);

            return card;
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
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0)
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
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(52, 63, 82),
                Margin = new Padding(4, 0, 4, 8),
                Text = "Desktop client untuk menerima dan mencetak tugas dari server PrintOrder.",
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void AddInfoRow(TableLayoutPanel parent, int index, string label, string value, Control? action = null)
        {
            var labelControl = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Margin = new Padding(4, 0, 0, 0),
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var separator = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = Padding.Empty,
                Text = ":",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var valueControl = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Margin = new Padding(10, 0, action == null ? 4 : 10, 0),
                Text = value,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _toolTip.SetToolTip(valueControl, value);

            parent.Controls.Add(labelControl, 0, index);
            parent.Controls.Add(separator, 1, index);
            parent.Controls.Add(valueControl, 2, index);

            if (action != null)
            {
                action.Dock = DockStyle.Fill;
                action.Margin = new Padding(0, 2, 4, 2);
                parent.Controls.Add(action, 3, index);
            }
            else
            {
                parent.Controls.Add(new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.White,
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
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            button.Click += click;
            return button;
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

        private static string ResolveVersion()
        {
            var informationalVersion = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            var version = string.IsNullOrWhiteSpace(informationalVersion)
                ? Application.ProductVersion
                : informationalVersion;

            var metadataIndex = version.IndexOf('+');
            return metadataIndex > 0 ? version[..metadataIndex] : version;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
                _logoImage?.Dispose();
                _logoImage = null;
            }

            base.Dispose(disposing);
        }

        private static Image? TryLoadLogoImage()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo_printform.png");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }
    }
}
