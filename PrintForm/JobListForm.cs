using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintForm
{
    internal sealed class JobListForm : Form
    {
        private readonly string _serverBaseUrl;
        private readonly Func<string?> _getClientId;
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _sendAuthorizedAsync;
        private readonly Func<PrintJob, Task> _printJobAsync;
        private readonly Func<PrintJob, Task> _rejectJobAsync;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _refreshButton = new Button();
        private readonly Label _statusLabel = new Label();
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000
        };
        private bool _isLoading;
        private bool _pendingRefresh;

        public JobListForm(
            string serverBaseUrl,
            Func<string?> getClientId,
            Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAuthorizedAsync,
            Func<PrintJob, Task> printJobAsync,
            Func<PrintJob, Task> rejectJobAsync)
        {
            _serverBaseUrl = serverBaseUrl.TrimEnd('/');
            _getClientId = getClientId;
            _sendAuthorizedAsync = sendAuthorizedAsync;
            _printJobAsync = printJobAsync;
            _rejectJobAsync = rejectJobAsync;

            Text = "Job List";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(780, 420);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8),
                AutoSize = false
            };

            _refreshButton.Text = "Refresh";
            _refreshButton.AutoSize = true;
            _refreshButton.Click += async (_, _) => await LoadJobsAsync();

            _statusLabel.AutoSize = true;
            _statusLabel.Padding = new Padding(8, 8, 0, 0);

            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(_statusLabel);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.CellContentClick += Grid_CellContentClick;

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colDoc",
                HeaderText = "Dokumen",
                FillWeight = 25
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colAlias",
                HeaderText = "Alias",
                FillWeight = 12
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colPaper",
                HeaderText = "Kertas",
                FillWeight = 10
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colCopies",
                HeaderText = "Salinan",
                FillWeight = 10
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colStatus",
                HeaderText = "Status",
                FillWeight = 10
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colTime",
                HeaderText = "Waktu",
                FillWeight = 18
            });
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "colPrint",
                HeaderText = "Aksi",
                UseColumnTextForButtonValue = false,
                FillWeight = 10
            });
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "colReject",
                HeaderText = "Tolak",
                UseColumnTextForButtonValue = false,
                FillWeight = 10
            });

            Controls.Add(_grid);
            Controls.Add(toolbar);

            Shown += async (_, _) => await LoadJobsAsync();
            _refreshTimer.Tick += async (_, _) => await LoadJobsAsync();
            _refreshTimer.Start();
        }

        public void RequestRefreshFromRealtime()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RequestRefreshFromRealtime));
                return;
            }

            _ = LoadJobsAsync();
        }

        private async void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            var row = _grid.Rows[e.RowIndex];
            if (row.Tag is not PrintJob job)
            {
                return;
            }

            var columnName = _grid.Columns[e.ColumnIndex].Name;
            if (columnName == "colPrint")
            {
                var latestJob = await FetchJobAsync(job.Id);
                if (latestJob == null)
                {
                    _statusLabel.Text = "Job tidak ditemukan atau sudah dihapus.";
                    await LoadJobsAsync();
                    return;
                }

                job = latestJob;
                if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    _statusLabel.Text = $"Job sudah berubah status ({job.Status}).";
                    await LoadJobsAsync();
                    return;
                }

                _statusLabel.Text = $"Mencetak {job.OriginalName}...";
                _refreshButton.Enabled = false;
                try
                {
                    await _printJobAsync(job);
                }
                finally
                {
                    _refreshButton.Enabled = true;
                }

                await LoadJobsAsync();
                return;
            }

            if (columnName == "colReject")
            {
                var latestJob = await FetchJobAsync(job.Id);
                if (latestJob == null)
                {
                    _statusLabel.Text = "Job tidak ditemukan atau sudah dihapus.";
                    await LoadJobsAsync();
                    return;
                }

                job = latestJob;
                if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    _statusLabel.Text = $"Job sudah berubah status ({job.Status}).";
                    await LoadJobsAsync();
                    return;
                }

                _statusLabel.Text = $"Menolak {job.OriginalName}...";
                _refreshButton.Enabled = false;
                try
                {
                    await _rejectJobAsync(job);
                }
                finally
                {
                    _refreshButton.Enabled = true;
                }

                await LoadJobsAsync();
            }
        }

        private async Task<PrintJob?> FetchJobAsync(string jobId)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_serverBaseUrl}/api/jobs/{Uri.EscapeDataString(jobId)}");
                using var response = await _sendAuthorizedAsync(request);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _statusLabel.Text = "Sesi login habis. Silakan login lagi.";
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<PrintJob>(body, _jsonOptions);
            }
            catch
            {
                _statusLabel.Text = "Gagal memeriksa status job.";
                return null;
            }
        }

        private async Task LoadJobsAsync()
        {
            if (_isLoading)
            {
                _pendingRefresh = true;
                return;
            }

            _isLoading = true;
            var clientId = _getClientId();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _statusLabel.Text = "Client belum terhubung.";
                _grid.Rows.Clear();
                _isLoading = false;
                return;
            }

            try
            {
                var encodedClientId = Uri.EscapeDataString(clientId);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_serverBaseUrl}/api/jobs?claimClientId={encodedClientId}&activeSessionOnly=true");
                using var response = await _sendAuthorizedAsync(request);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _statusLabel.Text = "Perlu login mitra untuk melihat job.";
                    _grid.Rows.Clear();
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _statusLabel.Text = "Gagal memuat job.";
                    return;
                }

                var body = await response.Content.ReadAsStringAsync();
                var jobs = DeserializeVisibleJobs(body);

                RenderJobs(jobs);
                _statusLabel.Text = $"Total job: {jobs.Count}";
            }
            catch
            {
                _statusLabel.Text = "Tidak bisa terhubung ke server.";
            }
            finally
            {
                _isLoading = false;
                if (_pendingRefresh)
                {
                    _pendingRefresh = false;
                    _ = LoadJobsAsync();
                }
            }
        }

        private List<PrintJob> DeserializeVisibleJobs(string body)
        {
            var jobs = new List<PrintJob>();

            if (string.IsNullOrWhiteSpace(body))
            {
                return jobs;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        AddJobIfVisible(item, jobs);
                    }

                    return jobs;
                }

                // Jaga-jaga kalau response server nanti berubah menjadi { items: [...] }
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("items", out var items)
                    && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        AddJobIfVisible(item, jobs);
                    }
                }
            }
            catch
            {
                _statusLabel.Text = "Format data job tidak valid.";
            }

            return jobs;
        }

        private void AddJobIfVisible(JsonElement item, List<PrintJob> jobs)
        {
            if (ShouldHideJobBySessionStatus(item))
            {
                return;
            }

            var job = item.Deserialize<PrintJob>(_jsonOptions);
            if (job != null)
            {
                jobs.Add(job);
            }
        }

        private static bool ShouldHideJobBySessionStatus(JsonElement jobElement)
        {
            if (TryReadSessionStatus(jobElement, out var status))
            {
                return IsClosedOrExpiredSession(status);
            }

            return false;
        }

        private static bool TryReadSessionStatus(JsonElement jobElement, out string status)
        {
            status = string.Empty;

            // Kemungkinan response langsung:
            // { "sessionStatus": "expired" }
            // { "printSessionStatus": "closed" }
            // { "session_status": "expired" }
            var directStatusFields = new[]
            {
                "sessionStatus",
                "printSessionStatus",
                "session_status",
                "print_session_status"
            };

            foreach (var field in directStatusFields)
            {
                if (TryReadStringProperty(jobElement, field, out status))
                {
                    return true;
                }
            }

            // Kemungkinan response nested:
            // { "session": { "status": "expired" } }
            // { "printSession": { "status": "closed" } }
            var sessionObjectFields = new[]
            {
                "session",
                "printSession",
                "print_session"
            };

            foreach (var field in sessionObjectFields)
            {
                if (!TryReadObjectProperty(jobElement, field, out var sessionElement))
                {
                    continue;
                }

                if (TryReadStringProperty(sessionElement, "status", out status))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsClosedOrExpiredSession(string? status)
        {
            return string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadStringProperty(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                value = property.Value.GetString()?.Trim() ?? string.Empty;
                return value.Length > 0;
            }

            return false;
        }

        private static bool TryReadObjectProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            value = default;

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                value = property.Value;
                return true;
            }

            return false;
        }

        private void RenderJobs(List<PrintJob> jobs)
        {
            _grid.Rows.Clear();
            foreach (var job in jobs)
            {
                var paper = job.PrintConfig?.PaperSize ?? "-";
                var copies = job.PrintConfig?.Copies.ToString() ?? "-";
                var timeText = job.CreatedAt;
                if (DateTime.TryParse(job.CreatedAt, out var dt))
                {
                    timeText = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                }
                var alias = string.IsNullOrWhiteSpace(job.Alias) ? "-" : job.Alias;

                var displayStatus = GetStatusLabel(job.Status);
                var rowIndex = _grid.Rows.Add(job.OriginalName, alias, paper, copies, displayStatus, timeText);
                var row = _grid.Rows[rowIndex];
                row.Tag = job;

                var buttonCell = (DataGridViewButtonCell)row.Cells["colPrint"];
                var rejectCell = (DataGridViewButtonCell)row.Cells["colReject"];
                if (string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    buttonCell.Value = "Print";
                    rejectCell.Value = "Tolak";
                    row.Cells["colReject"].Style.ForeColor = Color.Black;
                }
                else if (string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    buttonCell.Value = "Retry";
                    rejectCell.Value = "-";
                    row.Cells["colReject"].Style.ForeColor = Color.Gray;
                }
                else
                {
                    buttonCell.Value = "-";
                    row.Cells["colPrint"].Style.ForeColor = Color.Gray;
                    rejectCell.Value = "-";
                    row.Cells["colReject"].Style.ForeColor = Color.Gray;
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            base.OnFormClosed(e);
        }

        private static string GetStatusLabel(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "-";
            }

            return string.Equals(status, "done", StringComparison.OrdinalIgnoreCase)
                ? "Sent"
                : status;
        }
    }
}
