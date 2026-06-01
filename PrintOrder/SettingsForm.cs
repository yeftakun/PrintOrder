using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PrintOrder
{
    internal sealed class SettingsForm : Form
    {
        private readonly string _initialBaseUrl;
        private readonly NotificationOptions _initialNotificationOptions;
        private readonly bool _initialAutoStartEnabled;

        private readonly TextBox _baseUrlTextBox = new TextBox();
        private readonly CheckBox _soundNotificationCheckBox = new CheckBox();
        private readonly CheckBox _desktopNotificationCheckBox = new CheckBox();
        private readonly SettingsToggleSwitch _autoStartSwitch = new SettingsToggleSwitch();
        private readonly RoundedButton _testConnectionButton = new RoundedButton();
        private readonly RoundedButton _openConfigFolderButton = new RoundedButton();
        private readonly RoundedButton _refreshPdfEngineButton = new RoundedButton();
        private readonly RoundedButton _downloadPdfEngineButton = new RoundedButton();
        private readonly RoundedButton _saveButton = new RoundedButton();
        private readonly RoundedButton _cancelButton = new RoundedButton();
        private readonly Label _testResultLabel = new Label();
        private readonly Label _pdfEngineStatusLabel = new Label();
        private readonly Label _pdfEnginePathLabel = new Label();

        public string? SavedBaseUrl { get; private set; }
        public NotificationOptions? SavedNotificationOptions { get; private set; }
        public bool SavedChanges { get; private set; }
        public bool BaseUrlChanged { get; private set; }
        public bool NotificationOptionsChanged { get; private set; }
        public bool AutoStartChanged { get; private set; }

        public SettingsForm(string currentBaseUrl, NotificationOptions currentNotificationOptions)
        {
            _initialBaseUrl = (currentBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            _initialNotificationOptions = currentNotificationOptions?.Clone() ?? new NotificationOptions();
            _initialAutoStartEnabled = WindowsAutoStart.IsEnabled();

            InitializeLayout();
        }

        private void InitializeLayout()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.PageBackground;
            ClientSize = new Size(720, 660);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Pengaturan";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(24, 22, 24, 18)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildServerSection(), 0, 1);
            root.Controls.Add(BuildNotificationSection(), 0, 2);
            root.Controls.Add(BuildPdfEngineSection(), 0, 3);
            root.Controls.Add(BuildSystemSection(), 0, 4);
            root.Controls.Add(BuildFooter(), 0, 5);

            Controls.Add(root);

            Shown += (_, _) => _baseUrlTextBox.Focus();
        }

        private Control BuildHeader()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground
            };

            panel.Controls.Add(new IconBadge
            {
                Kind = IconKind.Settings,
                Circle = true,
                CircleBackColor = UiTheme.AccentSoft,
                IconColor = UiTheme.Accent,
                Location = new Point(0, 6),
                Size = new Size(48, 48)
            });

            panel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(64, 0),
                Size = new Size(420, 45),
                Text = "Pengaturan",
                TextAlign = ContentAlignment.MiddleLeft
            });

            // panel.Controls.Add(new Label
            // {
            //     AutoEllipsis = true,
            //     Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            //     ForeColor = UiTheme.MutedText,
            //     Location = new Point(66, 38),
            //     Size = new Size(540, 24),
            //     Text = "Konfigurasi koneksi, notifikasi, dan startup aplikasi.",
            //     TextAlign = ContentAlignment.MiddleLeft
            // });

            return panel;
        }

        private Control BuildServerSection()
        {
            var section = CreateSection();
            section.Controls.Add(CreateSectionTitle("Server", IconKind.Server));

            var baseUrlLabel = CreateFieldLabel("Base URL");
            baseUrlLabel.SetBounds(20, 48, 120, 24);
            section.Controls.Add(baseUrlLabel);

            var inputHost = CreateInputHost();
            inputHost.SetBounds(20, 74, 478, 44);
            _baseUrlTextBox.BorderStyle = BorderStyle.None;
            _baseUrlTextBox.Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
            _baseUrlTextBox.ForeColor = UiTheme.Text;
            _baseUrlTextBox.PlaceholderText = "http://127.0.0.1:3000";
            _baseUrlTextBox.Text = _initialBaseUrl;
            _baseUrlTextBox.SetBounds(14, 11, 448, 24);
            inputHost.Controls.Add(_baseUrlTextBox);
            section.Controls.Add(inputHost);

            _testConnectionButton.Text = "Test Koneksi";
            _testConnectionButton.UseAccentFill = false;
            _testConnectionButton.SetBounds(514, 74, 130, 44);
            _testConnectionButton.Click += async (_, _) => await TestConnectionAsync();
            section.Controls.Add(_testConnectionButton);

            _testResultLabel.AutoEllipsis = true;
            _testResultLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            _testResultLabel.ForeColor = UiTheme.MutedText;
            _testResultLabel.Location = new Point(20, 120);
            _testResultLabel.Size = new Size(624, 22);
            _testResultLabel.Text = "Contoh: http://127.0.0.1:3000";
            section.Controls.Add(_testResultLabel);

            return section;
        }

        private Control BuildNotificationSection()
        {
            var section = CreateSection();
            section.Controls.Add(CreateSectionTitle("Notifikasi", IconKind.Lightning));

            ConfigureCheckBox(_soundNotificationCheckBox, "Suara Notifikasi", _initialNotificationOptions.SoundEnabled);
            _soundNotificationCheckBox.SetBounds(20, 48, 260, 26);
            section.Controls.Add(_soundNotificationCheckBox);

            ConfigureCheckBox(_desktopNotificationCheckBox, "Notifikasi", _initialNotificationOptions.DesktopEnabled);
            _desktopNotificationCheckBox.SetBounds(20, 76, 260, 26);
            section.Controls.Add(_desktopNotificationCheckBox);

            var hint = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Location = new Point(310, 55),
                Size = new Size(330, 44),
                Text = "Notifikasi muncul saat tugas cetak baru diterima.",
                TextAlign = ContentAlignment.MiddleLeft
            };
            section.Controls.Add(hint);

            return section;
        }

        private Control BuildPdfEngineSection()
        {
            var section = CreateSection();
            section.Controls.Add(CreateSectionTitle("PDF Engine", IconKind.Document));

            _pdfEngineStatusLabel.AutoEllipsis = true;
            _pdfEngineStatusLabel.Font = new Font("Segoe UI", 10.2F, FontStyle.Bold);
            _pdfEngineStatusLabel.Location = new Point(20, 48);
            _pdfEngineStatusLabel.Size = new Size(382, 26);
            _pdfEngineStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            section.Controls.Add(_pdfEngineStatusLabel);

            _pdfEnginePathLabel.AutoEllipsis = true;
            _pdfEnginePathLabel.Font = new Font("Segoe UI", 8.9F, FontStyle.Regular);
            _pdfEnginePathLabel.ForeColor = UiTheme.MutedText;
            _pdfEnginePathLabel.Location = new Point(20, 74);
            _pdfEnginePathLabel.Size = new Size(382, 22);
            _pdfEnginePathLabel.TextAlign = ContentAlignment.MiddleLeft;
            section.Controls.Add(_pdfEnginePathLabel);

            _refreshPdfEngineButton.Text = "Deteksi Ulang";
            _refreshPdfEngineButton.UseAccentFill = false;
            _refreshPdfEngineButton.SetBounds(422, 50, 108, 40);
            _refreshPdfEngineButton.Click += (_, _) => RefreshPdfEngineStatus();
            section.Controls.Add(_refreshPdfEngineButton);

            _downloadPdfEngineButton.Text = "Download";
            _downloadPdfEngineButton.UseAccentFill = false;
            _downloadPdfEngineButton.SetBounds(542, 50, 102, 40);
            _downloadPdfEngineButton.Click += (_, _) => OpenSumatraPdfDownload();
            section.Controls.Add(_downloadPdfEngineButton);

            RefreshPdfEngineStatus();
            return section;
        }

        private Control BuildSystemSection()
        {
            var section = CreateSection();
            section.Controls.Add(CreateSectionTitle("Sistem", IconKind.Settings));

            var autoStartLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Regular),
                ForeColor = UiTheme.Text,
                Location = new Point(20, 50),
                Size = new Size(210, 28),
                Text = "Auto start Windows",
                TextAlign = ContentAlignment.MiddleLeft
            };
            section.Controls.Add(autoStartLabel);

            _autoStartSwitch.Checked = _initialAutoStartEnabled;
            _autoStartSwitch.Location = new Point(236, 49);
            _autoStartSwitch.Size = new Size(56, 30);
            section.Controls.Add(_autoStartSwitch);

            _openConfigFolderButton.Text = "Buka Folder Konfigurasi";
            _openConfigFolderButton.UseAccentFill = false;
            _openConfigFolderButton.SetBounds(424, 42, 220, 42);
            _openConfigFolderButton.Click += (_, _) => OpenConfigFolder();
            section.Controls.Add(_openConfigFolderButton);

            return section;
        }

        private Control BuildFooter()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground
            };

            var configPathLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.8F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Location = new Point(0, 10),
                Size = new Size(430, 34),
                Text = $"File: {AppConfig.GetConfigFilePath()}",
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(configPathLabel);

            _saveButton.Text = "Simpan";
            _saveButton.UseAccentFill = true;
            _saveButton.SetBounds(446, 10, 106, 42);
            _saveButton.Click += SaveButton_Click;
            panel.Controls.Add(_saveButton);

            _cancelButton.Text = "Batal";
            _cancelButton.UseAccentFill = false;
            _cancelButton.SetBounds(562, 10, 106, 42);
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            panel.Controls.Add(_cancelButton);

            return panel;
        }

        private async Task TestConnectionAsync()
        {
            var inputValue = NormalizeBaseUrlInput();
            if (!AppConfig.IsValidServerBaseUrl(inputValue))
            {
                SetTestResult("Base URL tidak valid.", JobVisuals.Danger);
                _baseUrlTextBox.Focus();
                return;
            }

            _testConnectionButton.Enabled = false;
            _testConnectionButton.Text = "Menguji...";
            SetTestResult("Menghubungi server...", UiTheme.MutedText);

            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                using var response = await client.GetAsync(inputValue, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode < 500)
                {
                    SetTestResult($"Server merespons ({(int)response.StatusCode}).", UiTheme.Success);
                }
                else
                {
                    SetTestResult($"Server merespons dengan error ({(int)response.StatusCode}).", JobVisuals.Warning);
                }
            }
            catch (TaskCanceledException)
            {
                SetTestResult("Koneksi timeout. Periksa server atau jaringan.", JobVisuals.Danger);
            }
            catch (Exception ex)
            {
                SetTestResult($"Tidak bisa terhubung: {ex.Message}", JobVisuals.Danger);
            }
            finally
            {
                _testConnectionButton.Text = "Test Koneksi";
                _testConnectionButton.Enabled = true;
            }
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            var inputValue = NormalizeBaseUrlInput();
            if (!AppConfig.IsValidServerBaseUrl(inputValue))
            {
                MessageBox.Show(
                    this,
                    "Nilai Base URL tidak valid. Gunakan URL HTTP/HTTPS yang lengkap.",
                    "Validasi Gagal",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                _baseUrlTextBox.Focus();
                return;
            }

            var newNotificationOptions = new NotificationOptions
            {
                SoundEnabled = _soundNotificationCheckBox.Checked,
                DesktopEnabled = _desktopNotificationCheckBox.Checked
            };

            BaseUrlChanged = !string.Equals(inputValue, _initialBaseUrl, StringComparison.OrdinalIgnoreCase);
            NotificationOptionsChanged =
                newNotificationOptions.SoundEnabled != _initialNotificationOptions.SoundEnabled
                || newNotificationOptions.DesktopEnabled != _initialNotificationOptions.DesktopEnabled;
            AutoStartChanged = _autoStartSwitch.Checked != _initialAutoStartEnabled;

            if (!BaseUrlChanged && !NotificationOptionsChanged && !AutoStartChanged)
            {
                MessageBox.Show(
                    this,
                    "Tidak ada perubahan konfigurasi.",
                    "Informasi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            try
            {
                if (BaseUrlChanged || NotificationOptionsChanged)
                {
                    AppConfig.SaveAppSettings(inputValue, newNotificationOptions);
                }

                if (AutoStartChanged)
                {
                    WindowsAutoStart.SetEnabled(_autoStartSwitch.Checked);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    this,
                    $"Tidak punya izin menulis konfigurasi di {AppConfig.GetConfigFilePath()}.",
                    "Gagal Menyimpan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Gagal menyimpan pengaturan: {ex.Message}",
                    "Gagal Menyimpan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            SavedBaseUrl = inputValue;
            SavedNotificationOptions = newNotificationOptions;
            SavedChanges = true;

            MessageBox.Show(
                this,
                "Pengaturan berhasil disimpan.",
                "Pengaturan Tersimpan",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }

        private string NormalizeBaseUrlInput()
        {
            return (_baseUrlTextBox.Text ?? string.Empty).Trim().Trim('"').TrimEnd('/');
        }

        private void SetTestResult(string message, Color color)
        {
            _testResultLabel.ForeColor = color;
            _testResultLabel.Text = message;
        }

        private void RefreshPdfEngineStatus()
        {
            var installation = SumatraPdfSupport.Detect();
            if (installation.IsAvailable)
            {
                _pdfEngineStatusLabel.ForeColor = UiTheme.Success;
                _pdfEngineStatusLabel.Text = "SumatraPDF ditemukan";
                _pdfEnginePathLabel.Text = installation.ExecutablePath ?? string.Empty;
                return;
            }

            _pdfEngineStatusLabel.ForeColor = JobVisuals.Warning;
            _pdfEngineStatusLabel.Text = "SumatraPDF belum ditemukan";
            _pdfEnginePathLabel.Text = "Diperlukan untuk PDF dengan rentang halaman seperti 1, 3-5.";
        }

        private void OpenSumatraPdfDownload()
        {
            try
            {
                SumatraPdfSupport.OpenDownloadPage();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Tidak bisa membuka halaman download: {ex.Message}",
                    "Download SumatraPDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static RoundedPanel CreateSection()
        {
            return new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.White,
                BorderColor = UiTheme.Border,
                CornerRadius = 12,
                Margin = new Padding(0, 0, 0, 12)
            };
        }

        private static Control CreateSectionTitle(string text, IconKind iconKind)
        {
            var panel = new Panel
            {
                BackColor = Color.White,
                Location = new Point(18, 14),
                Size = new Size(320, 30)
            };

            panel.Controls.Add(new IconBadge
            {
                Kind = iconKind,
                Circle = false,
                IconColor = UiTheme.Accent,
                Location = new Point(0, 3),
                Size = new Size(24, 24)
            });

            panel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 11.4F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(34, 0),
                Size = new Size(260, 30),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            });

            return panel;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.6F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static RoundedPanel CreateInputHost()
        {
            return new RoundedPanel
            {
                FillColor = Color.White,
                BorderColor = Color.FromArgb(205, 211, 220),
                CornerRadius = 8
            };
        }

        private static void ConfigureCheckBox(CheckBox checkBox, string text, bool isChecked)
        {
            checkBox.AutoSize = false;
            checkBox.BackColor = Color.White;
            checkBox.Checked = isChecked;
            checkBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            checkBox.ForeColor = UiTheme.Text;
            checkBox.Text = text;
            checkBox.TextAlign = ContentAlignment.MiddleLeft;
            checkBox.UseVisualStyleBackColor = false;
        }

        private void OpenConfigFolder()
        {
            var configFolder = Path.GetDirectoryName(AppConfig.GetConfigFilePath());
            if (string.IsNullOrWhiteSpace(configFolder))
            {
                return;
            }

            Directory.CreateDirectory(configFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = configFolder,
                UseShellExecute = true
            });
        }
    }

    internal sealed class SettingsToggleSwitch : Control
    {
        private bool _checked;
        private bool _hovered;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
            }
        }

        public SettingsToggleSwitch()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var backgroundBrush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

            var track = new Rectangle(1, 3, Width - 2, Height - 6);
            var trackColor = Checked
                ? (_hovered ? Color.FromArgb(30, 178, 86) : UiTheme.Success)
                : (_hovered ? Color.FromArgb(219, 224, 232) : Color.FromArgb(229, 233, 240));

            using var trackPath = UiDrawing.CreateRoundedRectangle(track, track.Height / 2);
            using var trackBrush = new SolidBrush(trackColor);
            e.Graphics.FillPath(trackBrush, trackPath);

            var knobSize = track.Height - 6;
            var knobX = Checked ? track.Right - knobSize - 3 : track.Left + 3;
            var knob = new Rectangle(knobX, track.Top + 3, knobSize, knobSize);
            using var knobBrush = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(knobBrush, knob);
        }
    }

    internal static class WindowsAutoStart
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PrintOrder Client";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
                return key?.GetValue(ValueName) is string value
                    && value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

            if (key == null)
            {
                throw new InvalidOperationException("Registry startup tidak bisa dibuka.");
            }

            if (enabled)
            {
                key.SetValue(ValueName, Quote(GetExecutablePath()), RegistryValueKind.String);
                return;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        private static string GetExecutablePath()
        {
            return Environment.ProcessPath ?? Application.ExecutablePath;
        }

        private static string Quote(string value)
        {
            return $"\"{value.Trim('"')}\"";
        }
    }
}
