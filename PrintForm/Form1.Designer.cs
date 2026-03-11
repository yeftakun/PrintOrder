namespace PrintForm
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelPrinter = new Label();
            comboPrinters = new ComboBox();
            btnConfig = new Button();
            btnJobList = new Button();
            btnSettings = new Button();
            btnLogin = new Button();
            labelServerUrl = new Label();
            labelClientId = new Label();
            labelAuthUser = new Label();
            statusStrip1 = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            printDocument1 = new System.Drawing.Printing.PrintDocument();
            pageSetupDialog1 = new PageSetupDialog();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // labelPrinter
            // 
            labelPrinter.AutoSize = true;
            labelPrinter.Location = new Point(20, 22);
            labelPrinter.Name = "labelPrinter";
            labelPrinter.Size = new Size(55, 20);
            labelPrinter.TabIndex = 0;
            labelPrinter.Text = "Printer:";
            // 
            // comboPrinters
            // 
            comboPrinters.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPrinters.FormattingEnabled = true;
            comboPrinters.Location = new Point(120, 18);
            comboPrinters.Name = "comboPrinters";
            comboPrinters.Size = new Size(260, 28);
            comboPrinters.TabIndex = 1;
            // 
            // btnConfig
            // 
            btnConfig.Location = new Point(120, 60);
            btnConfig.Name = "btnConfig";
            btnConfig.Size = new Size(140, 29);
            btnConfig.TabIndex = 2;
            btnConfig.Text = "Konfigurasi Cetak";
            btnConfig.UseVisualStyleBackColor = true;
            btnConfig.Click += btnConfig_Click;
            // 
            // btnJobList
            // 
            btnJobList.Location = new Point(120, 100);
            btnJobList.Name = "btnJobList";
            btnJobList.Size = new Size(140, 29);
            btnJobList.TabIndex = 3;
            btnJobList.Text = "Print Job";
            btnJobList.UseVisualStyleBackColor = true;
            btnJobList.Click += btnJobList_Click;
            // 
            // btnSettings
            // 
            btnSettings.Location = new Point(270, 60);
            btnSettings.Name = "btnSettings";
            btnSettings.Size = new Size(140, 29);
            btnSettings.TabIndex = 4;
            btnSettings.Text = "Pengaturan";
            btnSettings.UseVisualStyleBackColor = true;
            btnSettings.Click += btnSettings_Click;
            // 
            // btnLogin
            // 
            btnLogin.Location = new Point(270, 100);
            btnLogin.Name = "btnLogin";
            btnLogin.Size = new Size(140, 29);
            btnLogin.TabIndex = 5;
            btnLogin.Text = "Login";
            btnLogin.UseVisualStyleBackColor = true;
            btnLogin.Click += btnLogin_Click;
            // 
            // labelServerUrl
            // 
            labelServerUrl.AutoEllipsis = true;
            labelServerUrl.Location = new Point(20, 138);
            labelServerUrl.Name = "labelServerUrl";
            labelServerUrl.Size = new Size(530, 20);
            labelServerUrl.TabIndex = 6;
            labelServerUrl.Text = "Server: -";
            // 
            // labelClientId
            // 
            labelClientId.AutoEllipsis = true;
            labelClientId.Location = new Point(20, 160);
            labelClientId.Name = "labelClientId";
            labelClientId.Size = new Size(530, 20);
            labelClientId.TabIndex = 7;
            labelClientId.Text = "Client ID: -";
            // 
            // labelAuthUser
            // 
            labelAuthUser.AutoEllipsis = true;
            labelAuthUser.Location = new Point(20, 182);
            labelAuthUser.Name = "labelAuthUser";
            labelAuthUser.Size = new Size(530, 20);
            labelAuthUser.TabIndex = 8;
            labelAuthUser.Text = "Akun: belum login";
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { statusLabel });
            statusStrip1.Location = new Point(0, 226);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(560, 26);
            statusStrip1.TabIndex = 5;
            statusStrip1.Text = "statusStrip1";
            // 
            // statusLabel
            // 
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(38, 20);
            statusLabel.Text = "Siap";
            // 
            // printDocument1
            // 
            printDocument1.BeginPrint += printDocument1_BeginPrint;
            printDocument1.EndPrint += printDocument1_EndPrint;
            printDocument1.PrintPage += printDocument1_PrintPage;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 252);
            Controls.Add(labelAuthUser);
            Controls.Add(labelClientId);
            Controls.Add(labelServerUrl);
            Controls.Add(statusStrip1);
            Controls.Add(btnLogin);
            Controls.Add(btnSettings);
            Controls.Add(btnJobList);
            Controls.Add(btnConfig);
            Controls.Add(comboPrinters);
            Controls.Add(labelPrinter);
            Name = "Form1";
            Text = "PrintForm Client";
            Load += Form1_Load;
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelPrinter;
        private ComboBox comboPrinters;
        private Button btnConfig;
        private Button btnJobList;
        private Button btnSettings;
        private Button btnLogin;
        private Label labelServerUrl;
        private Label labelClientId;
        private Label labelAuthUser;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel statusLabel;
        private System.Drawing.Printing.PrintDocument printDocument1;
        private PageSetupDialog pageSetupDialog1;
    }
}
