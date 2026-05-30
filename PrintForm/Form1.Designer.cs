#nullable enable

namespace PrintForm
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();

                if (_logoImage != null)
                {
                    _logoImage.Dispose();
                    _logoImage = null;
                }
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private Image? _logoImage;

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            comboPrinters = new ComboBox();
            btnJobList = new RoundedButton();
            btnSettings = new RoundedButton();
            btnLogin = new RoundedButton();

            labelServerUrl = new Label();
            labelClientId = new Label();
            labelAuthUser = new Label();
            labelServerState = new Label();
            labelPrinterState = new Label();
            alertPairing = new RoundedPanel();

            dashboardHeaderContent = new Panel();
            dashboardContent = new Panel();

            statusStrip1 = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();

            printDocument1 = new System.Drawing.Printing.PrintDocument();
            pageSetupDialog1 = new PageSetupDialog();

            statusStrip1.SuspendLayout();
            SuspendLayout();

            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiTheme.PageBackground;
            ClientSize = new Size(1180, 820);
            MinimumSize = new Size(940, 700);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PrintForm Client";
            Load += Form1_Load;
            Resize += (_, _) => CenterDashboardShells();

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.PageBackground
            };

            // 
            // Header
            // 
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1180, 160),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White
            };

            dashboardHeaderContent.Location = new Point(0, 0);
            dashboardHeaderContent.Size = new Size(980, 160);
            dashboardHeaderContent.BackColor = Color.White;

            var separator = new Panel
            {
                Location = new Point(0, 159),
                Size = new Size(1180, 1),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = UiTheme.Border
            };

            var logoBox = new PictureBox
            {
                Location = new Point(0, 34),
                Size = new Size(76, 76),
                SizeMode = PictureBoxSizeMode.Zoom
            };

            _logoImage = TryLoadLogoImage();
            if (_logoImage != null)
            {
                logoBox.Image = _logoImage;
            }

            var titleLabel = new Label
            {
                AutoSize = false,
                Location = new Point(112, 39),
                Size = new Size(520, 42),
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "PrintForm Client"
            };

            var subtitleLabel = new Label
            {
                AutoSize = false,
                Location = new Point(115, 88),
                Size = new Size(610, 30),
                Font = new Font("Segoe UI", 12.5F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Penghubung printer lokal dengan PrintForm"
            };

            dashboardHeaderContent.Controls.Add(logoBox);
            dashboardHeaderContent.Controls.Add(titleLabel);
            dashboardHeaderContent.Controls.Add(subtitleLabel);

            headerPanel.Controls.Add(dashboardHeaderContent);
            headerPanel.Controls.Add(separator);

            // 
            // Content shell
            // 
            var contentPanel = new Panel
            {
                Location = new Point(0, 160),
                Size = new Size(1180, 620),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = UiTheme.PageBackground
            };

            dashboardContent.Location = new Point(0, 0);
            dashboardContent.Size = new Size(980, 600);
            dashboardContent.BackColor = UiTheme.PageBackground;

            // 
            // Ringkasan Status title
            // 
            var statusIcon = new Label
            {
                AutoSize = false,
                Location = new Point(0, 16),
                Size = new Size(34, 30),
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = UiTheme.Accent,
                Text = "▮",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var statusTitle = new Label
            {
                AutoSize = false,
                Location = new Point(40, 19),
                Size = new Size(300, 30),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Ringkasan Status"
            };

            dashboardContent.Controls.Add(statusIcon);
            dashboardContent.Controls.Add(statusTitle);

            // 
            // Status cards
            // 
            var serverCard = CreateStatusCard(
                "Status Server",
                "▤",
                UiTheme.Success,
                UiTheme.SuccessSoft,
                new Point(0, 52),
                out labelServerState);

            labelServerState.Text = "● Menghubungkan...";
            labelServerState.ForeColor = UiTheme.MutedText;

            var accountCard = CreateStatusCard(
                "Pairing Akun",
                "♙",
                UiTheme.Accent,
                UiTheme.AccentSoft,
                new Point(500, 52),
                out labelAuthUser);

            labelAuthUser.Text = "Belum terhubung";
            labelAuthUser.ForeColor = UiTheme.Accent;

            var clientCard = CreateStatusCard(
                "Client ID",
                "▣",
                UiTheme.Accent,
                UiTheme.AccentSoft,
                new Point(0, 154),
                out labelClientId);

            labelClientId.Text = "-";
            labelClientId.Font = new Font("Segoe UI", 11F, FontStyle.Regular);

            var printerStatusCard = CreateStatusCard(
                "Status Printer",
                "▥",
                UiTheme.Success,
                UiTheme.SuccessSoft,
                new Point(500, 154),
                out labelPrinterState);

            labelPrinterState.Text = "● Siap";
            labelPrinterState.ForeColor = UiTheme.Success;

            dashboardContent.Controls.Add(serverCard);
            dashboardContent.Controls.Add(accountCard);
            dashboardContent.Controls.Add(clientCard);
            dashboardContent.Controls.Add(printerStatusCard);

            // 
            // Printer Aktif card
            // 
            var printerCard = new RoundedPanel
            {
                Location = new Point(0, 260),
                Size = new Size(980, 160),
                CornerRadius = 10,
                FillColor = Color.White,
                BorderColor = UiTheme.Border
            };

            var printerIcon = new Label
            {
                AutoSize = false,
                Location = new Point(22, 19),
                Size = new Size(30, 30),
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = UiTheme.Accent,
                Text = "▥",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var printerTitle = new Label
            {
                AutoSize = false,
                Location = new Point(60, 20),
                Size = new Size(260, 30),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Printer Aktif"
            };

            comboPrinters.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPrinters.FormattingEnabled = true;
            comboPrinters.Location = new Point(22, 58);
            comboPrinters.Name = "comboPrinters";
            comboPrinters.Size = new Size(470, 31);
            comboPrinters.Font = new Font("Segoe UI", 11.5F, FontStyle.Regular);
            comboPrinters.TabIndex = 1;

            var printerInfo = new Label
            {
                AutoSize = false,
                Location = new Point(22, 103),
                Size = new Size(920, 26),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Dokumen dicetak menyesuaikan area cetak printer terpilih agar konten tidak terpotong."
            };

            alertPairing.Location = new Point(22, 128);
            alertPairing.Size = new Size(936, 34);
            alertPairing.CornerRadius = 8;
            alertPairing.FillColor = Color.FromArgb(255, 247, 244);
            alertPairing.BorderColor = Color.FromArgb(255, 211, 198);
            alertPairing.Visible = false;

            var alertText = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 0, 0, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "ⓘ  Hubungkan client dengan akun PrintForm untuk mulai menerima tugas cetak."
            };

            alertPairing.Controls.Add(alertText);

            printerCard.Controls.Add(printerIcon);
            printerCard.Controls.Add(printerTitle);
            printerCard.Controls.Add(comboPrinters);
            printerCard.Controls.Add(printerInfo);
            printerCard.Controls.Add(alertPairing);

            dashboardContent.Controls.Add(printerCard);

            // 
            // Aksi Cepat title
            // 
            var actionIcon = new Label
            {
                AutoSize = false,
                Location = new Point(0, 438),
                Size = new Size(34, 30),
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = UiTheme.Accent,
                Text = "↯",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var actionTitle = new Label
            {
                AutoSize = false,
                Location = new Point(40, 441),
                Size = new Size(300, 30),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Aksi Cepat"
            };

            dashboardContent.Controls.Add(actionIcon);
            dashboardContent.Controls.Add(actionTitle);

            // 
            // Buttons
            // 
            btnJobList.Location = new Point(0, 478);
            btnJobList.Name = "btnJobList";
            btnJobList.Size = new Size(360, 66);
            btnJobList.TabIndex = 2;
            btnJobList.Text = "▤  Daftar Tugas Cetak";
            btnJobList.UseAccentFill = true;
            btnJobList.Click += btnJobList_Click;

            btnSettings.Location = new Point(380, 478);
            btnSettings.Name = "btnSettings";
            btnSettings.Size = new Size(290, 66);
            btnSettings.TabIndex = 3;
            btnSettings.Text = "⚙  Pengaturan";
            btnSettings.UseAccentFill = false;
            btnSettings.Click += btnSettings_Click;

            btnLogin.Location = new Point(690, 478);
            btnLogin.Name = "btnLogin";
            btnLogin.Size = new Size(290, 66);
            btnLogin.TabIndex = 4;
            btnLogin.Text = "♙  Pair Akun";
            btnLogin.UseAccentFill = true;
            btnLogin.Click += btnLogin_Click;

            dashboardContent.Controls.Add(btnJobList);
            dashboardContent.Controls.Add(btnSettings);
            dashboardContent.Controls.Add(btnLogin);

            contentPanel.Controls.Add(dashboardContent);

            // 
            // Hidden compatibility label
            // 
            labelServerUrl.Visible = false;
            labelServerUrl.Text = "Server: -";

            mainPanel.Controls.Add(headerPanel);
            mainPanel.Controls.Add(contentPanel);
            mainPanel.Controls.Add(labelServerUrl);

            // 
            // statusStrip1
            // 
            statusStrip1.BackColor = Color.White;
            statusStrip1.Dock = DockStyle.Bottom;
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { statusLabel });
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(32, 4, 0, 4);
            statusStrip1.Size = new Size(1180, 32);
            statusStrip1.SizingGrip = false;
            statusStrip1.TabIndex = 5;
            statusStrip1.Text = "statusStrip1";

            // 
            // statusLabel
            // 
            statusLabel.Name = "statusLabel";
            statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
            statusLabel.ForeColor = UiTheme.MutedText;
            statusLabel.Text = "Belum terhubung dengan akun";

            // 
            // printDocument1
            // 
            printDocument1.BeginPrint += printDocument1_BeginPrint;
            printDocument1.EndPrint += printDocument1_EndPrint;
            printDocument1.PrintPage += printDocument1_PrintPage;

            Controls.Add(mainPanel);
            Controls.Add(statusStrip1);

            CenterDashboardShells();

            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private static RoundedPanel CreateStatusCard(
            string title,
            string icon,
            Color iconColor,
            Color iconBackground,
            Point location,
            out Label valueLabel)
        {
            var card = new RoundedPanel
            {
                Location = location,
                Size = new Size(480, 92),
                CornerRadius = 10,
                FillColor = Color.White,
                BorderColor = UiTheme.Border
            };

            var iconCircle = new RoundedPanel
            {
                Location = new Point(20, 18),
                Size = new Size(56, 56),
                CornerRadius = 28,
                FillColor = iconBackground,
                BorderThickness = 0
            };

            var iconLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = iconColor,
                Text = icon,
                TextAlign = ContentAlignment.MiddleCenter
            };

            iconCircle.Controls.Add(iconLabel);

            var titleLabel = new Label
            {
                AutoSize = false,
                Location = new Point(102, 20),
                Size = new Size(330, 26),
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = title
            };

            valueLabel = new Label
            {
                AutoSize = false,
                Location = new Point(102, 49),
                Size = new Size(350, 28),
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                AutoEllipsis = true,
                Text = "-"
            };

            card.Controls.Add(iconCircle);
            card.Controls.Add(titleLabel);
            card.Controls.Add(valueLabel);

            return card;
        }

        private void CenterDashboardShells()
        {
            if (dashboardHeaderContent != null)
            {
                dashboardHeaderContent.Left = Math.Max(0, (ClientSize.Width - dashboardHeaderContent.Width) / 2);
            }

            if (dashboardContent != null)
            {
                dashboardContent.Left = Math.Max(0, (ClientSize.Width - dashboardContent.Width) / 2);
            }
        }

        private static Image? TryLoadLogoImage()
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo_printform.png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "logo_printform.png")
            };

            foreach (var path in candidates)
            {
                if (!System.IO.File.Exists(path))
                {
                    continue;
                }

                try
                {
                    using var stream = System.IO.File.OpenRead(path);
                    using var image = Image.FromStream(stream);
                    return new Bitmap(image);
                }
                catch
                {
                    // Logo optional. Jika gagal dibaca, aplikasi tetap jalan.
                }
            }

            return null;
        }

        #endregion

        private ComboBox comboPrinters;
        private RoundedButton btnJobList;
        private RoundedButton btnSettings;
        private RoundedButton btnLogin;

        private Panel dashboardHeaderContent;
        private Panel dashboardContent;

        private Label labelServerUrl;
        private Label labelClientId;
        private Label labelAuthUser;
        private Label labelServerState;
        private Label labelPrinterState;
        private RoundedPanel alertPairing;

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel statusLabel;

        private System.Drawing.Printing.PrintDocument printDocument1;
        private PageSetupDialog pageSetupDialog1;
    }
}