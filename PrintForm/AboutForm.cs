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
            ClientSize = new Size(540, 386);
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
                Padding = new Padding(22, 18, 22, 18)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground
            };

            var logo = new PictureBox
            {
                Location = new Point(0, 5),
                Size = new Size(46, 46),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            _logoImage = TryLoadLogoImage();
            logo.Image = _logoImage;

            panel.Controls.Add(logo);
            panel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(62, 3),
                Size = new Size(360, 32),
                Text = "PrintOrder Client",
                TextAlign = ContentAlignment.MiddleLeft
            });

            panel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Location = new Point(64, 35),
                Size = new Size(360, 24),
                Text = $"Versi {ResolveVersion()}",
                TextAlign = ContentAlignment.MiddleLeft
            });

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
                Padding = new Padding(18, 16, 18, 14)
            };

            card.Controls.Add(CreateDescriptionLabel());
            AddInfoRow(card, 0, "Runtime", ".NET 8 Windows");
            AddInfoRow(card, 1, "Client ID", _clientId, CreateSmallButton("Copy", CopyClientId));
            AddInfoRow(card, 2, "Base URL", _baseUrl);
            AddInfoRow(card, 3, "Konfigurasi", AppConfig.GetConfigFilePath(), CreateSmallButton("Buka", OpenConfigFolder));

            return card;
        }

        private Control BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground
            };

            footer.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Location = new Point(0, 14),
                Size = new Size(260, 24),
                Text = $"Copyright {DateTime.Now.Year} PrintOrder",
                TextAlign = ContentAlignment.MiddleLeft
            });

            var closeButton = new RoundedButton
            {
                Text = "Tutup",
                UseAccentFill = true,
                Location = new Point(378, 7),
                Size = new Size(118, 40)
            };
            closeButton.Click += (_, _) => Close();
            footer.Controls.Add(closeButton);

            return footer;
        }

        private static Label CreateDescriptionLabel()
        {
            return new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(52, 63, 82),
                Location = new Point(18, 14),
                Size = new Size(460, 42),
                Text = "Desktop client untuk menerima dan mencetak tugas dari server PrintOrder.",
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void AddInfoRow(Control parent, int index, string label, string value, Control? action = null)
        {
            var top = 68 + index * 42;

            parent.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(18, top),
                Size = new Size(108, 26),
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft
            });

            parent.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.4F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Location = new Point(136, top),
                Size = new Size(action == null ? 342 : 262, 26),
                Text = value,
                TextAlign = ContentAlignment.MiddleLeft
            });

            if (action != null)
            {
                action.Location = new Point(410, top - 4);
                action.Size = new Size(68, 34);
                parent.Controls.Add(action);
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
