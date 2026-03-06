using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace PrintForm
{
    public partial class Form1 : Form
    {
        // Menyimpan gambar yang akan di-print via PrintDocument
        private Image? _imageToPrint;
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly string _serverBaseUrl = AppConfig.LoadServerBaseUrl();
        private string? _clientId;
        private System.Windows.Forms.Timer? _heartbeatTimer;
        private System.Windows.Forms.Timer? _pingTimer;
        private bool _registerInProgress;
        private bool _jobProcessing;
        private string? _activeJobId;
        private string? _activeJobTempPath;
        private JobListForm? _jobListForm;

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
            statusLabel.Text = "Siap. Pilih printer lalu buka Print Job.";

            await EnsureRegisteredAsync();
            StartHeartbeat();
            StartPingPolling();
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
                _jobListForm = new JobListForm(Http, _serverBaseUrl, () => _clientId, PrintJobFromListAsync, RejectJobFromListAsync);
            }

            _jobListForm.Show();
            _jobListForm.BringToFront();
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
            if (_imageToPrint == null)
            {
                using Font font = new Font("Segoe UI", 12);
                e.Graphics.DrawString("Tidak ada dokumen gambar yang dipilih.",
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

            e.Graphics.DrawImage(_imageToPrint, drawRect);

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
                _ = UpdateJobStatusAsync(jobId, e.PrintAction == PrintAction.PrintToPrinter ? "done" : "failed");
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
                using var response = await Http.PostAsync($"{_serverBaseUrl}/api/clients/register", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    statusLabel.Text = $"Gagal terhubung ke server ({(int)response.StatusCode}).";
                    return;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("id", out var id))
                {
                    _clientId = id.GetString();
                }

                statusLabel.Text = "Terhubung ke server.";
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
                using var response = await Http.PostAsync($"{_serverBaseUrl}/api/clients/heartbeat", content);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await RegisterClientAsync();
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
                using var response = await Http.GetAsync($"{_serverBaseUrl}/api/clients/{_clientId}/ping");
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await RegisterClientAsync();
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
            using var response = await Http.GetAsync($"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}/download");
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
            using var response = await Http.SendAsync(request);
        }

        private async System.Threading.Tasks.Task EnsureRegisteredAsync()
        {
            if (!string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            await RegisterClientAsync();
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

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopTimers();
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            try
            {
                var payload = new { clientId = _clientId };
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                Http.PostAsync($"{_serverBaseUrl}/api/clients/unregister", content)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Abaikan kegagalan saat shutdown
            }
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
    }
}
