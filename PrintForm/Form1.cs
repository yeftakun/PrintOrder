using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintForm
{
    public partial class Form1 : Form
    {
        // Menyimpan gambar yang akan di-print via PrintDocument
        private Image? _imageToPrint;
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

        private bool HasSavedAuthState => !string.IsNullOrWhiteSpace(_refreshToken);
        private bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

        public Form1()
        {
            InitializeComponent();
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
            statusLabel.Text = "Siap. Pilih printer lalu buka Print Job.";

            if (HasSavedAuthState)
            {
                statusLabel.Text = "Memulihkan sesi login...";
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
            using var settingsForm = new SettingsForm(_serverBaseUrl);
            var result = settingsForm.ShowDialog(this);

            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(settingsForm.SavedBaseUrl))
            {
                statusLabel.Text = "Pengaturan tidak diubah.";
                return;
            }

            statusLabel.Text = "Pengaturan disimpan. Restart aplikasi untuk menerapkan base_url baru.";
            MessageBox.Show(
                "Perubahan tersimpan ke printform.ini.\nRestart aplikasi agar koneksi memakai base_url terbaru.",
                "Pengaturan",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            if (HasSavedAuthState)
            {
                var confirm = MessageBox.Show(
                    this,
                    "Keluar dari akun ini?",
                    "Logout",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                await LogoutAsync();
                return;
            }

            using var loginForm = new LoginForm(_authUsername);
            var loginResult = loginForm.ShowDialog(this);
            if (loginResult != DialogResult.OK)
            {
                return;
            }

            await LoginAsync(loginForm.Identifier, loginForm.Password);
        }

        private void comboPrinters_SelectedIndexChanged(object? sender, EventArgs e)
        {
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
                using Font font = new Font("Segoe UI", 12);
                graphics.DrawString("Tidak ada dokumen gambar yang dipilih.",
                                    font, Brushes.Black,
                                    e.MarginBounds.Location);
                e.HasMorePages = false;
                return;
            }

            Rectangle m = e.MarginBounds;

            // Rasio aspek gambar dan halaman
            float imgRatio = (float)_imageToPrint.Width / _imageToPrint.Height;
            float pageRatio = (float)m.Width / m.Height;

            Rectangle drawRect;

            if (imgRatio > pageRatio)
            {
                // Gambar lebih lebar
                int drawWidth = m.Width;
                int drawHeight = (int)(m.Width / imgRatio);
                int drawY = m.Top + (m.Height - drawHeight) / 2;
                drawRect = new Rectangle(m.Left, drawY, drawWidth, drawHeight);
            }
            else
            {
                // Gambar lebih tinggi
                int drawHeight = m.Height;
                int drawWidth = (int)(m.Height * imgRatio);
                int drawX = m.Left + (m.Width - drawWidth) / 2;
                drawRect = new Rectangle(drawX, m.Top, drawWidth, drawHeight);
            }

            graphics.DrawImage(_imageToPrint, drawRect);

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
            if (IsAuthenticated)
            {
                var displayName = string.IsNullOrWhiteSpace(_authUsername) ? _authUserId : _authUsername;
                labelAuthUser.Text = $"Akun: {displayName ?? "terautentikasi"}";
            }
            else if (HasSavedAuthState)
            {
                var displayName = string.IsNullOrWhiteSpace(_authUsername) ? _authUserId : _authUsername;
                labelAuthUser.Text = $"Akun tersimpan: {displayName ?? "-"}";
            }
            else
            {
                labelAuthUser.Text = "Akun: belum login";
            }

            btnLogin.Text = HasSavedAuthState ? "Logout" : "Login";
        }

        private async Task LoginAsync(string identifier, string password)
        {
            try
            {
                statusLabel.Text = "Login ke server...";

                var payload = JsonSerializer.Serialize(new
                {
                    identifier,
                    password
                });

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync($"{_serverBaseUrl}/api/auth/login", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var apiError = TryExtractApiError(responseBody);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        statusLabel.Text = "Login gagal. Periksa identifier/password.";
                    }
                    else
                    {
                        statusLabel.Text = apiError ?? $"Login gagal ({(int)response.StatusCode}).";
                    }
                    return;
                }

                if (!TryApplyAuthBundle(responseBody))
                {
                    statusLabel.Text = "Respons login tidak valid.";
                    return;
                }

                statusLabel.Text = $"Login berhasil sebagai {_authUsername ?? identifier}.";
                await RegisterClientAsync();
            }
            catch
            {
                statusLabel.Text = "Tidak bisa login ke server.";
            }
        }

        private async Task LogoutAsync()
        {
            var refreshToken = _refreshToken;
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                try
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        refreshToken
                    });
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var _ = await Http.PostAsync($"{_serverBaseUrl}/api/auth/logout", content);
                }
                catch
                {
                    // Abaikan error logout server; state lokal tetap dibersihkan.
                }
            }

            ClearAuthState();
            statusLabel.Text = "Logout berhasil.";
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
                            statusLabel.Text = "Sesi login berakhir. Silakan login ulang.";
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
                    statusLabel.Text = "Tidak bisa memperbarui sesi login.";
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
                        statusLabel.Text = "Client dikenali server. Silakan login agar bisa menerima job.";
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

                if (recognized && !IsAuthenticated)
                {
                    statusLabel.Text = "Client dikenali. Login diperlukan untuk operasi lanjutan.";
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
                    statusLabel.Text = "Perlu login mitra untuk mengaktifkan client ini.";
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    statusLabel.Text = "Koneksi server terputus.";
                }
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
                    statusLabel.Text = "Perlu login mitra untuk sinkronisasi ping.";
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

                var printed = await PrintNonImageAsync(downloadPath);
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

            if (job.PrintConfig != null)
            {
                if (job.PrintConfig.Copies >= 1 && job.PrintConfig.Copies <= 999)
                {
                    printDocument1.PrinterSettings.Copies = (short)job.PrintConfig.Copies;
                }

                if (!string.IsNullOrWhiteSpace(job.PrintConfig.PaperSize))
                {
                    foreach (PaperSize size in printDocument1.PrinterSettings.PaperSizes)
                    {
                        if (string.Equals(size.PaperName, job.PrintConfig.PaperSize, StringComparison.OrdinalIgnoreCase))
                        {
                            printDocument1.DefaultPageSettings.PaperSize = size;
                            break;
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
                statusLabel.Text = "Sesi login habis. Login ulang sebelum mengambil file job.";
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

        private async System.Threading.Tasks.Task<bool> PrintNonImageAsync(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                if (await TryPrintPdfWithSumatraAsync(filePath))
                {
                    return true;
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
                var userDataDir = Path.Combine(Path.GetTempPath(), $"printform-edge-{Guid.NewGuid():N}");
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

        private async System.Threading.Tasks.Task<bool> TryPrintPdfWithSumatraAsync(string filePath)
        {
            var printerName = GetSelectedPrinterName();
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return false;
            }

            var sumatraPath = ResolveSumatraPath();
            if (string.IsNullOrWhiteSpace(sumatraPath))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sumatraPath,
                    Arguments = $"-print-to \"{printerName}\" -silent -exit-on-print \"{filePath}\"",
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

        private string? ResolveSumatraPath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                Path.Combine(programFilesX86, "SumatraPDF", "SumatraPDF.exe"),
                Path.Combine(programFiles, "SumatraPDF", "SumatraPDF.exe"),
                Path.Combine(localAppData, "SumatraPDF", "SumatraPDF.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
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
