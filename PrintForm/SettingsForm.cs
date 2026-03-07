using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintForm
{
    internal sealed class SettingsForm : Form
    {
        private readonly string _initialBaseUrl;
        private readonly TextBox _baseUrlTextBox = new TextBox();
        private readonly Button _saveButton = new Button();
        private readonly Button _cancelButton = new Button();

        public string? SavedBaseUrl { get; private set; }

        public SettingsForm(string currentBaseUrl)
        {
            _initialBaseUrl = currentBaseUrl;
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            Text = "Pengaturan";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 225);

            var fileLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 16),
                Text = $"File: {AppConfig.GetConfigFilePath()}"
            };

            var sectionLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 48),
                Font = new Font(Font, FontStyle.Bold),
                Text = "[server]"
            };

            var baseUrlLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 82),
                Text = "base_url"
            };

            _baseUrlTextBox.Location = new Point(120, 78);
            _baseUrlTextBox.Size = new Size(490, 27);
            _baseUrlTextBox.Text = _initialBaseUrl;

            var hintLabel = new Label
            {
                AutoSize = true,
                Location = new Point(120, 112),
                Text = "Contoh: http://127.0.0.1:3000"
            };

            _saveButton.Location = new Point(390, 165);
            _saveButton.Size = new Size(105, 32);
            _saveButton.Text = "Simpan";
            _saveButton.UseVisualStyleBackColor = true;
            _saveButton.Click += SaveButton_Click;

            _cancelButton.Location = new Point(505, 165);
            _cancelButton.Size = new Size(105, 32);
            _cancelButton.Text = "Batal";
            _cancelButton.UseVisualStyleBackColor = true;
            _cancelButton.DialogResult = DialogResult.Cancel;

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;

            Controls.Add(fileLabel);
            Controls.Add(sectionLabel);
            Controls.Add(baseUrlLabel);
            Controls.Add(_baseUrlTextBox);
            Controls.Add(hintLabel);
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
                    "Nilai base_url tidak valid. Gunakan URL HTTP/HTTPS yang lengkap.",
                    "Validasi Gagal",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _baseUrlTextBox.Focus();
                return;
            }

            if (string.Equals(inputValue, _initialBaseUrl, StringComparison.OrdinalIgnoreCase))
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

            if (!Program.TrySaveServerBaseUrlWithElevation(inputValue, out var errorMessage))
            {
                MessageBox.Show(
                    this,
                    errorMessage,
                    "Gagal Menyimpan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            SavedBaseUrl = inputValue;
            MessageBox.Show(
                this,
                "Berhasil menyimpan printform.ini dengan perubahan:\n- base_url",
                "Pengaturan Tersimpan",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
