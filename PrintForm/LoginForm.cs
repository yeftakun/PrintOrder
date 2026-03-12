using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintForm
{
    internal sealed class LoginForm : Form
    {
        private readonly TextBox _identifierTextBox = new TextBox();
        private readonly TextBox _passwordTextBox = new TextBox();
        private readonly CheckBox _showPasswordCheckBox = new CheckBox();
        private readonly Button _loginButton = new Button();
        private readonly Button _cancelButton = new Button();

        public string Identifier => (_identifierTextBox.Text ?? string.Empty).Trim();
        public string Password => _passwordTextBox.Text ?? string.Empty;

        public LoginForm(string? lastIdentifier)
        {
            InitializeLayout(lastIdentifier);
        }

        private void InitializeLayout(string? lastIdentifier)
        {
            Text = "Pair Akun Mitra";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 230);

            var infoLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 16),
                Text = "Verifikasi akun untuk pairing client ke akun mitra."
            };

            var identifierLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 56),
                Text = "Identifier"
            };

            _identifierTextBox.Location = new Point(120, 52);
            _identifierTextBox.Size = new Size(320, 27);
            _identifierTextBox.Text = string.IsNullOrWhiteSpace(lastIdentifier) ? string.Empty : lastIdentifier.Trim();

            var passwordLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 95),
                Text = "Password"
            };

            _passwordTextBox.Location = new Point(120, 91);
            _passwordTextBox.Size = new Size(320, 27);
            _passwordTextBox.PasswordChar = '*';

            _showPasswordCheckBox.AutoSize = true;
            _showPasswordCheckBox.Location = new Point(120, 126);
            _showPasswordCheckBox.Text = "Tampilkan password";
            _showPasswordCheckBox.CheckedChanged += (_, _) =>
            {
                _passwordTextBox.PasswordChar = _showPasswordCheckBox.Checked ? '\0' : '*';
            };

            _loginButton.Location = new Point(250, 172);
            _loginButton.Size = new Size(90, 32);
            _loginButton.Text = "Pair";
            _loginButton.UseVisualStyleBackColor = true;
            _loginButton.Click += LoginButton_Click;

            _cancelButton.Location = new Point(350, 172);
            _cancelButton.Size = new Size(90, 32);
            _cancelButton.Text = "Batal";
            _cancelButton.UseVisualStyleBackColor = true;
            _cancelButton.DialogResult = DialogResult.Cancel;

            AcceptButton = _loginButton;
            CancelButton = _cancelButton;

            Controls.Add(infoLabel);
            Controls.Add(identifierLabel);
            Controls.Add(_identifierTextBox);
            Controls.Add(passwordLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(_showPasswordCheckBox);
            Controls.Add(_loginButton);
            Controls.Add(_cancelButton);

            Shown += (_, _) =>
            {
                if (_identifierTextBox.TextLength == 0)
                {
                    _identifierTextBox.Focus();
                }
                else
                {
                    _passwordTextBox.Focus();
                }
            };
        }

        private void LoginButton_Click(object? sender, EventArgs e)
        {
            if (Identifier.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Identifier wajib diisi.",
                    "Validasi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _identifierTextBox.Focus();
                return;
            }

            if (Password.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Password wajib diisi.",
                    "Validasi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _passwordTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
