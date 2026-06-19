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
using System.Reflection;
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
        private const string InsufficientCreditCode = "INSUFFICIENT_CREDIT";
        private const string InsufficientCreditOperatorMessage = "Kredit toko tidak cukup. Tambahkan kredit atau aktifkan plan sebelum mencetak.";
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly HttpClient DownloadHttp = CreateDownloadHttpClient();
        private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
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
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private Icon? _applicationIcon;
        private bool _allowApplicationExit;

        private readonly object _jobNotificationLock = new object();
        private readonly HashSet<string> _notifiedRealtimeJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private NotificationOptions _notificationOptions = AppConfig.LoadNotificationOptions();
        private SoundPlayer? _incomingJobSoundPlayer;
        private bool _incomingJobSoundPlayerInitialized;

        private bool HasSavedAuthState => !string.IsNullOrWhiteSpace(_refreshToken);
        private bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

        private static string ResolveDashboardTitle()
        {
            var informationalVersion = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            var version = string.IsNullOrWhiteSpace(informationalVersion)
                ? Application.ProductVersion
                : informationalVersion;

            var metadataIndex = version.IndexOf('+');
            if (metadataIndex > 0)
            {
                version = version[..metadataIndex];
            }

            return $"PrintOrder Client v{version}";
        }

        private sealed class JobStatusUpdateResult
        {
            public JobStatusUpdateResult(
                bool isSuccess,
                HttpStatusCode statusCode,
                string? code,
                string? error,
                PrintJob? job)
            {
                IsSuccess = isSuccess;
                StatusCode = statusCode;
                Code = code;
                Error = error;
                Job = job;
            }

            public bool IsSuccess { get; }
            public HttpStatusCode StatusCode { get; }
            public string? Code { get; }
            public string? Error { get; }
            public PrintJob? Job { get; }

            public bool IsInsufficientCredit =>
                StatusCode == (HttpStatusCode)402
                && string.Equals(Code, InsufficientCreditCode, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PdfPrintAttemptResult
        {
            public bool Success { get; init; }
            public string Engine { get; init; } = string.Empty;
            public bool EngineMissing { get; init; }
            public bool TimedOut { get; init; }
            public int? ExitCode { get; init; }
            public string FailureMessage { get; init; } = string.Empty;
        }

        public Form1()
        {
            InitializeComponent();

            HideFooterStatus();
            BindDashboardStatusToHiddenFooter();
            InitializeApplicationIcon();
            InitializeTrayIcon();

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
                Timeout = TimeSpan.FromSeconds(AppConfig.LoadTimeoutOptions().ApiTimeoutSeconds)
            };
        }

        private static HttpClient CreateDownloadHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
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
            return await SendAuthorizedAsync(
                request,
                Http,
                HttpCompletionOption.ResponseContentRead,
                CancellationToken.None);
        }

        private async Task<HttpResponseMessage> SendAuthorizedAsync(
            HttpRequestMessage request,
            HttpClient httpClient,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_accessToken) && HasSavedAuthState)
            {
                await TryRefreshAccessTokenAsync(updateStatusOnFailure: false);
            }

            var retryRequest = await CloneRequestAsync(request);

            ApplyAuthorizationHeader(request);
            var response = await httpClient.SendAsync(request, completionOption, cancellationToken);

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
            return await httpClient.SendAsync(retryRequest, completionOption, cancellationToken);
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

        private static string? TryExtractApiCode(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (!TryReadString(doc.RootElement, "code", out var code))
                {
                    return null;
                }

                return code;
            }
            catch
            {
                return null;
            }
        }

        private static PrintJob? TryExtractJobFromResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (doc.RootElement.TryGetProperty("job", out var jobElement)
                    && jobElement.ValueKind == JsonValueKind.Object)
                {
                    return jobElement.Deserialize<PrintJob>(CaseInsensitiveJsonOptions);
                }

                if (doc.RootElement.TryGetProperty("id", out _)
                    && doc.RootElement.TryGetProperty("status", out _))
                {
                    return doc.RootElement.Deserialize<PrintJob>(CaseInsensitiveJsonOptions);
                }

                return null;
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

        private async System.Threading.Tasks.Task<PrintJob?> PrintJobFromListAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                statusLabel.Text = "Masih memproses job lain.";
                return null;
            }

            return await ProcessJobAsync(job);
        }

        private async System.Threading.Tasks.Task RejectJobFromListAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                statusLabel.Text = "Masih memproses job lain.";
                return;
            }

            var rejectResult = await UpdateJobStatusAsync(job.Id, "rejected");
            statusLabel.Text = rejectResult.IsSuccess
                ? $"Job {job.Id} ditolak."
                : rejectResult.Error ?? $"Gagal menolak job ({(int)rejectResult.StatusCode}).";
        }

        private async System.Threading.Tasks.Task<PrintJob?> ProcessJobAsync(PrintJob job)
        {
            if (_jobProcessing)
            {
                return null;
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
                    var pendingResult = await UpdateJobStatusAsync(job.Id, "pending");
                    if (!pendingResult.IsSuccess)
                    {
                        return HandleJobStartDenied(job, pendingResult);
                    }

                    _jobProcessing = false;
                    _activeJobId = null;
                    return null;
                }

                SetClientJobStatus(job.Id, "downloading", $"Mengunduh file job {job.Id}...");
                var downloadPath = await DownloadJobFileAsync(job.Id, job.OriginalName);
                if (string.IsNullOrWhiteSpace(downloadPath))
                {
                    await UpdateJobStatusAsync(job.Id, "failed");
                    _jobProcessing = false;
                    _activeJobId = null;
                    return null;
                }

                _activeJobTempPath = downloadPath;

                var printStartResult = await UpdateJobStatusAsync(job.Id, "printing");
                if (!printStartResult.IsSuccess)
                {
                    TryDeleteTempFile(downloadPath);
                    _activeJobTempPath = null;
                    return HandleJobStartDenied(job, printStartResult);
                }

                SetClientJobStatus(job.Id, "printing", $"Mencetak job {job.Id}...");

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
                    return null;
                }

                var printed = await PrintNonImageAsync(downloadPath, job.PrintConfig);
                TryDeleteTempFile(downloadPath);
                _activeJobTempPath = null;
                await UpdateJobStatusAsync(job.Id, printed ? "done" : "failed");
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    $"Print job processing error. jobId={job.Id}, errorType={ex.GetType().Name}, message=\"{ex.Message}\"");
                if (!string.IsNullOrWhiteSpace(job.Id))
                {
                    await UpdateJobStatusAsync(job.Id, "failed");
                }

                return null;
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

            return null;
        }

        private void SetClientJobStatus(string jobId, string localStatus, string message)
        {
            statusLabel.Text = message;
            _jobListForm?.SetLocalJobStatus(jobId, localStatus, message);
        }

        private PrintJob? HandleJobStartDenied(PrintJob job, JobStatusUpdateResult result)
        {
            if (result.IsInsufficientCredit)
            {
                LogInsufficientCreditRejection(job, result);
                statusLabel.Text = InsufficientCreditOperatorMessage;
                MessageBox.Show(
                    InsufficientCreditOperatorMessage,
                    "Kredit Tidak Cukup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return result.Job;
            }

            var message = result.StatusCode == HttpStatusCode.Unauthorized
                ? "Sesi pairing habis. Pair akun ulang sebelum mencetak."
                : result.Error ?? $"Gagal memulai job cetak ({(int)result.StatusCode}).";

            statusLabel.Text = message;
            MessageBox.Show(
                message,
                "Gagal Mencetak",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return result.Job;
        }

        private static void LogInsufficientCreditRejection(PrintJob job, JobStatusUpdateResult result)
        {
            var serverJobId = string.IsNullOrWhiteSpace(result.Job?.Id) ? job.Id : result.Job.Id;
            var serverStatus = string.IsNullOrWhiteSpace(result.Job?.Status) ? "-" : result.Job.Status;
            Trace.TraceWarning(
                $"Print job rejected because store credit is insufficient. jobId={serverJobId}, serverStatus={serverStatus}, httpStatus={(int)result.StatusCode}");
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
            var timeouts = AppConfig.LoadTimeoutOptions();
            using var downloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeouts.DownloadTimeoutSeconds));
            var stopwatch = Stopwatch.StartNew();
            string? filePath = null;

            Trace.TraceInformation(
                $"Job file download started. jobId={jobId}, timeoutSeconds={timeouts.DownloadTimeoutSeconds}");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}/download");
                using var response = await SendAuthorizedAsync(
                    request,
                    DownloadHttp,
                    HttpCompletionOption.ResponseHeadersRead,
                    downloadCts.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    statusLabel.Text = "Sesi pairing habis. Pair akun ulang sebelum mengambil file job.";
                    Trace.TraceWarning(
                        $"Job file download unauthorized. jobId={jobId}, elapsedMs={stopwatch.ElapsedMilliseconds}");
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(downloadCts.Token);
                    var apiError = TryExtractApiError(responseBody);
                    statusLabel.Text = apiError ?? $"Gagal download file job ({(int)response.StatusCode}).";
                    Trace.TraceWarning(
                        $"Job file download failed. jobId={jobId}, httpStatus={(int)response.StatusCode}, elapsedMs={stopwatch.ElapsedMilliseconds}, reason=\"{statusLabel.Text}\"");
                    return null;
                }

                var contentLength = response.Content.Headers.ContentLength;
                filePath = Path.Combine(Path.GetTempPath(), BuildJobTempFileName(jobId, originalName));

                await using (var stream = await response.Content.ReadAsStreamAsync(downloadCts.Token))
                await using (var fileStream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.CopyToAsync(fileStream, downloadCts.Token);
                    await fileStream.FlushAsync(downloadCts.Token);
                }

                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Exists ? fileInfo.Length : 0;
                if (!fileInfo.Exists || fileSize <= 0)
                {
                    statusLabel.Text = "Download file job gagal: file temp kosong.";
                    Trace.TraceWarning(
                        $"Job file download validation failed. jobId={jobId}, sizeBytes={fileSize}, contentLengthBytes={FormatNullableLength(contentLength)}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", reason=\"empty file\"");
                    TryDeleteTempFile(filePath);
                    return null;
                }

                if (contentLength.HasValue && fileSize != contentLength.Value)
                {
                    statusLabel.Text = "Download file job gagal: ukuran file tidak lengkap.";
                    Trace.TraceWarning(
                        $"Job file download validation failed. jobId={jobId}, sizeBytes={fileSize}, contentLengthBytes={contentLength.Value}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", reason=\"content length mismatch\"");
                    TryDeleteTempFile(filePath);
                    return null;
                }

                Trace.TraceInformation(
                    $"Job file download completed. jobId={jobId}, sizeBytes={fileSize}, contentLengthBytes={FormatNullableLength(contentLength)}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\"");
                return filePath;
            }
            catch (OperationCanceledException) when (downloadCts.IsCancellationRequested)
            {
                statusLabel.Text = $"Download timeout setelah {timeouts.DownloadTimeoutSeconds} detik untuk job {jobId}.";
                Trace.TraceWarning(
                    $"Job file download timeout. jobId={jobId}, timeoutSeconds={timeouts.DownloadTimeoutSeconds}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath ?? "-"}\"");
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    TryDeleteTempFile(filePath);
                }

                return null;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Download file job gagal. Periksa koneksi/server dan coba lagi.";
                Trace.TraceError(
                    $"Job file download error. jobId={jobId}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath ?? "-"}\", errorType={ex.GetType().Name}, message=\"{ex.Message}\"");
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    TryDeleteTempFile(filePath);
                }

                return null;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private static string BuildJobTempFileName(string jobId, string originalName)
        {
            var safeJobId = SanitizeFileNameSegment(jobId);
            if (string.IsNullOrWhiteSpace(safeJobId))
            {
                safeJobId = "job";
            }

            var safeName = SanitizeFileNameSegment(Path.GetFileName(originalName));
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "file.bin";
            }

            if (safeName.Length > 120)
            {
                var extension = Path.GetExtension(safeName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
                var maxNameLength = Math.Max(1, 120 - extension.Length);
                safeName = nameWithoutExtension[..Math.Min(nameWithoutExtension.Length, maxNameLength)] + extension;
            }

            return $"printorder-{safeJobId}-{Guid.NewGuid():N}-{safeName}";
        }

        private static string SanitizeFileNameSegment(string? value)
        {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Length == 0)
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(candidate.Length);
            foreach (var ch in candidate)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.ToString();
        }

        private static string FormatNullableLength(long? value)
        {
            return value.HasValue ? value.Value.ToString() : "-";
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

                var sumatraResult = await TryPrintPdfWithSumatraAsync(filePath, pageRange);
                if (sumatraResult.Success)
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(pageRange))
                {
                    if (sumatraResult.EngineMissing)
                    {
                        statusLabel.Text = "Gagal mencetak PDF sesuai rentang halaman. Install SumatraPDF agar rentang halaman didukung.";
                    }
                    else if (sumatraResult.TimedOut)
                    {
                        statusLabel.Text = "Proses print PDF timeout di SumatraPDF. Naikkan sumatra_print_timeout_seconds di printorder.ini jika file besar.";
                    }
                    else
                    {
                        statusLabel.Text = sumatraResult.FailureMessage.Length > 0
                            ? sumatraResult.FailureMessage
                            : "Gagal mencetak PDF sesuai rentang halaman lewat SumatraPDF.";
                    }

                    return false;
                }

                var edgeHeadlessResult = await TryPrintPdfWithEdgeAsync(filePath, headless: true);
                if (edgeHeadlessResult.Success)
                {
                    return true;
                }

                var edgeWindowResult = await TryPrintPdfWithEdgeAsync(filePath, headless: false);
                if (edgeWindowResult.Success)
                {
                    return true;
                }

                if (sumatraResult.TimedOut || edgeHeadlessResult.TimedOut || edgeWindowResult.TimedOut)
                {
                    statusLabel.Text = sumatraResult.EngineMissing
                        ? "Proses print PDF timeout lewat Edge. Install SumatraPDF atau naikkan timeout proses print di printorder.ini."
                        : "Proses print PDF timeout. Naikkan timeout proses print di printorder.ini jika file besar.";
                }
                else if (sumatraResult.EngineMissing)
                {
                    statusLabel.Text = "Gagal mencetak PDF. Install SumatraPDF.";
                }
                else if (edgeWindowResult.FailureMessage.Length > 0)
                {
                    statusLabel.Text = edgeWindowResult.FailureMessage;
                }
                else if (edgeHeadlessResult.FailureMessage.Length > 0)
                {
                    statusLabel.Text = edgeHeadlessResult.FailureMessage;
                }
                else
                {
                    statusLabel.Text = "Gagal mencetak PDF. Install SumatraPDF.";
                }

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

        private async System.Threading.Tasks.Task<PdfPrintAttemptResult> TryPrintPdfWithEdgeAsync(string filePath)
        {
            return await TryPrintPdfWithEdgeAsync(filePath, headless: true);
        }

        private async System.Threading.Tasks.Task<PdfPrintAttemptResult> TryPrintPdfWithEdgeAsync(string filePath, bool headless)
        {
            var engineName = headless ? "EdgeHeadless" : "EdgeWindow";
            var jobId = string.IsNullOrWhiteSpace(_activeJobId) ? "-" : _activeJobId;
            var fileSize = GetFileSizeForLog(filePath);
            var timeouts = AppConfig.LoadTimeoutOptions();
            var timeoutSeconds = headless
                ? timeouts.EdgeHeadlessPrintTimeoutSeconds
                : timeouts.EdgeWindowPrintTimeoutSeconds;
            var timeoutMs = timeoutSeconds * 1000;
            var stopwatch = Stopwatch.StartNew();

            var printerName = GetSelectedPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                Trace.TraceWarning(
                    $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"printer not selected\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    FailureMessage = "Printer tidak ditemukan."
                };
            }

            var edgePath = ResolveEdgePath();
            if (string.IsNullOrWhiteSpace(edgePath))
            {
                Trace.TraceWarning(
                    $"PDF print engine missing. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"Edge executable not found\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    EngineMissing = true,
                    FailureMessage = "Microsoft Edge tidak ditemukan."
                };
            }

            var userDataDir = Path.Combine(Path.GetTempPath(), $"printorder-edge-{Guid.NewGuid():N}");
            try
            {
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

                Trace.TraceInformation(
                    $"PDF print started. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, timeoutSeconds={timeoutSeconds}, tempPath=\"{filePath}\"");

                using var p = Process.Start(psi);
                if (p == null)
                {
                    Trace.TraceWarning(
                        $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"process did not start\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        FailureMessage = "Gagal menjalankan Microsoft Edge untuk print PDF."
                    };
                }

                var exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    TryKillProcessTree(p);
                    Trace.TraceWarning(
                        $"PDF print timeout. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, timeoutSeconds={timeoutSeconds}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        TimedOut = true,
                        FailureMessage = $"Proses print PDF timeout lewat {engineName}."
                    };
                }

                var exitCode = p.ExitCode;
                if (exitCode != 0)
                {
                    Trace.TraceWarning(
                        $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, exitCode={exitCode}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", reason=\"non-zero exit code\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        ExitCode = exitCode,
                        FailureMessage = $"Gagal mencetak PDF lewat {engineName} (exit code {exitCode})."
                    };
                }

                await System.Threading.Tasks.Task.CompletedTask;
                Trace.TraceInformation(
                    $"PDF print completed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, exitCode={exitCode}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\"");
                return new PdfPrintAttemptResult
                {
                    Success = true,
                    Engine = engineName,
                    ExitCode = exitCode
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    $"PDF print error. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", errorType={ex.GetType().Name}, message=\"{ex.Message}\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    FailureMessage = $"Gagal mencetak PDF lewat {engineName}."
                };
            }
            finally
            {
                stopwatch.Stop();
                TryDeleteTempDirectory(userDataDir);
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

        private async System.Threading.Tasks.Task<PdfPrintAttemptResult> TryPrintPdfWithSumatraAsync(string filePath, string? pageRange)
        {
            const string engineName = "SumatraPDF";
            var jobId = string.IsNullOrWhiteSpace(_activeJobId) ? "-" : _activeJobId;
            var fileSize = GetFileSizeForLog(filePath);
            var timeouts = AppConfig.LoadTimeoutOptions();
            var timeoutSeconds = timeouts.SumatraPrintTimeoutSeconds;
            var timeoutMs = timeoutSeconds * 1000;
            var stopwatch = Stopwatch.StartNew();

            var printerName = GetSelectedPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                Trace.TraceWarning(
                    $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"printer not selected\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    FailureMessage = "Printer tidak ditemukan."
                };
            }

            var sumatraPath = SumatraPdfSupport.ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(sumatraPath))
            {
                Trace.TraceWarning(
                    $"PDF print engine missing. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"SumatraPDF executable not found\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    EngineMissing = true,
                    FailureMessage = "SumatraPDF belum ditemukan."
                };
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

                Trace.TraceInformation(
                    $"PDF print started. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, timeoutSeconds={timeoutSeconds}, tempPath=\"{filePath}\"");

                using var p = Process.Start(psi);
                if (p == null)
                {
                    Trace.TraceWarning(
                        $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, tempPath=\"{filePath}\", reason=\"process did not start\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        FailureMessage = "Gagal menjalankan SumatraPDF untuk print PDF."
                    };
                }

                var exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    TryKillProcessTree(p);
                    Trace.TraceWarning(
                        $"PDF print timeout. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, timeoutSeconds={timeoutSeconds}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        TimedOut = true,
                        FailureMessage = "Proses print PDF timeout di SumatraPDF."
                    };
                }

                var exitCode = p.ExitCode;
                if (exitCode != 0)
                {
                    Trace.TraceWarning(
                        $"PDF print failed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, exitCode={exitCode}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", reason=\"non-zero exit code\"");
                    return new PdfPrintAttemptResult
                    {
                        Engine = engineName,
                        ExitCode = exitCode,
                        FailureMessage = $"Gagal mencetak PDF lewat SumatraPDF (exit code {exitCode})."
                    };
                }

                await System.Threading.Tasks.Task.CompletedTask;
                Trace.TraceInformation(
                    $"PDF print completed. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, exitCode={exitCode}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\"");
                return new PdfPrintAttemptResult
                {
                    Success = true,
                    Engine = engineName,
                    ExitCode = exitCode
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    $"PDF print error. jobId={jobId}, engine={engineName}, fileSizeBytes={fileSize}, elapsedMs={stopwatch.ElapsedMilliseconds}, tempPath=\"{filePath}\", errorType={ex.GetType().Name}, message=\"{ex.Message}\"");
                return new PdfPrintAttemptResult
                {
                    Engine = engineName,
                    FailureMessage = "Gagal mencetak PDF lewat SumatraPDF."
                };
            }
            finally
            {
                stopwatch.Stop();
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

        private static long GetFileSizeForLog(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                return fileInfo.Exists ? fileInfo.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Abaikan kegagalan kill; caller tetap melaporkan timeout.
            }
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

        private async System.Threading.Tasks.Task<JobStatusUpdateResult> UpdateJobStatusAsync(string jobId, string status)
        {
            var payload = new { status };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}")
            {
                Content = content
            };
            try
            {
                using var response = await SendAuthorizedAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                return new JobStatusUpdateResult(
                    response.IsSuccessStatusCode,
                    response.StatusCode,
                    TryExtractApiCode(responseBody),
                    TryExtractApiError(responseBody),
                    TryExtractJobFromResponse(responseBody));
            }
            catch
            {
                return new JobStatusUpdateResult(
                    false,
                    0,
                    null,
                    "Tidak bisa menghubungi server.",
                    null);
            }
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

        private void InitializeApplicationIcon()
        {
            _applicationIcon = TryLoadApplicationIcon();
            if (_applicationIcon != null)
            {
                Icon = _applicationIcon;
            }
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip(components);
            _trayMenu.Items.Add("Dashboard", null, (_, _) => ShowDashboardFromTray());
            _trayMenu.Items.Add("Buka Portal", null, (_, _) => OpenPortalFromTray());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Tutup", null, (_, _) => RequestApplicationExit());

            _trayIcon = new NotifyIcon(components)
            {
                ContextMenuStrip = _trayMenu,
                Icon = _applicationIcon ?? SystemIcons.Application,
                Text = "PrintOrder",
                Visible = true
            };

            _trayIcon.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowDashboardFromTray();
                }
            };
        }

        private void OpenPortalFromTray()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = BuildPortalUrl(),
                    UseShellExecute = true
                });
            }
            catch
            {
                statusLabel.Text = "Tidak bisa membuka portal PrintOrder.";
            }
        }

        private string BuildPortalUrl()
        {
            return $"{_serverBaseUrl.TrimEnd('/')}/portal";
        }

        private void ShowDashboardFromTray()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowDashboardFromTray));
                return;
            }

            ShowInTaskbar = true;
            Show();

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            BringToFront();
            Activate();
        }

        private void HideDashboardToTray()
        {
            Hide();
            ShowInTaskbar = false;
            statusLabel.Text = "Dashboard ditutup. PrintOrder tetap berjalan di system tray.";
        }

        private void RequestApplicationExit()
        {
            if (!ConfirmApplicationExit())
            {
                return;
            }

            _allowApplicationExit = true;
            Close();
        }

        private bool ConfirmApplicationExit()
        {
            var result = MessageBox.Show(
                "Tutup PrintOrder?\n\nAplikasi akan berhenti menerima tugas cetak sampai dibuka kembali.",
                "Konfirmasi Keluar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            return result == DialogResult.Yes;
        }

        private static Icon? TryLoadApplicationIcon()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "logo_printorder.ico"),
                Path.Combine(AppContext.BaseDirectory, "logo_printorder.ico")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    return new Icon(path);
                }
                catch
                {
                    // Icon optional untuk runtime; executable tetap punya embedded icon.
                }
            }

            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                return null;
            }
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
            if (!_allowApplicationExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideDashboardToTray();
                return;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
            }

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
