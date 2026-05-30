using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintForm
{
    internal sealed class SettingsForm : Form
    {
        private readonly string _initialBaseUrl;
        private readonly NotificationOptions _initialNotificationOptions;

        private readonly TextBox _baseUrlTextBox = new TextBox();
        private readonly CheckBox _soundNotificationCheckBox = new CheckBox();
        private readonly CheckBox _desktopNotificationCheckBox = new CheckBox();
        private readonly Button _saveButton = new Button();
        private readonly Button _cancelButton = new Button();

        public string? SavedBaseUrl { get; private set; }
        public NotificationOptions? SavedNotificationOptions { get; private set; }
        public bool SavedChanges { get; private set; }
        public bool BaseUrlChanged { get; private set; }
        public bool NotificationOptionsChanged { get; private set; }

        public SettingsForm(string currentBaseUrl, NotificationOptions currentNotificationOptions)
        {
            _initialBaseUrl = (currentBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            _initialNotificationOptions = currentNotificationOptions?.Clone() ?? new NotificationOptions();

            InitializeLayout();
        }

        private void InitializeLayout()
        {
            Text = "Pengaturan";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 320);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var fileLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 16),
                Size = new Size(595, 22),
                ForeColor = UiTheme.MutedText,
                AutoEllipsis = true,
                Text = $"File: {AppConfig.GetConfigFilePath()}"
            };

            var serverSectionLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 52),
                Font = new Font(Font, FontStyle.Bold),
                Text = "Server"
            };

            var baseUrlLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 88),
                Text = "Base URL"
            };

            _baseUrlTextBox.Location = new Point(140, 84);
            _baseUrlTextBox.Size = new Size(470, 27);
            _baseUrlTextBox.Text = _initialBaseUrl;

            var hintLabel = new Label
            {
                AutoSize = true,
                Location = new Point(140, 116),
                ForeColor = UiTheme.MutedText,
                Text = "Contoh: http://127.0.0.1:3000"
            };

            var notificationSectionLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 156),
                Font = new Font(Font, FontStyle.Bold),
                Text = "Notifikasi"
            };

            _soundNotificationCheckBox.Location = new Point(140, 188);
            _soundNotificationCheckBox.Size = new Size(450, 24);
            _soundNotificationCheckBox.Text = "Suara Notifikasi";
            _soundNotificationCheckBox.Checked = _initialNotificationOptions.SoundEnabled;

            _desktopNotificationCheckBox.Location = new Point(140, 218);
            _desktopNotificationCheckBox.Size = new Size(450, 24);
            _desktopNotificationCheckBox.Text = "Notifikasi";
            _desktopNotificationCheckBox.Checked = _initialNotificationOptions.DesktopEnabled;

            var notificationHintLabel = new Label
            {
                AutoSize = false,
                Location = new Point(140, 248),
                Size = new Size(470, 22),
                ForeColor = UiTheme.MutedText,
                Text = "Notifikasi muncul selama 4 detik saat tugas cetak baru masuk."
            };

            _saveButton.Location = new Point(390, 280);
            _saveButton.Size = new Size(105, 32);
            _saveButton.Text = "Simpan";
            _saveButton.UseVisualStyleBackColor = true;
            _saveButton.Click += SaveButton_Click;

            _cancelButton.Location = new Point(505, 280);
            _cancelButton.Size = new Size(105, 32);
            _cancelButton.Text = "Batal";
            _cancelButton.UseVisualStyleBackColor = true;
            _cancelButton.DialogResult = DialogResult.Cancel;

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;

            Controls.Add(fileLabel);
            Controls.Add(serverSectionLabel);
            Controls.Add(baseUrlLabel);
            Controls.Add(_baseUrlTextBox);
            Controls.Add(hintLabel);
            Controls.Add(notificationSectionLabel);
            Controls.Add(_soundNotificationCheckBox);
            Controls.Add(_desktopNotificationCheckBox);
            Controls.Add(notificationHintLabel);
            Controls.Add(_saveButton);
            Controls.Add(_cancelButton);
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            var inputValue = (_baseUrlTextBox.Text ?? string.Empty).Trim().Trim('"').TrimEnd('/');
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

            if (!BaseUrlChanged && !NotificationOptionsChanged)
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
                AppConfig.SaveAppSettings(inputValue, newNotificationOptions);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    this,
                    $"Tidak punya izin menulis printform.ini di {AppConfig.GetConfigFilePath()}.",
                    "Gagal Menyimpan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Gagal menyimpan file printform.ini: {ex.Message}",
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
    }
}