using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintOrder
{
    public partial class Form1 : Form
    {
        // Menyimpan gambar yang akan di-print via PrintDocument
        private Image? _imageToPrint;
        private int _activeContentScale = 100;
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly string _serverBaseUrl = AppConfig.LoadServerBaseUrl();
        private readonly SemaphoreSlim _authRefreshLock = new SemaphoreSlim(1, 1);
        private string? _clientId = AppConfig.LoadOrCreateClientId();
        private string? _accessToken;
        private string? _refreshToken;
        private string? _authUserId;
        private string? _authUsername;
        private System.Windows.Forms.Timer? _heartbeatTimer;
        private System.Windows.Forms.Timer? _pingTimer;
        private bool _registerInProgress;
        private bool _jobProcessing;
        private string? _activeJobId;
        private string? _activeJobTempPath;
        private JobListForm? _jobListForm;
        private ClientWebSocket? _realtimeSocket;
        private CancellationTokenSource? _realtimeCts;
        private Task? _realtimeWorker;

        private readonly object _jobNotificationLock = new object();
        private readonly HashSet<string> _notifiedRealtimeJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private NotificationOptions _notificationOptions = AppConfig.LoadNotificationOptions();
        private SoundPlayer? _incomingJobSoundPlayer;
        private bool _incomingJobSoundPlayerInitialized;

        private bool HasSavedAuthState => !string.IsNullOrWhiteSpace(_refreshToken);
        private bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

        public Form1()
        {
            InitializeComponent();

            HideFooterStatus();
            BindDashboardStatusToHiddenFooter();

            FormClosing += Form1_FormClosing;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        // =========================
        // EVENT FORM LOAD
        // =========================
        private async void Form1_Load(object sender, EventArgs e)
        {
            // Isi comboPrinters dengan printer yang terpasang di Windows
            comboPrinters.Items.Clear();

            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                comboPrinters.Items.Add(printerName);
            }

            if (comboPrinters.Items.Count > 0)
            {
                var defaultSettings = new PrinterSettings();
                comboPrinters.SelectedItem = defaultSettings.PrinterName;
            }

            comboPrinters.SelectedIndexChanged += comboPrinters_SelectedIndexChanged;

            labelServerUrl.Text = $"Server: {_serverBaseUrl}";
            LoadPersistedAuthState();
            UpdateClientIdLabel();
            UpdateAuthUi();
            RefreshDashboardState();
            statusLabel.Text = "Mencoba terhubung ke server...";

            if (HasSavedAuthState)
            {
                statusLabel.Text = "Memulihkan sesi pairing...";
                await TryRefreshAccessTokenAsync(updateStatusOnFailure: false);
            }

            await EnsureRegisteredAsync();
            StartHeartbeat();
            StartPingPolling();
            StartRealtime();
        }

        // =========================
        // KONFIGURASI HALAMAN (H/P, UKURAN KERTAS, MARGIN)
        // =========================
        private void btnConfig_Click(object sender, EventArgs e)
        {
            // Pastikan printer di-set ke printer yang dipilih
            if (comboPrinters.SelectedItem is string selectedPrinter)
            {
                printDocument1.PrinterSettings.PrinterName = selectedPrinter;
            }

            pageSetupDialog1.Document = printDocument1;

            var result = pageSetupDialog1.ShowDialog();

            if (result == DialogResult.OK)
            {
                statusLabel.Text = "Konfigurasi halaman diperbarui.";
            }
            else
            {
                statusLabel.Text = "Konfigurasi halaman dibatalkan.";
            }
        }

        private void btnJobList_Click(object sender, EventArgs e)
        {
            if (!HasSavedAuthState)
            {
                statusLabel.Text = "Hubungkan client dengan akun PrintOrder terlebih dahulu.";
                return;
            }

            if (_jobListForm == null || _jobListForm.IsDisposed)
            {
                _jobListForm = new JobListForm(
                    _serverBaseUrl,
                    () => _clientId,
                    SendAuthorizedAsync,
                    PrintJobFromListAsync,
                    RejectJobFromListAsync);
            }

            _jobListForm.Show();
            _jobListForm.BringToFront();
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using var settingsForm = new SettingsForm(_serverBaseUrl, _notificationOptions);
            var result = settingsForm.ShowDialog(this);

            if (result != DialogResult.OK || !settingsForm.SavedChanges)
            {
                statusLabel.Text = "Pengaturan tidak diubah.";
                return;
            }

            if (settingsForm.SavedNotificationOptions != null)
            {
                _notificationOptions = settingsForm.SavedNotificationOptions;
            }
            else
            {
                _notificationOptions = AppConfig.LoadNotificationOptions();
            }

            if (settingsForm.BaseUrlChanged)
            {
                statusLabel.Text = "Pengaturan disimpan. Restart aplikasi untuk menerapkan Base URL baru.";
                MessageBox.Show(
                    "Perubahan tersimpan ke printorder.ini.\nRestart aplikasi agar koneksi memakai Base URL terbaru.",
                    "Pengaturan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            statusLabel.Text = settingsForm.AutoStartChanged && !settingsForm.NotificationOptionsChanged
                ? "Pengaturan auto start disimpan."
                : "Pengaturan disimpan.";
        }

        private void btnInfo_Click(object? sender, EventArgs e)
        {
            using var aboutForm = new AboutForm(_clientId, _serverBaseUrl);
            aboutForm.ShowDialog(this);
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            if (HasSavedAuthState)
            {
                await UnpairClientAsync();
                return;
            }

            using var loginForm = new LoginForm(_authUsername);
            var loginResult = loginForm.ShowPairingDialog(this);
            if (loginResult != DialogResult.OK)
            {
                return;
            }

            await LoginAsync(loginForm.Identifier, loginForm.Password);
        }

        private void comboPrinters_SelectedIndexChanged(object? sender, EventArgs e)
        {
            RefreshDashboardState();
            _ = SendHeartbeatAsync();
        }

        // =========================
        // EVENT PRINTDOCUMENT (PRINT)
        // =========================
        private void printDocument1_PrintPage(object sender, PrintPageEventArgs e)
        {
            var graphics = e.Graphics;
            if (graphics == null)
            {
                e.HasMorePages = false;
                return;
            }

            if (_imageToPrint == null)
            {
                 // Tidak ada gambar, skip
                e.HasMorePages = false;
                return;
            }

            // Gunakan PageBounds (Ukuran Fisik Kertas)
            // Karena kita set Margins(0,0,0,0), MarginBounds == PageBounds secara logika size, 
            // tapi kita ingin menghitung posisi relatif terhadap fisik kertas.
            Rectangle p = e.PageBounds;

            // Hitung Best Fit Base (100%) -> Fit to Paper
            float imgRatio = (float)_imageToPrint.Width / _imageToPrint.Height;
            float pageRatio = (float)p.Width / p.Height;

            int baseWidth, baseHeight;

            if (imgRatio > pageRatio)
            {
                // Gambar lebih lebar (relative to page) -> Fit Width
                baseWidth = p.Width;
                baseHeight = (int)(p.Width / imgRatio);
            }
            else
            {
                // Gambar lebih tinggi -> Fit Height
                baseHeight = p.Height;
                baseWidth = (int)(p.Height * imgRatio);
            }

            // Terapkan Scaling dari Konfigurasi
            float scaleFactor = _activeContentScale / 100.0f;
            int drawWidth = (int)(baseWidth * scaleFactor);
            int drawHeight = (int)(baseHeight * scaleFactor);

            // Centering di Fisik Kertas
            // Koordinat (0,0) PageBounds adalah pojok kiri atas kertas fisik.
            int physX = (p.Width - drawWidth) / 2;
            int physY = (p.Height - drawHeight) / 2;

            // Adjust ke Koordinat Graphics
            // Graphics Origin (0,0) biasanya dimulai dari HardMarginTopLeft.
            // Jadi untuk menggambar di (physX, physY) kertas fisik, kita harus kurangi HardMargin.
            float hardX = e.PageSettings.HardMarginX;
            float hardY = e.PageSettings.HardMarginY;

            // Gambar
            graphics.DrawImage(_imageToPrint, new Rectangle(physX - (int)hardX, physY - (int)hardY, drawWidth, drawHeight));

            e.HasMorePages = false;
        }

        private void printDocument1_BeginPrint(object sender, PrintEventArgs e)
        {
            statusLabel.Text = "Proses print dimulai...";
        }

        private void printDocument1_EndPrint(object sender, PrintEventArgs e)
        {
            var isAutoJob = !string.IsNullOrWhiteSpace(_activeJobId);
            if (isAutoJob)
            {
                var jobId = _activeJobId;
                _activeJobId = null;
                _jobProcessing = false;
                if (!string.IsNullOrWhiteSpace(_activeJobTempPath))
                {
                    TryDeleteTempFile(_activeJobTempPath);
                    _activeJobTempPath = null;
                }
                if (!string.IsNullOrWhiteSpace(jobId))
                {
                    _ = UpdateJobStatusAsync(jobId, e.PrintAction == PrintAction.PrintToPrinter ? "done" : "failed");
                }
            }

            if (!isAutoJob && e.PrintAction == PrintAction.PrintToPrinter)
            {
                MessageBox.Show("Dokumen berhasil dikirim ke printer.",
                                "Informasi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusLabel.Text = "Selesai mengirim ke printer.";
            }
            else if (!isAutoJob)
            {
                statusLabel.Text = "Print dibatalkan atau tidak dikirim ke printer.";
            }
        }

        // =========================
        // STUB EVENT LAMA (JIKA MASIH TERIKAT DI DESIGNER)
        // =========================
        private void label1_Click(object sender, EventArgs e)
        {
            // Dibiarkan kosong; hanya agar designer tidak error
        }

        private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            // Dibiarkan kosong; hanya agar designer tidak error
        }

        private void LoadPersistedAuthState()
        {
            var state = AppConfig.LoadAuthState();
            if (state == null)
            {
                return;
            }

            _refreshToken = state.RefreshToken;
            _authUserId = state.UserId;
            _authUsername = state.Username;
        }

        private void PersistAuthState()
        {
            var refreshToken = (_refreshToken ?? string.Empty).Trim();
            if (refreshToken.Length == 0)
            {
                AppConfig.ClearAuthState();
                return;
            }

            try
            {
                AppConfig.SaveAuthState(new AuthState
                {
                    RefreshToken = refreshToken,
                    UserId = _authUserId,
                    Username = _authUsername
                });
            }
            catch
            {
                // Abaikan kegagalan persist state auth agar app tetap berjalan.
            }
        }

        private void ClearAuthState()
        {
            _accessToken = null;
            _refreshToken = null;
            _authUserId = null;
            _authUsername = null;
            AppConfig.ClearAuthState();
            UpdateAuthUi();
        }

        private void UpdateAuthUi()
        {
            var isPaired = HasSavedAuthState;
            var displayName = string.IsNullOrWhiteSpace(_authUsername) ? _authUserId : _authUsername;

            labelAuthUser.Text = isPaired
                ? displayName ?? "Terhubung"
                : "Belum terhubung";

            labelAuthUser.ForeColor = isPaired
                ? UiTheme.MutedText
                : UiTheme.Accent;

            btnLogin.Text = isPaired
                ? "Lepas Pairing"
                : "Pair Akun";

            btnLogin.IconKind = isPaired
                ? IconKind.LinkOff
                : IconKind.Account;

            btnLogin.UseAccentFill = !isPaired;

            btnJobList.Enabled = isPaired;
            btnJobList.UseAccentFill = isPaired;

            alertPairing.Visible = !isPaired;

            RefreshDashboardState();
        }

        private void HideFooterStatus()
        {
            if (statusStrip1 == null || statusStrip1.IsDisposed)
            {
                return;
            }

            statusStrip1.Visible = false;
            statusStrip1.Height = 0;
        }

        private void BindDashboardStatusToHiddenFooter()
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.TextChanged += (_, _) =>
            {
                SetDashboardStatusFromMessage(statusLabel.Text);
            };

            SetDashboardStatusFromMessage(statusLabel.Text);
        }

        private void SetDashboardStatusFromMessage(string? message)
        {
            var text = (message ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return;
            }

            if (labelServerState == null || labelServerState.IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetDashboardStatusFromMessage(text)));
                return;
            }

            labelServerState.Text = text;
            labelServerState.ForeColor = ResolveDashboardStatusColor(text);
        }

        private static Color ResolveDashboardStatusColor(string text)
        {
            if (ContainsAny(text,
                    "mencoba terhubung",
                    "memulihkan sesi",
                    "memverifikasi",
                    "memperbarui sesi",
                    "memproses"))
            {
                return UiTheme.MutedText;
            }

            if (ContainsAny(text,
                    "tidak bisa",
                    "gagal",
                    "terputus",
                    "offline",
                    "habis",
                    "wajib",
                    "dibatalkan",
                    "ditolak",
                    "tidak valid",
                    "tidak ditemukan"))
            {
                return UiTheme.Accent;
            }

            if (ContainsAny(text,
                    "berhasil",
                    "terhubung",
                    "siap",
                    "selesai",
                    "ping diterima",
                    "dikenali server"))
            {
                return UiTheme.Success;
            }

            return UiTheme.MutedText;
        }

        private static bool ContainsAny(string source, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshDashboardState()
        {
            var printerName = GetSelectedPrinterName();
            var hasPrinter = !string.IsNullOrWhiteSpace(printerName);

            labelPrinterState.Text = hasPrinter ? "● Siap" : "● Tidak tersedia";
            labelPrinterState.ForeColor = hasPrinter ? UiTheme.Success : UiTheme.Accent;

            if (!HasSavedAuthState)
            {
                btnJobList.Enabled = false;
                btnJobList.UseAccentFill = false;
                alertPairing.Visible = true;
            }
        }

        private async Task LoginAsync(string identifier, string password)
        {
            try
            {
                statusLabel.Text = "Memverifikasi akun untuk pairing...";

                await EnsureRegisteredAsync();
                if (string.IsNullOrWhiteSpace(_clientId))
                {
                    statusLabel.Text = "Client ID belum siap. Coba lagi.";
                    return;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    identifier,
                    password
                });

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(
                    $"{_serverBaseUrl}/api/clients/{Uri.EscapeDataString(_clientId)}/pair",
                    content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var apiError = TryExtractApiError(responseBody);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        statusLabel.Text = "Pairing gagal. Periksa identifier/password.";
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        statusLabel.Text = apiError ?? "Client ini sudah dimiliki akun lain.";
                    }
                    else
                    {
                        statusLabel.Text = apiError ?? $"Pairing gagal ({(int)response.StatusCode}).";
                    }
                    return;
                }

                if (!TryApplyAuthBundle(responseBody))
                {
                    statusLabel.Text = "Respons pairing tidak valid.";
                    return;
                }

                await SendHeartbeatAsync();
                statusLabel.Text = $"Pairing berhasil sebagai {_authUsername ?? identifier}. Client siap menerima job.";
            }
            catch
            {
                statusLabel.Text = "Tidak bisa melakukan pairing ke server.";
            }
        }

        private async Task UnpairClientAsync()
        {
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                ClearAuthState();
                statusLabel.Text = "Pairing lokal dilepas.";
                return;
            }

            var pin = PromptPinForUnpair();
            if (pin == null)
            {
                statusLabel.Text = "Lepas pairing dibatalkan.";
                return;
            }

            var pinVerified = await VerifyAccountPinAsync(pin);
            if (!pinVerified)
            {
                return;
            }

            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{_serverBaseUrl}/api/clients/{Uri.EscapeDataString(_clientId)}/unbind")
                {
                    Content = content
                };

                using var response = await SendAuthorizedAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var apiError = TryExtractApiError(responseBody);

                    if (response.StatusCode == HttpStatusCode.Conflict
                        && !string.IsNullOrWhiteSpace(apiError)
                        && apiError.Contains("already unbound", StringComparison.OrdinalIgnoreCase))
                    {
                        ClearAuthState();
                        await SendHeartbeatAsync();
                        statusLabel.Text = "Pairing lokal disinkronkan. Client memang sudah unbound di server.";
                        return;
                    }

                    statusLabel.Text = apiError ?? $"Gagal melepas pairing ({(int)response.StatusCode}).";
                    return;
                }

                ClearAuthState();
                await SendHeartbeatAsync();
                statusLabel.Text = "Pairing berhasil dilepas dari akun.";
            }
            catch
            {
                statusLabel.Text = "Tidak bisa menghubungi server untuk melepas pairing.";
            }
        }

        private async Task<bool> VerifyAccountPinAsync(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                statusLabel.Text = "PIN wajib diisi.";
                return false;
            }

            try
            {
                var payload = JsonSerializer.Serialize(new { pin });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_serverBaseUrl}/api/auth/verify-pin")
                {
                    Content = content
                };

                using var response = await SendAuthorizedAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var apiError = TryExtractApiError(responseBody);
                statusLabel.Text = apiError ?? $"Verifikasi PIN gagal ({(int)response.StatusCode}).";
                return false;
            }
            catch
            {
                statusLabel.Text = "Tidak bisa memverifikasi PIN akun.";
                return false;
            }
        }

        private string? PromptPinForUnpair()
        {
            using var dialog = new UnpairPinDialog();
            return dialog.ShowPairingDialog(this) == DialogResult.OK
                ? dialog.Pin
                : null;
        }

        private async Task<bool> TryRefreshAccessTokenAsync(bool updateStatusOnFailure)
        {
            var refreshToken = _refreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return false;
            }

            await _authRefreshLock.WaitAsync();
            try
            {
                refreshToken = _refreshToken;
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return false;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    refreshToken
                });

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync($"{_serverBaseUrl}/api/auth/refresh", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        ClearAuthState();
                        if (updateStatusOnFailure)
                        {
                            statusLabel.Text = "Sesi pairing berakhir. Silakan pair akun ulang.";
                        }
                    }
                    else if (updateStatusOnFailure)
                    {
                        var apiError = TryExtractApiError(responseBody);
                        statusLabel.Text = apiError ?? $"Gagal refresh sesi ({(int)response.StatusCode}).";
                    }

                    return false;
                }

                if (!TryApplyAuthBundle(responseBody))
                {
                    if (updateStatusOnFailure)
                    {
                        statusLabel.Text = "Respons refresh tidak valid.";
                    }
                    return false;
                }

                return true;
            }
            catch
            {
                if (updateStatusOnFailure)
                {
                    statusLabel.Text = "Tidak bisa memperbarui sesi pairing.";
                }
                return false;
            }
            finally
            {
                _authRefreshLock.Release();
            }
        }

        private bool TryApplyAuthBundle(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (!TryReadString(doc.RootElement, "accessToken", out var accessToken)
                    || !TryReadString(doc.RootElement, "refreshToken", out var refreshToken))
                {
                    return false;
                }

                string? userId = null;
                string? username = null;
                if (doc.RootElement.TryGetProperty("user", out var userElement)
                    && userElement.ValueKind == JsonValueKind.Object)
                {
                    if (TryReadString(userElement, "id", out var idValue))
                    {
                        userId = idValue;
                    }

                    if (TryReadString(userElement, "username", out var usernameValue))
                    {
                        username = usernameValue;
                    }
                }

                _accessToken = accessToken;
                _refreshToken = refreshToken;
                _authUserId = userId;
                _authUsername = username;
                PersistAuthState();
                UpdateAuthUi();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request)
        {
            if (string.IsNullOrWhiteSpace(_accessToken) && HasSavedAuthState)
            {
                await TryRefreshAccessTokenAsync(updateStatusOnFailure: false);
            }

            var retryRequest = await CloneRequestAsync(request);

            ApplyAuthorizationHeader(request);
            var response = await Http.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Unauthorized || !HasSavedAuthState)
            {
                retryRequest.Dispose();
                return response;
            }

            var refreshed = await TryRefreshAccessTokenAsync(updateStatusOnFailure: false);
            if (!refreshed || string.IsNullOrWhiteSpace(_accessToken))
            {
                retryRequest.Dispose();
                return response;
            }

            response.Dispose();
            ApplyAuthorizationHeader(retryRequest);
            return await Http.SendAsync(retryRequest);
        }

        private void ApplyAuthorizationHeader(HttpRequestMessage request)
        {
            request.Headers.Authorization = null;
            if (!string.IsNullOrWhiteSpace(_accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content != null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync();
                var clonedContent = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                {
                    clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                clone.Content = clonedContent;
            }

            return clone;
        }

        private static string? TryExtractApiError(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (!TryReadString(doc.RootElement, "error", out var errorText))
                {
                    return null;
                }

                return errorText;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadString(JsonElement element, string propertyName, out string value)
        {
            if (element.TryGetProperty(propertyName, out var found)
                && found.ValueKind == JsonValueKind.String)
            {
                var raw = found.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    value = raw.Trim();
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static bool TryReadBooleanFromJson(string json, string propertyName, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(propertyName, out var found)
                    || (found.ValueKind != JsonValueKind.True && found.ValueKind != JsonValueKind.False))
                {
                    return false;
                }

                value = found.GetBoolean();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async System.Threading.Tasks.Task RegisterClientAsync()
        {
            if (_registerInProgress)
            {
                return;
            }

            _registerInProgress = true;
            try
            {
                statusLabel.Text = "Mencoba terhubung ke server...";
                var printers = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                var payload = new
                {
                    clientId = _clientId,
                    name = Environment.MachineName,
                    printers,
                    selectedPrinter = GetSelectedPrinterName()
                };
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_serverBaseUrl}/api/clients/register")
                {
                    Content = content
                };
                using var response = await SendAuthorizedAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        statusLabel.Text = "Client dikenali server. Silakan pair akun untuk mengaktifkan client.";
                    }
                    else
                    {
                        var apiError = TryExtractApiError(responseBody);
                        statusLabel.Text = apiError ?? $"Gagal terhubung ke server ({(int)response.StatusCode}).";
                    }
                    return;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("id", out var id))
                {
                    var idValue = id.GetString();
                    if (Guid.TryParse(idValue, out var parsedGuid))
                    {
                        _clientId = parsedGuid.ToString("D");
                        UpdateClientIdLabel();
                    }
                }

                var recognized = doc.RootElement.TryGetProperty("recognized", out var recognizedElement)
                    && recognizedElement.ValueKind == JsonValueKind.True;

                if (!recognized && HasSavedAuthState)
                {
                    ClearAuthState();
                    statusLabel.Text = "Pairing akun telah dilepas dari server. Client kembali mode belum dipairing.";
                    return;
                }

                if (recognized && !IsAuthenticated)
                {
                    statusLabel.Text = "Client sudah bind akun, tetapi sesi pairing belum aktif.";
                }
                else
                {
                    statusLabel.Text = "Terhubung ke server.";
                }
            }
            catch
            {
                statusLabel.Text = "Tidak bisa terhubung ke server.";
            }
            finally
            {
                _registerInProgress = false;
            }
        }

        private void StartHeartbeat()
        {
            _heartbeatTimer = new System.Windows.Forms.Timer
            {
                Interval = 30000
            };
            _heartbeatTimer.Tick += async (_, _) => await SendHeartbeatAsync();
            _heartbeatTimer.Start();
        }

        private void StartPingPolling()
        {
            _pingTimer = new System.Windows.Forms.Timer
            {
                Interval = 5000
            };
            _pingTimer.Tick += async (_, _) => await PollPingAsync();
            _pingTimer.Start();
        }

        private async System.Threading.Tasks.Task SendHeartbeatAsync()
        {
            await EnsureRegisteredAsync();
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            try
            {
                var payload = new
                {
                    clientId = _clientId,
                    selectedPrinter = GetSelectedPrinterName()
                };
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_serverBaseUrl}/api/clients/heartbeat")
                {
                    Content = content
                };
                using var response = await SendAuthorizedAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await RegisterClientAsync();
                    return;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    statusLabel.Text = "Perlu pairing akun untuk mengaktifkan client ini.";
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (TryReadBooleanFromJson(responseBody, "recognized", out var recognized)
                        && !recognized
                        && HasSavedAuthState)
                    {
                        ClearAuthState();
                        statusLabel.Text = "Pairing akun telah dilepas dari browser/mitra. Pair ulang jika diperlukan.";
                    }
                    return;
                }

                statusLabel.Text = "Koneksi server terputus.";
            }
            catch
            {
                statusLabel.Text = "Koneksi server terputus.";
            }
        }

        private async System.Threading.Tasks.Task PollPingAsync()
        {
            await EnsureRegisteredAsync();
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_serverBaseUrl}/api/clients/{_clientId}/ping");
                using var response = await SendAuthorizedAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await RegisterClientAsync();
                    return;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    statusLabel.Text = "Perlu pairing akun untuk sinkronisasi ping.";
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                {
                    return;
                }

                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                {
                    return;
                }

                var count = items.GetArrayLength();
                statusLabel.Text = "Ping diterima dari server.";
                MessageBox.Show($"Ping diterima dari server ({count}).",
                                "Ping", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                // Abaikan jika server tidak bisa dihubungi
            }
        }

        private async System.Threading.Tasks.Task PrintJobFromListAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                statusLabel.Text = "Masih memproses job lain.";
                return;
            }

            await ProcessJobAsync(job);
        }

        private async System.Threading.Tasks.Task RejectJobFromListAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                statusLabel.Text = "Masih memproses job lain.";
                return;
            }

            await UpdateJobStatusAsync(job.Id, "rejected");
            statusLabel.Text = $"Job {job.Id} ditolak.";
        }

        private async System.Threading.Tasks.Task ProcessJobAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                return;
            }

            _jobProcessing = true;
            _activeJobId = job.Id;
            statusLabel.Text = $"Memproses job {job.Id}...";
            var waitForEndPrint = false;
            _ = SendHeartbeatAsync();

            try
            {
                var printerName = GetSelectedPrinterName();
                if (IsPrinterOffline(printerName, out var offlineReason))
                {
                    statusLabel.Text = offlineReason ?? "Printer sedang offline. Job ditunda.";
                    await UpdateJobStatusAsync(job.Id, "pending");
                    _jobProcessing = false;
                    _activeJobId = null;
                    return;
                }

                await UpdateJobStatusAsync(job.Id, "printing");

                var downloadPath = await DownloadJobFileAsync(job.Id, job.OriginalName);
                if (string.IsNullOrWhiteSpace(downloadPath))
                {
                    await UpdateJobStatusAsync(job.Id, "failed");
                    _jobProcessing = false;
                    _activeJobId = null;
                    return;
                }

                _activeJobTempPath = downloadPath;

                // Reset image cache
                _imageToPrint?.Dispose();
                _imageToPrint = null;

                string ext = Path.GetExtension(downloadPath).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
                {
                    using var img = Image.FromFile(downloadPath);
                    _imageToPrint = new Bitmap(img);
                }

                ApplyPrintConfig(job);

                if (_imageToPrint != null)
                {
                    waitForEndPrint = true;
                    printDocument1.Print();
                    return;
                }

                var printed = await PrintNonImageAsync(downloadPath, job.PrintConfig);
                TryDeleteTempFile(downloadPath);
                _activeJobTempPath = null;
                await UpdateJobStatusAsync(job.Id, printed ? "done" : "failed");
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(job.Id))
                {
                    await UpdateJobStatusAsync(job.Id, "failed");
                }
            }
            finally
            {
                if (!waitForEndPrint)
                {
                    _jobProcessing = false;
                    _activeJobId = null;
                    if (!string.IsNullOrWhiteSpace(_activeJobTempPath))
                    {
                        TryDeleteTempFile(_activeJobTempPath);
                        _activeJobTempPath = null;
                    }
                }
            }
        }

        private void ApplyPrintConfig(PrintJob job)
        {
            var printerName = comboPrinters.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                var defaultSettings = new PrinterSettings();
                printerName = defaultSettings.PrinterName;
            }

            printDocument1.PrinterSettings.PrinterName = printerName ?? string.Empty;
            if (!printDocument1.PrinterSettings.IsValid)
            {
                statusLabel.Text = "Printer tidak valid.";
                return;
            }

            // Set Margin 0 agar app tidak melakukan shrink software.
            // Driver printer tetap punya hard-margin sendiri.
            printDocument1.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            _activeContentScale = 100;

            if (job.PrintConfig != null)
            {
                var config = job.PrintConfig;
                _activeContentScale = config.ContentScale;

                if (config.Copies >= 1 && config.Copies <= 999)
                {
                    printDocument1.PrinterSettings.Copies = (short)config.Copies;
                }

                // Color Mode
                if (!string.IsNullOrWhiteSpace(config.ColorMode))
                {
                    printDocument1.DefaultPageSettings.Color = !string.Equals(config.ColorMode, "bw", StringComparison.OrdinalIgnoreCase);
                }

                // Orientation
                if (!string.IsNullOrWhiteSpace(config.Orientation))
                {
                    printDocument1.DefaultPageSettings.Landscape = string.Equals(config.Orientation, "landscape", StringComparison.OrdinalIgnoreCase);
                }

                // Paper Size
                if (!string.IsNullOrWhiteSpace(config.PaperSize))
                {
                    bool found = false;
                    foreach (PaperSize size in printDocument1.PrinterSettings.PaperSizes)
                    {
                        if (string.Equals(size.PaperName, config.PaperSize, StringComparison.OrdinalIgnoreCase))
                        {
                            printDocument1.DefaultPageSettings.PaperSize = size;
                            found = true;
                            break;
                        }
                    }

                    // Fallback: Jika F4 tidak ada, coba Legal
                    if (!found && string.Equals(config.PaperSize, "F4", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (PaperSize size in printDocument1.PrinterSettings.PaperSizes)
                        {
                            if (size.Kind == PaperKind.Legal || string.Equals(size.PaperName, "Legal", StringComparison.OrdinalIgnoreCase))
                            {
                                printDocument1.DefaultPageSettings.PaperSize = size;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task<string?> DownloadJobFileAsync(string jobId, string originalName)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}/download");
            using var response = await SendAuthorizedAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                statusLabel.Text = "Sesi pairing habis. Pair akun ulang sebelum mengambil file job.";
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var safeName = Path.GetFileName(originalName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = jobId;
            }

            var filePath = Path.Combine(Path.GetTempPath(), $"{jobId}_{safeName}");
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            return filePath;
        }

        private async System.Threading.Tasks.Task<bool> PrintNonImageAsync(string filePath, PrintConfig? printConfig)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                if (!TryNormalizePdfPageRange(printConfig?.PageRange, out var pageRange, out var pageRangeError))
                {
                    statusLabel.Text = pageRangeError;
                    return false;
                }

                if (await TryPrintPdfWithSumatraAsync(filePath, pageRange))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(pageRange))
                {
                    statusLabel.Text = "Gagal mencetak PDF sesuai rentang halaman. Install SumatraPDF agar rentang halaman didukung.";
                    return false;
                }

                if (await TryPrintPdfWithEdgeAsync(filePath, headless: true))
                {
                    return true;
                }

                if (await TryPrintPdfWithEdgeAsync(filePath, headless: false))
                {
                    return true;
                }

                statusLabel.Text = "Gagal mencetak PDF. Install SumatraPDF.";
                return false;
            }

            var printerName = comboPrinters.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                var defaultSettings = new PrinterSettings();
                printerName = defaultSettings.PrinterName;
            }

            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                Verb = "printto",
                Arguments = $"\"{printerName}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            };

            var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(10000);
            }

            await System.Threading.Tasks.Task.CompletedTask;
            return true;
        }

        private async System.Threading.Tasks.Task<bool> TryPrintPdfWithEdgeAsync(string filePath)
        {
            return await TryPrintPdfWithEdgeAsync(filePath, headless: true);
        }

        private async System.Threading.Tasks.Task<bool> TryPrintPdfWithEdgeAsync(string filePath, bool headless)
        {
            var printerName = GetSelectedPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return false;
            }

            var edgePath = ResolveEdgePath();
            if (string.IsNullOrWhiteSpace(edgePath))
            {
                return false;
            }

            try
            {
                var userDataDir = Path.Combine(Path.GetTempPath(), $"printorder-edge-{Guid.NewGuid():N}");
                Directory.CreateDirectory(userDataDir);
                var safePrinter = printerName.Replace("\"", "\\\"");
                var fileUrl = new Uri(filePath).AbsoluteUri;

                var headlessFlag = headless ? "--headless=new" : "";
                var appFlag = headless ? "" : $"--app=\"{fileUrl}\"";
                var target = headless ? $"\"{fileUrl}\"" : "";

                var psi = new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = $"{headlessFlag} --disable-gpu --no-first-run --disable-extensions --disable-print-preview --kiosk-printing --user-data-dir=\"{userDataDir}\" --print-to-printer=\"{safePrinter}\" {appFlag} {target}",
                    CreateNoWindow = headless,
                    WindowStyle = headless ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                };

                var p = Process.Start(psi);
                if (p == null)
                {
                    return false;
                }

                var timeoutMs = headless ? 20000 : 8000;
                var exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try
                    {
                        p.Kill(true);
                    }
                    catch
                    {
                        // Abaikan kegagalan kill
                    }
                    return false;
                }

                if (p.ExitCode != 0 && headless)
                {
                    return false;
                }

                await System.Threading.Tasks.Task.CompletedTask;
                TryDeleteTempDirectory(userDataDir);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsPrinterOffline(string? printerName, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(printerName))
            {
                reason = "Printer tidak ditemukan. Job ditunda.";
                return true;
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, WorkOffline, PrinterStatus FROM Win32_Printer");
                using var results = searcher.Get();
                foreach (ManagementObject printer in results)
                {
                    var name = printer["Name"]?.ToString();
                    if (!string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var workOffline = printer["WorkOffline"];
                    if (workOffline is bool isOffline && isOffline)
                    {
                        reason = "Printer sedang offline. Job ditunda.";
                        return true;
                    }

                    var statusObj = printer["PrinterStatus"];
                    if (statusObj != null && int.TryParse(statusObj.ToString(), out var statusCode))
                    {
                        if (statusCode == 7)
                        {
                            reason = "Printer sedang offline. Job ditunda.";
                            return true;
                        }
                    }

                    return false;
                }

                reason = "Printer tidak ditemukan. Job ditunda.";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async System.Threading.Tasks.Task<bool> TryPrintPdfWithSumatraAsync(string filePath, string? pageRange)
        {
            var printerName = GetSelectedPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return false;
            }

            var sumatraPath = SumatraPdfSupport.ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(sumatraPath))
            {
                return false;
            }

            try
            {
                var printSettings = BuildSumatraPrintSettings(pageRange);
                var printSettingsArg = string.IsNullOrWhiteSpace(printSettings)
                    ? string.Empty
                    : $"-print-settings \"{printSettings}\" ";

                var psi = new ProcessStartInfo
                {
                    FileName = sumatraPath,
                    Arguments = $"{printSettingsArg}-print-to \"{printerName}\" -silent -exit-on-print \"{filePath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(15000);
                }

                await System.Threading.Tasks.Task.CompletedTask;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildSumatraPrintSettings(string? pageRange)
        {
            return string.IsNullOrWhiteSpace(pageRange)
                ? string.Empty
                : pageRange;
        }

        private static bool TryNormalizePdfPageRange(string? rawPageRange, out string pageRange, out string errorMessage)
        {
            pageRange = string.Empty;
            errorMessage = string.Empty;

            var raw = (rawPageRange ?? string.Empty).Trim();
            if (raw.Length == 0
                || string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "semua", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "semua halaman", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var tokens = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizePdfPageRangeToken)
                .ToArray();

            if (tokens.Length == 0 || tokens.Any(token => token.Length == 0))
            {
                errorMessage = "Rentang halaman tidak valid. Contoh format: 1, 3-5.";
                return false;
            }

            pageRange = string.Join(",", tokens);
            return true;
        }

        private static string NormalizePdfPageRangeToken(string token)
        {
            var normalized = token.Replace(" ", string.Empty);
            if (int.TryParse(normalized, out var page) && page > 0)
            {
                return page.ToString();
            }

            var parts = normalized.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return string.Empty;
            }

            if (!int.TryParse(parts[0], out var startPage)
                || !int.TryParse(parts[1], out var endPage)
                || startPage <= 0
                || endPage <= 0
                || startPage > endPage)
            {
                return string.Empty;
            }

            return $"{startPage}-{endPage}";
        }

        private string? ResolveEdgePath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "msedge.exe";
        }

        private void TryDeleteTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Abaikan jika file tidak bisa dihapus
            }
        }

        private void TryDeleteTempDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Abaikan jika folder tidak bisa dihapus
            }
        }

        private async System.Threading.Tasks.Task UpdateJobStatusAsync(string jobId, string status)
        {
            var payload = new { status };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}")
            {
                Content = content
            };
            using var response = await SendAuthorizedAsync(request);
        }

        private async System.Threading.Tasks.Task EnsureRegisteredAsync()
        {
            _clientId ??= AppConfig.LoadOrCreateClientId();
            UpdateClientIdLabel();

            await RegisterClientAsync();
        }

        private void UpdateClientIdLabel()
        {
            var value = string.IsNullOrWhiteSpace(_clientId) ? "-" : _clientId;
            labelClientId.Text = $"Client ID: {value}";

            RefreshDashboardState();
        }

        private string? GetSelectedPrinterName()
        {
            var selected = comboPrinters.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return selected;
            }

            try
            {
                var defaultSettings = new PrinterSettings();
                return defaultSettings.PrinterName;
            }
            catch
            {
                return null;
            }
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopTimers();
            StopRealtime();

            try
            {
                _incomingJobSoundPlayer?.Dispose();
                _incomingJobSoundPlayer = null;
            }
            catch
            {
                // Abaikan dispose error.
            }

            // Jangan unregister saat shutdown: biarkan status offline dihitung dari timeout heartbeat.
        }

        private void StopTimers()
        {
            if (_heartbeatTimer != null)
            {
                _heartbeatTimer.Stop();
                _heartbeatTimer.Dispose();
            }

            if (_pingTimer != null)
            {
                _pingTimer.Stop();
                _pingTimer.Dispose();
            }
        }

        private void StartRealtime()
        {
            if (_realtimeCts != null)
            {
                return;
            }

            _realtimeCts = new CancellationTokenSource();
            _realtimeWorker = Task.Run(() => RunRealtimeLoopAsync(_realtimeCts.Token));
        }

        private async Task RunRealtimeLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ClientWebSocket? socket = null;

                try
                {
                    var realtimeUri = BuildRealtimeUri();
                    if (realtimeUri == null)
                    {
                        return;
                    }

                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    _realtimeSocket = socket;

                    await socket.ConnectAsync(realtimeUri, cancellationToken);
                    await SendRealtimeIdentifyAsync(socket, cancellationToken);
                    await SendRealtimeSubscribeAsync(socket, cancellationToken);
                    await ReceiveRealtimeMessagesAsync(socket, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Fallback polling tetap berjalan saat realtime gagal.
                }
                finally
                {
                    if (socket != null)
                    {
                        try
                        {
                            socket.Dispose();
                        }
                        catch
                        {
                            // Abaikan dispose error
                        }

                        if (ReferenceEquals(_realtimeSocket, socket))
                        {
                            _realtimeSocket = null;
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private Uri? BuildRealtimeUri()
        {
            if (!Uri.TryCreate(_serverBaseUrl, UriKind.Absolute, out var baseUri))
            {
                return null;
            }

            var scheme = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws";

            var clientId = _clientId;

            var builder = new UriBuilder(baseUri)
            {
                Scheme = scheme,
                Path = "/ws",
                Query = string.IsNullOrWhiteSpace(clientId)
                    ? "role=client"
                    : $"clientId={Uri.EscapeDataString(clientId)}&role=client",
                Fragment = string.Empty
            };

            if (baseUri.IsDefaultPort)
            {
                builder.Port = -1;
            }

            return builder.Uri;
        }

        private async Task SendRealtimeIdentifyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            var identifyPayload = JsonSerializer.Serialize(new
            {
                action = "identify",
                clientId = _clientId,
                role = "client"
            });

            var bytes = Encoding.UTF8.GetBytes(identifyPayload);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private static async Task SendRealtimeSubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var subscribePayload = JsonSerializer.Serialize(new
            {
                action = "subscribe",
                channels = new[] { "jobs", "clients", "sessions" }
            });

            var bytes = Encoding.UTF8.GetBytes(subscribePayload);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private async Task ReceiveRealtimeMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult? result = null;

                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                        }
                        catch
                        {
                            // Abaikan close error
                        }
                        return;
                    }

                    if (result.Count > 0)
                    {
                        messageStream.Write(buffer, 0, result.Count);
                    }
                }
                while (result != null && !result.EndOfMessage);

                if (result == null || result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var payload = Encoding.UTF8.GetString(messageStream.ToArray());
                HandleRealtimeMessage(payload);
            }
        }

        private void HandleRealtimeMessage(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("type", out var typeElement))
                {
                    return;
                }

                var eventType = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    return;
                }

                switch (eventType)
                {
                    case "job.created":
                        if (ShouldRefreshJobsForCurrentClient(doc.RootElement))
                        {
                            NotifyIncomingJobFromRealtime(doc.RootElement);
                            RequestJobListRefreshFromRealtime();
                        }
                        break;

                    case "job.status.changed":
                    case "jobs.removed":
                        if (ShouldRefreshJobsForCurrentClient(doc.RootElement))
                        {
                            RequestJobListRefreshFromRealtime();
                        }
                        break;
                }
            }
            catch
            {
                // Abaikan pesan realtime yang tidak dikenali.
            }
        }

        private void NotifyIncomingJobFromRealtime(JsonElement root)
        {
            if (!HasSavedAuthState)
            {
                return;
            }

            if (!_notificationOptions.SoundEnabled && !_notificationOptions.DesktopEnabled)
            {
                return;
            }

            TryReadRealtimeJobId(root, out var jobId);
            if (ShouldSkipIncomingJobNotification(jobId))
            {
                return;
            }

            var message = BuildIncomingJobNotificationMessage(root);

            if (_notificationOptions.SoundEnabled)
            {
                PlayIncomingJobSoundOnUiThread();
            }

            if (_notificationOptions.DesktopEnabled)
            {
                ShowIncomingJobToastOnUiThread(message);
            }
        }

        private bool ShouldSkipIncomingJobNotification(string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return false;
            }

            lock (_jobNotificationLock)
            {
                if (_notifiedRealtimeJobIds.Count > 500)
                {
                    _notifiedRealtimeJobIds.Clear();
                }

                return !_notifiedRealtimeJobIds.Add(jobId);
            }
        }

        private static bool TryReadRealtimeJobId(JsonElement root, out string jobId)
        {
            jobId = string.Empty;

            if (!root.TryGetProperty("payload", out var payload))
            {
                return false;
            }

            if (payload.TryGetProperty("job", out var job)
                && TryReadString(job, "id", out jobId))
            {
                return true;
            }

            if (TryReadString(payload, "jobId", out jobId))
            {
                return true;
            }

            if (TryReadString(payload, "id", out jobId))
            {
                return true;
            }

            return false;
        }

        private static string BuildIncomingJobNotificationMessage(JsonElement root)
        {
            if (!root.TryGetProperty("payload", out var payload))
            {
                return "Ada tugas cetak baru yang masuk.";
            }

            if (payload.TryGetProperty("job", out var job))
            {
                if (TryReadString(job, "originalName", out var originalName))
                {
                    return $"Tugas cetak baru: {originalName}";
                }

                if (TryReadString(job, "alias", out var alias))
                {
                    return $"Tugas cetak baru: {alias}";
                }

                if (TryReadString(job, "id", out var jobId))
                {
                    return $"Tugas cetak baru masuk. ID: {jobId}";
                }
            }

            return "Ada tugas cetak baru yang masuk.";
        }

        private void ShowIncomingJobToastOnUiThread(string message)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => ShowIncomingJobToastOnUiThread(message)));
                }
                catch
                {
                    // Abaikan jika form sedang ditutup.
                }

                return;
            }

            ToastNotificationForm.ShowNotification(
                this,
                "Tugas Cetak Baru",
                message,
                TimeSpan.FromSeconds(4));
        }

        private void PlayIncomingJobSoundOnUiThread()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(PlayIncomingJobSoundOnUiThread));
                }
                catch
                {
                    // Abaikan jika form sedang ditutup.
                }

                return;
            }

            PlayIncomingJobSound();
        }

        private void PlayIncomingJobSound()
        {
            try
            {
                var player = GetIncomingJobSoundPlayer();
                if (player != null)
                {
                    player.Play();
                    return;
                }

                SystemSounds.Asterisk.Play();
            }
            catch
            {
                try
                {
                    SystemSounds.Beep.Play();
                }
                catch
                {
                    // Abaikan jika suara sistem tidak tersedia.
                }
            }
        }

        private SoundPlayer? GetIncomingJobSoundPlayer()
        {
            if (_incomingJobSoundPlayerInitialized)
            {
                return _incomingJobSoundPlayer;
            }

            _incomingJobSoundPlayerInitialized = true;

            var soundPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Sounds",
                "job_incoming.wav");

            if (!File.Exists(soundPath))
            {
                return null;
            }

            try
            {
                _incomingJobSoundPlayer = new SoundPlayer(soundPath);
                _incomingJobSoundPlayer.LoadAsync();
                return _incomingJobSoundPlayer;
            }
            catch
            {
                _incomingJobSoundPlayer?.Dispose();
                _incomingJobSoundPlayer = null;
                return null;
            }
        }

        private bool ShouldRefreshJobsForCurrentClient(JsonElement root)
        {
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return false;
            }

            if (!root.TryGetProperty("payload", out var payload))
            {
                return true;
            }

            if (!payload.TryGetProperty("job", out var job))
            {
                return true;
            }

            if (!job.TryGetProperty("targetClientId", out var targetClientIdElement))
            {
                return true;
            }

            var targetClientId = targetClientIdElement.GetString();
            return string.Equals(targetClientId, _clientId, StringComparison.OrdinalIgnoreCase);
        }

        private void RequestJobListRefreshFromRealtime()
        {
            var form = _jobListForm;
            if (form == null || form.IsDisposed)
            {
                return;
            }

            form.RequestRefreshFromRealtime();
        }

        private void StopRealtime()
        {
            if (_realtimeCts != null)
            {
                try
                {
                    _realtimeCts.Cancel();
                }
                catch
                {
                    // Abaikan cancel error
                }

                _realtimeCts.Dispose();
                _realtimeCts = null;
            }

            if (_realtimeSocket != null)
            {
                try
                {
                    _realtimeSocket.Abort();
                    _realtimeSocket.Dispose();
                }
                catch
                {
                    // Abaikan dispose error
                }

                _realtimeSocket = null;
            }

            _realtimeWorker = null;
        }
    }
}
