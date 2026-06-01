using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintOrder
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

        private readonly TextBox _searchTextBox = new TextBox();
        private readonly JobCommandButton _refreshButton = new JobCommandButton();
        private readonly Label _statusLabel = new Label();
        private readonly FlowLayoutPanel _jobRowsPanel = new FlowLayoutPanel();
        private readonly Label _emptyStateLabel = new Label();
        private readonly JobCommandButton _filterButton = new JobCommandButton();
        private readonly MetricCard _totalMetric = new MetricCard("Total", UiTheme.Accent);
        private readonly MetricCard _readyMetric = new MetricCard("Siap", UiTheme.Success);
        private readonly MetricCard _pendingMetric = new MetricCard("Tertunda", JobVisuals.Warning);
        private readonly MetricCard _cancelledMetric = new MetricCard("Batal", JobVisuals.Danger);
        private readonly MetricCard _doneMetric = new MetricCard("Berhasil", JobVisuals.Done);
        private readonly List<PrintJob> _allJobs = new List<PrintJob>();
        private Control? _listPage;
        private JobDetailPage? _detailPage;
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000
        };

        private JobFilterState _filterState = JobFilterState.Default;
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

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.PageBackground;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            MinimumSize = new Size(980, 560);
            Size = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterParent;
            Text = "Daftar Tugas Cetak";

            BuildLayout();

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

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildToolbar(), 0, 1);
            root.Controls.Add(BuildJobTable(), 0, 2);
            root.Controls.Add(BuildFooter(), 0, 3);

            _listPage = root;
            Controls.Add(_listPage);
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(34, 24, 34, 20)
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.White
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var hero = new JobHeroIcon
            {
                Dock = DockStyle.Left,
                Size = new Size(104, 104),
                Margin = new Padding(0)
            };

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White,
                Padding = new Padding(0, 22, 0, 0)
            };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 28F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Daftar Tugas Cetak",
                TextAlign = ContentAlignment.MiddleLeft
            };

            var subtitle = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 12.5F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Pantau dan proses tugas cetak dari server",
                TextAlign = ContentAlignment.MiddleLeft
            };

            titleStack.Controls.Add(title, 0, 0);
            titleStack.Controls.Add(subtitle, 0, 1);

            content.Controls.Add(hero, 0, 0);
            content.Controls.Add(titleStack, 1, 0);
            header.Controls.Add(content);

            return header;
        }

        private Control BuildToolbar()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                Padding = new Padding(34, 18, 34, 34)
            };

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = UiTheme.PageBackground
            };

            var searchBox = BuildSearchBox();
            var metrics = new FlowLayoutPanel
            {
                BackColor = UiTheme.PageBackground,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            metrics.Controls.Add(_cancelledMetric);
            metrics.Controls.Add(_pendingMetric);
            metrics.Controls.Add(_doneMetric);
            metrics.Controls.Add(_readyMetric);
            metrics.Controls.Add(_totalMetric);

            _filterButton.Text = string.Empty;
            _filterButton.Glyph = JobGlyph.Filter;
            _filterButton.Filled = false;
            _filterButton.AccentColor = UiTheme.Text;
            _filterButton.Click += (_, _) => ShowFilterDialog();

            _refreshButton.Text = "Refresh";
            _refreshButton.Glyph = JobGlyph.Refresh;
            _refreshButton.Filled = false;
            _refreshButton.AccentColor = UiTheme.Accent;
            _refreshButton.Click += async (_, _) => await LoadJobsAsync();

            toolbar.Controls.Add(searchBox);
            toolbar.Controls.Add(_filterButton);
            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(metrics);
            toolbar.Resize += (_, _) => ArrangeToolbarControls(toolbar, searchBox, metrics);
            toolbar.HandleCreated += (_, _) => ArrangeToolbarControls(toolbar, searchBox, metrics);

            host.Controls.Add(toolbar);

            return host;
        }

        private void ArrangeToolbarControls(Panel toolbar, Control searchBox, FlowLayoutPanel metrics)
        {
            const int controlHeight = 44;
            const int gap = 10;
            const int filterWidth = 48;
            const int refreshWidth = 112;
            const int metricsWidth = 520;

            var top = Math.Max(0, (toolbar.ClientSize.Height - controlHeight) / 2);
            var metricsActualWidth = Math.Min(metricsWidth, Math.Max(0, toolbar.ClientSize.Width / 2 - gap));
            var rightEdge = toolbar.ClientSize.Width;

            metrics.SetBounds(
                Math.Max(0, rightEdge - metricsActualWidth),
                0,
                metricsActualWidth,
                toolbar.ClientSize.Height);

            var commandRight = Math.Max(0, metrics.Left - 28);
            var refreshLeft = Math.Max(0, commandRight - refreshWidth);
            var filterLeft = Math.Max(0, refreshLeft - gap - filterWidth);
            var searchWidth = Math.Max(180, filterLeft - gap);

            searchBox.SetBounds(0, top, searchWidth, controlHeight);
            _filterButton.SetBounds(filterLeft, top, filterWidth, controlHeight);
            _refreshButton.SetBounds(refreshLeft, top, refreshWidth, controlHeight);

            var showMetrics = toolbar.ClientSize.Width >= 850;
            metrics.Visible = showMetrics;
            if (!showMetrics)
            {
                var compactCommandRight = toolbar.ClientSize.Width;
                refreshLeft = Math.Max(0, compactCommandRight - refreshWidth);
                filterLeft = Math.Max(0, refreshLeft - gap - filterWidth);
                searchWidth = Math.Max(180, filterLeft - gap);

                searchBox.SetBounds(0, top, searchWidth, controlHeight);
                _filterButton.SetBounds(filterLeft, top, filterWidth, controlHeight);
                _refreshButton.SetBounds(refreshLeft, top, refreshWidth, controlHeight);
            }
        }

        private Control BuildSearchBox()
        {
            var searchHost = new RoundedPanel
            {
                FillColor = Color.White,
                BorderColor = Color.FromArgb(215, 219, 226),
                CornerRadius = 12,
                Padding = new Padding(46, 8, 12, 7)
            };

            var icon = new GlyphView
            {
                Glyph = JobGlyph.Search,
                GlyphColor = UiTheme.Text,
                Size = new Size(24, 24),
                Location = new Point(17, 10),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };

            _searchTextBox.BorderStyle = BorderStyle.None;
            _searchTextBox.Dock = DockStyle.Fill;
            _searchTextBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            _searchTextBox.ForeColor = UiTheme.Text;
            _searchTextBox.PlaceholderText = "Cari dokumen, alias, atau kertas";
            _searchTextBox.TextChanged += (_, _) => RenderFilteredJobs();

            searchHost.Controls.Add(_searchTextBox);
            searchHost.Controls.Add(icon);

            return searchHost;
        }

        private Control BuildJobTable()
        {
            var tableCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.White,
                BorderColor = UiTheme.Border,
                CornerRadius = 14,
                Padding = new Padding(16, 16, 16, 14),
                Margin = new Padding(34, 0, 34, 0)
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 2
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            table.Controls.Add(BuildTableHeader(), 0, 0);

            var bodyHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(0)
            };

            _jobRowsPanel.Dock = DockStyle.Fill;
            _jobRowsPanel.AutoScroll = true;
            _jobRowsPanel.BackColor = Color.White;
            _jobRowsPanel.FlowDirection = FlowDirection.TopDown;
            _jobRowsPanel.WrapContents = false;
            _jobRowsPanel.Padding = new Padding(0, 2, 0, 0);
            _jobRowsPanel.SizeChanged += (_, _) => ResizeRowsToPanel();

            _emptyStateLabel.Dock = DockStyle.Fill;
            _emptyStateLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            _emptyStateLabel.ForeColor = UiTheme.MutedText;
            _emptyStateLabel.Text = "Belum ada tugas cetak untuk sesi aktif.";
            _emptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyStateLabel.Visible = false;

            bodyHost.Controls.Add(_emptyStateLabel);
            bodyHost.Controls.Add(_jobRowsPanel);
            table.Controls.Add(bodyHost, 0, 1);

            tableCard.Controls.Add(table);
            return tableCard;
        }

        private Control BuildTableHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = JobColumnLayout.ColumnCount,
                RowCount = 1,
                Padding = new Padding(18, 0, 16, 0),
                Margin = new Padding(0)
            };
            JobColumnLayout.Apply(header);

            header.Controls.Add(CreateHeaderLabel("Dokumen", ContentAlignment.MiddleLeft), 0, 0);
            header.Controls.Add(CreateHeaderLabel("Alias", ContentAlignment.MiddleLeft), 1, 0);
            header.Controls.Add(CreateHeaderLabel("Kertas", ContentAlignment.MiddleLeft), 2, 0);
            header.Controls.Add(CreateHeaderLabel("Salinan", ContentAlignment.MiddleCenter), 3, 0);
            header.Controls.Add(CreateHeaderLabel("Status", ContentAlignment.MiddleLeft), 4, 0);
            header.Controls.Add(CreateHeaderLabel("Waktu", ContentAlignment.MiddleLeft), 5, 0);
            header.Controls.Add(CreateHeaderLabel("Aksi", ContentAlignment.MiddleLeft), 6, 0);

            return header;
        }

        private static Label CreateHeaderLabel(string text, ContentAlignment alignment)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = text,
                TextAlign = alignment
            };
        }

        private Control BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                Padding = new Padding(34, 0, 34, 12)
            };

            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground
            };

            var check = new GlyphView
            {
                Glyph = JobGlyph.CheckCircle,
                GlyphColor = UiTheme.Success,
                Size = new Size(30, 30),
                Location = new Point(0, 10)
            };

            _statusLabel.AutoEllipsis = true;
            _statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
            _statusLabel.ForeColor = UiTheme.MutedText;
            _statusLabel.Location = new Point(42, 11);
            _statusLabel.Size = new Size(900, 28);
            _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _statusLabel.Text = "Sinkronisasi aktif - tugas baru akan muncul otomatis";
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            content.Controls.Add(_statusLabel);
            content.Controls.Add(check);
            footer.Controls.Add(content);

            return footer;
        }

        private async Task LoadJobsAsync()
        {
            if (_isLoading)
            {
                _pendingRefresh = true;
                return;
            }

            _isLoading = true;
            SetBusy(true);

            try
            {
                var clientId = _getClientId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    _statusLabel.Text = "Client belum terhubung.";
                    _allJobs.Clear();
                    UpdateMetrics(_allJobs);
                    RenderFilteredJobs();
                    return;
                }

                _statusLabel.Text = "Memuat tugas cetak...";

                var encodedClientId = Uri.EscapeDataString(clientId);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_serverBaseUrl}/api/jobs?claimClientId={encodedClientId}&activeSessionOnly=true");
                using var response = await _sendAuthorizedAsync(request);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _statusLabel.Text = "Perlu login mitra untuk melihat job.";
                    _allJobs.Clear();
                    UpdateMetrics(_allJobs);
                    RenderFilteredJobs();
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _statusLabel.Text = "Gagal memuat job.";
                    return;
                }

                var body = await response.Content.ReadAsStringAsync();
                var jobs = DeserializeVisibleJobs(body);

                _allJobs.Clear();
                _allJobs.AddRange(jobs);
                UpdateMetrics(_allJobs);
                RenderFilteredJobs();
                _statusLabel.Text = $"{jobs.Count} tugas diterima";
            }
            catch
            {
                _statusLabel.Text = "Tidak bisa terhubung ke server.";
            }
            finally
            {
                _isLoading = false;
                SetBusy(false);

                if (_pendingRefresh)
                {
                    _pendingRefresh = false;
                    _ = LoadJobsAsync();
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
            _filterButton.Enabled = !busy;
            _searchTextBox.Enabled = !busy;

            foreach (Control control in _jobRowsPanel.Controls)
            {
                if (control is JobRowControl row)
                {
                    row.SetActionsEnabled(!busy);
                }
            }
        }

        private void ShowFilterDialog()
        {
            using var dialog = new JobFilterDialog(_filterState, _allJobs);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _filterState = dialog.SelectedFilter;
            UpdateFilterButton();
            RenderFilteredJobs();
        }

        private void UpdateFilterButton()
        {
            var activeCount = _filterState.ActiveFilterCount;
            _filterButton.Text = activeCount == 0 ? string.Empty : activeCount.ToString();
            _filterButton.AccentColor = activeCount == 0 ? UiTheme.Text : UiTheme.Accent;
            _filterButton.Invalidate();
        }

        private async Task HandlePrintActionAsync(PrintJob job)
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
            SetBusy(true);
            try
            {
                await _printJobAsync(job);
            }
            finally
            {
                SetBusy(false);
            }

            await LoadJobsAsync();
        }

        private async Task HandleRejectActionAsync(PrintJob job)
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
            SetBusy(true);
            try
            {
                await _rejectJobAsync(job);
            }
            finally
            {
                SetBusy(false);
            }

            await LoadJobsAsync();
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

        private void UpdateMetrics(IReadOnlyCollection<PrintJob> jobs)
        {
            _totalMetric.SetValue(jobs.Count.ToString());
            _readyMetric.SetValue(jobs.Count(job => JobVisuals.IsStatus(job.Status, "ready")).ToString());
            _pendingMetric.SetValue(jobs.Count(job => JobVisuals.IsStatus(job.Status, "pending")).ToString());
            _cancelledMetric.SetValue(jobs.Count(job => JobVisuals.IsStatus(job.Status, "rejected")).ToString());
            _doneMetric.SetValue(jobs.Count(job => JobVisuals.IsStatus(job.Status, "done")).ToString());
        }

        private void RenderFilteredJobs()
        {
            var query = (_searchTextBox.Text ?? string.Empty).Trim();
            var jobs = _allJobs
                .Where(job => string.IsNullOrWhiteSpace(query) || MatchesSearch(job, query))
                .Where(job => _filterState.Matches(job))
                .ToList();

            RenderJobRows(jobs);
            _statusLabel.Text = jobs.Count == _allJobs.Count
                ? $"{_allJobs.Count} tugas tersinkron - sesi aktif saja"
                : $"Menampilkan {jobs.Count} dari {_allJobs.Count} tugas - sesi aktif saja";
        }

        private static bool MatchesSearch(PrintJob job, string query)
        {
            return Contains(job.OriginalName, query)
                || Contains(job.Alias, query)
                || Contains(job.PrintConfig?.PaperSize, query)
                || Contains(JobVisuals.GetStatusLabel(job.Status), query);
        }

        private static bool Contains(string? source, string query)
        {
            return !string.IsNullOrWhiteSpace(source)
                && source.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void RenderJobRows(IReadOnlyCollection<PrintJob> jobs)
        {
            _jobRowsPanel.SuspendLayout();
            try
            {
                _jobRowsPanel.Controls.Clear();

                foreach (var job in jobs)
                {
                    var row = new JobRowControl(job);
                    row.DetailRequested += (_, _) => ShowJobDetail(job);
                    row.PrintRequested += async (_, _) => await HandlePrintActionAsync(job);
                    row.RejectRequested += async (_, _) => await HandleRejectActionAsync(job);
                    _jobRowsPanel.Controls.Add(row);
                }

                ResizeRowsToPanel();
            }
            finally
            {
                _jobRowsPanel.ResumeLayout();
            }

            _emptyStateLabel.Text = _allJobs.Count == 0
                ? "Belum ada tugas cetak untuk sesi aktif."
                : "Tidak ada tugas yang cocok dengan pencarian.";
            _emptyStateLabel.Visible = jobs.Count == 0;
            _jobRowsPanel.Visible = jobs.Count > 0;
        }

        private void ShowJobDetail(PrintJob job)
        {
            _detailPage?.Dispose();
            _detailPage = new JobDetailPage(
                job,
                HandlePrintActionAsync,
                HandleRejectActionAsync,
                ShowListPage);

            if (_listPage != null)
            {
                _listPage.Visible = false;
            }

            Controls.Add(_detailPage);
            _detailPage.BringToFront();
        }

        private void ShowListPage()
        {
            if (_detailPage != null)
            {
                Controls.Remove(_detailPage);
                _detailPage.Dispose();
                _detailPage = null;
            }

            if (_listPage != null)
            {
                _listPage.Visible = true;
                _listPage.BringToFront();
            }

            _ = LoadJobsAsync();
        }

        private void ResizeRowsToPanel()
        {
            var width = _jobRowsPanel.ClientSize.Width;
            if (_jobRowsPanel.VerticalScroll.Visible)
            {
                width -= SystemInformation.VerticalScrollBarWidth + 4;
            }

            width = Math.Max(640, width - 4);

            foreach (Control control in _jobRowsPanel.Controls)
            {
                control.Width = width;
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class JobDetailPage : Panel
    {
        private readonly PrintJob _job;
        private readonly Func<PrintJob, Task> _printActionAsync;
        private readonly Func<PrintJob, Task> _rejectActionAsync;
        private readonly Action _backAction;
        private readonly JobCommandButton _backButton = new JobCommandButton();
        private readonly JobCommandButton _printButton = new JobCommandButton();
        private readonly JobCommandButton _rejectButton = new JobCommandButton();

        public JobDetailPage(
            PrintJob job,
            Func<PrintJob, Task> printActionAsync,
            Func<PrintJob, Task> rejectActionAsync,
            Action backAction)
        {
            _job = job;
            _printActionAsync = printActionAsync;
            _rejectActionAsync = rejectActionAsync;
            _backAction = backAction;

            Dock = DockStyle.Fill;
            BackColor = UiTheme.PageBackground;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(42, 24, 42, 18)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 3,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

            var hero = new JobHeroIcon
            {
                Dock = DockStyle.Left,
                Size = new Size(104, 104),
                Margin = new Padding(0)
            };

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White,
                Padding = new Padding(0, 23, 0, 0)
            };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            titleStack.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Detail Tugas Cetak",
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            titleStack.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Lihat rincian tugas cetak dari server",
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);

            _backButton.Text = "Kembali";
            _backButton.Glyph = JobGlyph.ArrowLeft;
            _backButton.Filled = false;
            _backButton.AccentColor = UiTheme.Accent;
            _backButton.Size = new Size(150, 52);
            _backButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _backButton.Margin = new Padding(0, 30, 0, 0);
            _backButton.Click += (_, _) => _backAction();

            layout.Controls.Add(hero, 0, 0);
            layout.Controls.Add(titleStack, 1, 0);
            layout.Controls.Add(_backButton, 2, 0);
            header.Controls.Add(layout);

            return header;
        }

        private Control BuildContent()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                AutoScroll = true,
                Padding = new Padding(44, 0, 44, 14)
            };

            var layout = new TableLayoutPanel
            {
                BackColor = UiTheme.PageBackground,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };

            layout.Controls.Add(BuildDocumentSection(), 0, 0);
            layout.Controls.Add(BuildPrintConfigSection(), 0, 1);
            layout.Controls.Add(BuildBottomSections(), 0, 2);

            host.Controls.Add(layout);
            host.Resize += (_, _) => ArrangeDetailContent(host, layout);
            host.HandleCreated += (_, _) => ArrangeDetailContent(host, layout);
            return host;
        }

        private static void ArrangeDetailContent(Panel host, TableLayoutPanel layout)
        {
            const int minDocumentHeight = 258;
            const int minConfigHeight = 216;
            const int minBottomHeight = 230;
            const int minContentHeight = minDocumentHeight + minConfigHeight + minBottomHeight;

            var availableWidth = Math.Max(760, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            var availableHeight = Math.Max(minContentHeight, host.ClientSize.Height - host.Padding.Vertical - 4);

            var documentHeight = Math.Max(minDocumentHeight, (int)Math.Round(availableHeight * 0.37));
            var configHeight = Math.Max(minConfigHeight, (int)Math.Round(availableHeight * 0.30));
            var bottomHeight = Math.Max(minBottomHeight, availableHeight - documentHeight - configHeight);
            var totalHeight = documentHeight + configHeight + bottomHeight;

            layout.SuspendLayout();
            try
            {
                layout.SetBounds(host.Padding.Left, host.Padding.Top, availableWidth, totalHeight);
                layout.RowStyles.Clear();
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, documentHeight));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, configHeight));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, bottomHeight));
            }
            finally
            {
                layout.ResumeLayout();
            }
        }

        private Control BuildDocumentSection()
        {
            var body = BuildTwoColumnSection(
                BuildFieldGroup(
                    BuildDetailRow(DetailIconPresets.File(), "Nama Dokumen", _job.OriginalName),
                    BuildDetailRow(DetailIconPresets.Tag(), "Alias", EmptyDash(_job.Alias)),
                    BuildDetailRow(DetailIconPresets.Hash(), "ID Job", EmptyDash(_job.Id)),
                    BuildDetailRow(DetailIconPresets.FileSize(), "Ukuran File", "-")),
                BuildFieldGroup(
                    BuildDetailRow(DetailIconPresets.Status(), "Status", new StatusPill(_job.Status)
                    {
                        Size = new Size(110, 34),
                        Anchor = AnchorStyles.Left,
                        Margin = new Padding(0)
                    }),
                    BuildDetailRow(DetailIconPresets.Clock(), "Waktu Masuk", FormatTime(_job.CreatedAt))));

            return BuildSection("Informasi Dokumen", DetailIconPresets.SectionDocument(), body, new Padding(26, 20, 26, 22));
        }

        private Control BuildPrintConfigSection()
        {
            var config = _job.PrintConfig;
            var body = BuildTwoColumnSection(
                BuildFieldGroup(
                    BuildDetailRow(DetailIconPresets.Paper(), "Ukuran Kertas", EmptyDash(config?.PaperSize)),
                    BuildDetailRow(DetailIconPresets.Copies(), "Salinan", config == null ? "-" : Math.Max(1, config.Copies).ToString()),
                    BuildDetailRow(DetailIconPresets.ColorMode(), "Mode Warna", FormatColorMode(config?.ColorMode))),
                BuildFieldGroup(
                    BuildDetailRow(DetailIconPresets.Orientation(), "Orientasi", FormatOrientation(config?.Orientation)),
                    BuildDetailRow(DetailIconPresets.Pages(), "Rentang Halaman", EmptyDash(config?.PageRange, "Semua halaman")),
                    BuildDetailRow(DetailIconPresets.Scale(), "Skala", $"{ResolveScale(config)}%")));

            return BuildSection("Konfigurasi Cetak", DetailIconPresets.SectionSettings(), body, new Padding(26, 18, 26, 22));
        }

        private Control BuildBottomSections()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 2,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            layout.Controls.Add(BuildNotesSection(), 0, 0);
            layout.Controls.Add(BuildSummarySection(), 1, 0);

            return layout;
        }

        private Control BuildNotesSection()
        {
            var noteBox = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.FromArgb(250, 251, 253),
                BorderColor = UiTheme.Border,
                CornerRadius = 10,
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 4, 10, 0)
            };

            noteBox.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(55, 65, 81),
                Text = EmptyDash(_job.PrintConfig?.Notes),
                TextAlign = ContentAlignment.TopLeft
            });

            return BuildSection("Catatan", DetailIconPresets.SectionNotes(), noteBox, new Padding(26, 18, 26, 18));
        }

        private Control BuildSummarySection()
        {
            var body = BuildFieldGroup(
                BuildDetailRow(DetailIconPresets.Price(), "Estimasi Harga", "-"),
                BuildDetailRow(DetailIconPresets.Storage(), "Status File", new StatusPill("available")
                {
                    Size = new Size(106, 34),
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0)
                }));

            var section = BuildSection("Ringkasan Tambahan", DetailIconPresets.SectionSummary(), body, new Padding(26, 18, 26, 18));
            section.Margin = new Padding(10, 0, 0, 0);
            return section;
        }

        private RoundedPanel BuildSection(string title, DetailIconPreset icon, Control body, Padding padding)
        {
            var section = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                FillColor = Color.White,
                BorderColor = UiTheme.Border,
                CornerRadius = 12,
                Padding = padding,
                Margin = new Padding(0, 0, 0, 14)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(BuildSectionTitle(title, icon), 0, 0);
            layout.Controls.Add(body, 0, 1);
            section.Controls.Add(layout);

            return section;
        }

        private static Control BuildSectionTitle(string title, DetailIconPreset icon)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            panel.Controls.Add(new DetailIconView(icon)
            {
                Size = new Size(28, 28),
                Location = new Point(0, 7),
                IconColor = UiTheme.Accent
            });

            panel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 12.2F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Location = new Point(42, 4),
                Size = new Size(360, 34),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            });

            return panel;
        }

        private static Control BuildTwoColumnSection(Control left, Control right)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 2,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            left.Margin = new Padding(0, 0, 18, 0);
            right.Margin = new Padding(18, 0, 0, 0);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(right, 1, 0);
            return layout;
        }

        private static Control BuildFieldGroup(params Control[] rows)
        {
            var group = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = rows.Length
            };

            foreach (var row in rows)
            {
                group.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                group.Controls.Add(row, 0, group.Controls.Count);
            }

            return group;
        }

        private static Control BuildDetailRow(DetailIconPreset icon, string label, string value)
        {
            return BuildDetailRow(icon, label, BuildValueLabel(value));
        }

        private static Control BuildDetailRow(DetailIconPreset icon, string label, Control value, string? displayOverride = null)
        {
            if (value is StatusPill && !string.IsNullOrWhiteSpace(displayOverride))
            {
                value = new StatusPill(displayOverride)
                {
                    Size = value.Size,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0)
                };
            }

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            row.Controls.Add(new DetailIconView(icon)
            {
                Dock = DockStyle.Fill,
                IconColor = Color.FromArgb(65, 76, 96),
                Margin = new Padding(0, 7, 10, 7)
            }, 0, 0);

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Regular),
                ForeColor = Color.FromArgb(62, 72, 92),
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft
            }, 1, 0);

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Regular),
                ForeColor = Color.FromArgb(62, 72, 92),
                Text = ":",
                TextAlign = ContentAlignment.MiddleCenter
            }, 2, 0);

            value.Dock = value is StatusPill ? DockStyle.None : DockStyle.Fill;
            row.Controls.Add(value, 3, 0);

            return row;
        }

        private static Label BuildValueLabel(string value)
        {
            return new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Regular),
                ForeColor = Color.FromArgb(46, 56, 76),
                Text = value,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Control BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(44, 16, 44, 16)
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border, 1F);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            _rejectButton.Text = "Hapus";
            _rejectButton.Filled = false;
            _rejectButton.AccentColor = UiTheme.Accent;
            _rejectButton.Enabled = JobVisuals.IsStatus(_job.Status, "ready");
            _rejectButton.Click += async (_, _) => await RunActionAsync(_rejectActionAsync);

            _printButton.Text = JobVisuals.IsStatus(_job.Status, "pending") ? "Retry" : "Cetak";
            _printButton.Filled = JobVisuals.IsStatus(_job.Status, "ready");
            _printButton.AccentColor = UiTheme.Accent;
            _printButton.Enabled = JobVisuals.IsStatus(_job.Status, "ready") || JobVisuals.IsStatus(_job.Status, "pending");
            _printButton.Click += async (_, _) => await RunActionAsync(_printActionAsync);

            footer.Controls.Add(_rejectButton);
            footer.Controls.Add(_printButton);
            footer.Resize += (_, _) => ArrangeFooterButtons(footer);
            footer.HandleCreated += (_, _) => ArrangeFooterButtons(footer);

            return footer;
        }

        private void ArrangeFooterButtons(Panel footer)
        {
            const int buttonWidth = 210;
            const int buttonHeight = 54;
            const int gap = 24;

            var top = Math.Max(0, (footer.ClientSize.Height - buttonHeight) / 2);
            var printLeft = footer.ClientSize.Width - buttonWidth;
            var rejectLeft = printLeft - gap - buttonWidth;

            _rejectButton.SetBounds(Math.Max(0, rejectLeft), top, buttonWidth, buttonHeight);
            _printButton.SetBounds(Math.Max(0, printLeft), top, buttonWidth, buttonHeight);
        }

        private async Task RunActionAsync(Func<PrintJob, Task> action)
        {
            SetBusy(true);
            try
            {
                await action(_job);
                _backAction();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _backButton.Enabled = !busy;
            _rejectButton.Enabled = !busy && JobVisuals.IsStatus(_job.Status, "ready");
            _printButton.Enabled = !busy && (JobVisuals.IsStatus(_job.Status, "ready") || JobVisuals.IsStatus(_job.Status, "pending"));
        }

        private static string EmptyDash(string? value, string fallback = "-")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string FormatTime(string? value)
        {
            if (DateTime.TryParse(value, out var dt))
            {
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }

            return EmptyDash(value);
        }

        private static string FormatColorMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "bw" or "blackwhite" or "black_white" or "grayscale" => "Hitam Putih",
                "color" or "colour" => "Warna",
                _ => value
            };
        }

        private static string FormatOrientation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "portrait" => "Portrait",
                "landscape" => "Landscape",
                _ => value
            };
        }

        private static int ResolveScale(PrintConfig? config)
        {
            if (config == null || config.ContentScale <= 0)
            {
                return 100;
            }

            return config.ContentScale;
        }
    }

    internal readonly record struct DetailIconPreset(string[] Names, IconKind FallbackIcon);

    internal static class DetailIconPresets
    {
        public static DetailIconPreset SectionDocument() => new(new[] { "file-text", "file-up", "lucide-file-text" }, IconKind.Document);
        public static DetailIconPreset SectionSettings() => new(new[] { "settings", "lucide-settings" }, IconKind.Settings);
        public static DetailIconPreset SectionNotes() => new(new[] { "notebook-text", "sticky-note", "lucide-notebook-text" }, IconKind.Document);
        public static DetailIconPreset SectionSummary() => new(new[] { "chart-column", "bar-chart-3", "lucide-chart-column" }, IconKind.Bars);
        public static DetailIconPreset File() => new(new[] { "file", "file-text", "lucide-file" }, IconKind.Document);
        public static DetailIconPreset Tag() => new(new[] { "tag", "lucide-tag" }, IconKind.Document);
        public static DetailIconPreset Hash() => new(new[] { "hash", "lucide-hash" }, IconKind.Bars);
        public static DetailIconPreset FileSize() => new(new[] { "file-chart-column", "file-box", "lucide-file-box" }, IconKind.Document);
        public static DetailIconPreset Status() => new(new[] { "gauge", "compass", "lucide-gauge" }, IconKind.Bars);
        public static DetailIconPreset Clock() => new(new[] { "clock", "lucide-clock" }, IconKind.Bars);
        public static DetailIconPreset Paper() => new(new[] { "file", "sheet", "lucide-file" }, IconKind.Document);
        public static DetailIconPreset Copies() => new(new[] { "copy", "files", "lucide-copy" }, IconKind.Document);
        public static DetailIconPreset ColorMode() => new(new[] { "palette", "lucide-palette" }, IconKind.Settings);
        public static DetailIconPreset Orientation() => new(new[] { "file-cog", "rotate-cw-square", "lucide-file-cog" }, IconKind.Document);
        public static DetailIconPreset Pages() => new(new[] { "file-stack", "files", "lucide-files" }, IconKind.Document);
        public static DetailIconPreset Scale() => new(new[] { "scan", "maximize", "lucide-scan" }, IconKind.Settings);
        public static DetailIconPreset Price() => new(new[] { "circle-dollar-sign", "badge-dollar-sign", "lucide-circle-dollar-sign" }, IconKind.Bars);
        public static DetailIconPreset Storage() => new(new[] { "hard-drive", "archive", "lucide-hard-drive" }, IconKind.Server);
    }

    internal sealed class DetailIconView : Control
    {
        private readonly DetailIconPreset _preset;

        public Color IconColor { get; set; } = Color.FromArgb(65, 76, 96);

        public DetailIconView(DetailIconPreset preset)
        {
            _preset = preset;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = Rectangle.Inflate(ClientRectangle, -2, -2);

            if (JobLucideAssets.TryDrawNamed(e.Graphics, _preset.Names, rect, IconColor, tint: true))
            {
                return;
            }

            IconAssets.DrawIcon(e.Graphics, _preset.FallbackIcon, CenterSquare(rect), IconColor, 2F);
        }

        private static Rectangle CenterSquare(Rectangle bounds)
        {
            var size = Math.Min(bounds.Width, bounds.Height);
            return new Rectangle(
                bounds.X + (bounds.Width - size) / 2,
                bounds.Y + (bounds.Height - size) / 2,
                size,
                size);
        }
    }

    internal sealed class JobRowControl : RoundedPanel
    {
        private readonly JobCommandButton _printButton = new JobCommandButton();
        private readonly JobCommandButton _rejectButton = new JobCommandButton();
        private readonly bool _canPrint;
        private readonly bool _canReject;

        public event EventHandler? DetailRequested;
        public event EventHandler? PrintRequested;
        public event EventHandler? RejectRequested;

        public JobRowControl(PrintJob job)
        {
            CornerRadius = 10;
            FillColor = Color.White;
            BorderColor = Color.FromArgb(232, 235, 240);
            Height = 78;
            Margin = new Padding(0, 0, 0, 8);
            Padding = new Padding(1);

            _canPrint = JobVisuals.IsStatus(job.Status, "ready") || JobVisuals.IsStatus(job.Status, "pending");
            _canReject = JobVisuals.IsStatus(job.Status, "ready");

            BuildLayout(job);
        }

        public void SetActionsEnabled(bool enabled)
        {
            _printButton.Enabled = enabled && _canPrint;
            _rejectButton.Enabled = enabled && _canReject;
        }

        private void BuildLayout(PrintJob job)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = JobColumnLayout.ColumnCount,
                RowCount = 1,
                Padding = new Padding(18, 8, 16, 8),
                Margin = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            JobColumnLayout.Apply(layout);

            layout.Controls.Add(BuildDocumentCell(job), 0, 0);
            layout.Controls.Add(CreateCellLabel(string.IsNullOrWhiteSpace(job.Alias) ? "-" : job.Alias, ContentAlignment.MiddleLeft), 1, 0);
            layout.Controls.Add(CreateCellLabel(job.PrintConfig?.PaperSize ?? "-", ContentAlignment.MiddleLeft), 2, 0);
            layout.Controls.Add(CreateCellLabel(ResolveCopies(job), ContentAlignment.MiddleCenter), 3, 0);
            layout.Controls.Add(BuildStatusCell(job.Status), 4, 0);
            layout.Controls.Add(CreateCellLabel(FormatTime(job.CreatedAt), ContentAlignment.MiddleLeft), 5, 0);
            layout.Controls.Add(BuildActionCell(job), 6, 0);

            Controls.Add(layout);
            WireDetailOpen(this);
        }

        private Control BuildDocumentCell(PrintJob job)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var icon = new DocumentTypeIcon
            {
                Extension = Path.GetExtension(job.OriginalName),
                Size = new Size(38, 44),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 14, 0)
            };

            var label = CreateCellLabel(job.OriginalName, ContentAlignment.MiddleLeft);

            panel.Controls.Add(icon, 0, 0);
            panel.Controls.Add(label, 1, 0);

            return panel;
        }

        private static Control BuildStatusCell(string? status)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var pill = new StatusPill(status)
            {
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
                Size = new Size(110, 34)
            };
            panel.Controls.Add(pill, 0, 1);

            return panel;
        }

        private Control BuildActionCell(PrintJob job)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 3,
                RowCount = 3,
                Margin = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            _rejectButton.Text = "Tolak";
            _rejectButton.Dock = DockStyle.Fill;
            _rejectButton.Margin = new Padding(0, 0, 6, 0);
            _rejectButton.Filled = false;
            _rejectButton.AccentColor = UiTheme.Accent;
            _rejectButton.Enabled = _canReject;
            _rejectButton.Click += (_, _) =>
            {
                if (_rejectButton.Enabled)
                {
                    RejectRequested?.Invoke(this, EventArgs.Empty);
                }
            };

            _printButton.Text = JobVisuals.IsStatus(job.Status, "pending") ? "Retry" : "Cetak";
            _printButton.Dock = DockStyle.Fill;
            _printButton.Margin = new Padding(0);
            _printButton.Filled = JobVisuals.IsStatus(job.Status, "ready");
            _printButton.AccentColor = UiTheme.Accent;
            _printButton.Enabled = _canPrint;
            _printButton.Click += (_, _) =>
            {
                if (_printButton.Enabled)
                {
                    PrintRequested?.Invoke(this, EventArgs.Empty);
                }
            };

            panel.Controls.Add(_rejectButton, 1, 1);
            panel.Controls.Add(_printButton, 2, 1);

            return panel;
        }

        private void WireDetailOpen(Control control)
        {
            if (control is JobCommandButton)
            {
                return;
            }

            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => DetailRequested?.Invoke(this, EventArgs.Empty);

            foreach (Control child in control.Controls)
            {
                WireDetailOpen(child);
            }
        }

        private static Label CreateCellLabel(string? text, ContentAlignment alignment)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.2F, FontStyle.Regular),
                ForeColor = Color.FromArgb(74, 82, 96),
                Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
                TextAlign = alignment,
                Margin = new Padding(0)
            };
        }

        private static string ResolveCopies(PrintJob job)
        {
            return job.PrintConfig == null ? "-" : Math.Max(1, job.PrintConfig.Copies).ToString();
        }

        private static string FormatTime(string? value)
        {
            if (DateTime.TryParse(value, out var dt))
            {
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }

            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }

    internal sealed class MetricCard : RoundedPanel
    {
        private readonly Label _valueLabel;
        private readonly Label? _suffixLabel;

        public MetricCard(string caption, Color accentColor, string? suffix = null)
        {
            Size = new Size(92, 58);
            Margin = new Padding(7, 0, 0, 0);
            FillColor = Color.White;
            BorderColor = Color.FromArgb(
                Math.Min(255, accentColor.R + 70),
                Math.Min(255, accentColor.G + 70),
                Math.Min(255, accentColor.B + 70));
            CornerRadius = 10;
            Padding = new Padding(8, 4, 8, 4);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = string.IsNullOrWhiteSpace(suffix) ? 2 : 3,
                Margin = new Padding(0)
            };
            if (string.IsNullOrWhiteSpace(suffix))
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            }
            else
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            var captionLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = accentColor == UiTheme.Accent ? UiTheme.MutedText : accentColor,
                Text = caption,
                TextAlign = ContentAlignment.BottomCenter,
                Margin = new Padding(0)
            };

            _valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", string.IsNullOrWhiteSpace(suffix) ? 15F : 14F, FontStyle.Bold),
                ForeColor = accentColor,
                Text = "0",
                TextAlign = string.IsNullOrWhiteSpace(suffix) ? ContentAlignment.TopCenter : ContentAlignment.MiddleCenter,
                Margin = new Padding(0)
            };

            layout.Controls.Add(captionLabel, 0, 0);
            layout.Controls.Add(_valueLabel, 0, 1);

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                _suffixLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    Font = new Font("Segoe UI", 8.2F, FontStyle.Regular),
                    ForeColor = UiTheme.MutedText,
                    Text = suffix,
                    TextAlign = ContentAlignment.TopCenter,
                    Margin = new Padding(0)
                };
                layout.Controls.Add(_suffixLabel, 0, 2);
            }

            Controls.Add(layout);
        }

        public void SetValue(string value)
        {
            _valueLabel.Text = value;
        }
    }

    internal sealed class JobFilterDialog : Form
    {
        private readonly ComboBox _statusCombo = new ComboBox();
        private readonly ComboBox _documentTypeCombo = new ComboBox();
        private readonly ComboBox _paperCombo = new ComboBox();
        private readonly CheckBox _actionableOnlyCheckBox = new CheckBox();

        public JobFilterState SelectedFilter { get; private set; }

        public JobFilterDialog(JobFilterState currentFilter, IReadOnlyCollection<PrintJob> jobs)
        {
            SelectedFilter = currentFilter;

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.PageBackground;
            ClientSize = new Size(460, 348);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Filter Tugas Cetak";

            BuildLayout(jobs, currentFilter);
        }

        private void BuildLayout(IReadOnlyCollection<PrintJob> jobs, JobFilterState currentFilter)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(22, 18, 22, 18)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = "Filter Tugas Cetak",
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(title, 0, 0);
            root.SetColumnSpan(title, 2);

            ConfigureCombo(_statusCombo);
            AddOption(_statusCombo, "Semua status", JobFilterState.All);
            AddOption(_statusCombo, "Siap", "ready");
            AddOption(_statusCombo, "Tertunda", "pending");
            AddOption(_statusCombo, "Gagal", "failed");
            AddOption(_statusCombo, "Mencetak", "printing");
            AddOption(_statusCombo, "Berhasil", "done");
            AddOption(_statusCombo, "Batal", "rejected");

            ConfigureCombo(_documentTypeCombo);
            AddOption(_documentTypeCombo, "Semua dokumen", JobFilterState.All);
            AddOption(_documentTypeCombo, "PDF", JobFilterState.DocumentPdf);
            AddOption(_documentTypeCombo, "Gambar", JobFilterState.DocumentImage);
            AddOption(_documentTypeCombo, "Lainnya", JobFilterState.DocumentOther);

            ConfigureCombo(_paperCombo);
            AddOption(_paperCombo, "Semua kertas", JobFilterState.All);
            foreach (var paperSize in GetPaperSizeOptions(jobs, currentFilter.PaperSize))
            {
                AddOption(_paperCombo, paperSize, paperSize);
            }

            root.Controls.Add(CreateFieldLabel("Status"), 0, 1);
            root.Controls.Add(_statusCombo, 1, 1);
            root.Controls.Add(CreateFieldLabel("Dokumen"), 0, 2);
            root.Controls.Add(_documentTypeCombo, 1, 2);
            root.Controls.Add(CreateFieldLabel("Kertas"), 0, 3);
            root.Controls.Add(_paperCombo, 1, 3);

            _actionableOnlyCheckBox.Dock = DockStyle.Fill;
            _actionableOnlyCheckBox.ForeColor = UiTheme.Text;
            _actionableOnlyCheckBox.Text = "Hanya tampilkan tugas yang bisa diproses";
            _actionableOnlyCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_actionableOnlyCheckBox, 1, 4);

            var hint = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.2F, FontStyle.Regular),
                ForeColor = UiTheme.MutedText,
                Text = "Filter ini diterapkan ke daftar sesi aktif yang sudah dimuat.",
                TextAlign = ContentAlignment.TopLeft
            };
            root.Controls.Add(hint, 0, 5);
            root.SetColumnSpan(hint, 2);

            var buttonBar = BuildButtonBar();
            root.Controls.Add(buttonBar, 0, 6);
            root.SetColumnSpan(buttonBar, 2);

            Controls.Add(root);
            ApplyState(currentFilter);
        }

        private Control BuildButtonBar()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PageBackground,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            var applyButton = CreateDialogButton("Terapkan", UiTheme.Accent, true);
            applyButton.Click += (_, _) =>
            {
                SelectedFilter = new JobFilterState(
                    GetSelectedValue(_statusCombo),
                    GetSelectedValue(_documentTypeCombo),
                    GetSelectedValue(_paperCombo),
                    _actionableOnlyCheckBox.Checked);
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancelButton = CreateDialogButton("Batal", UiTheme.MutedText, false);
            cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var resetButton = CreateDialogButton("Reset", UiTheme.Accent, false);
            resetButton.Click += (_, _) => ApplyState(JobFilterState.Default);

            panel.Controls.Add(applyButton);
            panel.Controls.Add(cancelButton);
            panel.Controls.Add(resetButton);

            return panel;
        }

        private static JobCommandButton CreateDialogButton(string text, Color accentColor, bool filled)
        {
            return new JobCommandButton
            {
                Text = text,
                AccentColor = accentColor,
                Filled = filled,
                Size = new Size(104, 38),
                Margin = new Padding(8, 0, 0, 0)
            };
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void ConfigureCombo(ComboBox combo)
        {
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Font = new Font("Segoe UI", 10.2F, FontStyle.Regular);
            combo.Margin = new Padding(0, 7, 0, 7);
        }

        private static void AddOption(ComboBox combo, string label, string value)
        {
            combo.Items.Add(new JobFilterOption(label, value));
        }

        private static IReadOnlyList<string> GetPaperSizeOptions(IReadOnlyCollection<PrintJob> jobs, string selectedPaperSize)
        {
            var values = jobs
                .Select(job => job.PrintConfig?.PaperSize?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList();

            if (!string.Equals(selectedPaperSize, JobFilterState.All, StringComparison.OrdinalIgnoreCase)
                && !values.Contains(selectedPaperSize, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(selectedPaperSize);
            }

            return values;
        }

        private void ApplyState(JobFilterState state)
        {
            SelectValue(_statusCombo, state.Status);
            SelectValue(_documentTypeCombo, state.DocumentType);
            SelectValue(_paperCombo, state.PaperSize);
            _actionableOnlyCheckBox.Checked = state.ActionableOnly;
        }

        private static void SelectValue(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (item is JobFilterOption option
                    && string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static string GetSelectedValue(ComboBox combo)
        {
            return combo.SelectedItem is JobFilterOption option
                ? option.Value
                : JobFilterState.All;
        }
    }

    internal sealed class JobFilterOption
    {
        public JobFilterOption(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public string Value { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal sealed class JobFilterState
    {
        public const string All = "all";
        public const string DocumentPdf = "pdf";
        public const string DocumentImage = "image";
        public const string DocumentOther = "other";

        public static readonly JobFilterState Default = new JobFilterState(All, All, All, false);

        public JobFilterState(
            string status,
            string documentType,
            string paperSize,
            bool actionableOnly)
        {
            Status = Normalize(status);
            DocumentType = Normalize(documentType);
            PaperSize = string.IsNullOrWhiteSpace(paperSize) ? All : paperSize;
            ActionableOnly = actionableOnly;
        }

        public string Status { get; }
        public string DocumentType { get; }
        public string PaperSize { get; }
        public bool ActionableOnly { get; }

        public int ActiveFilterCount
        {
            get
            {
                var count = 0;
                if (!IsAll(Status)) count++;
                if (!IsAll(DocumentType)) count++;
                if (!IsAll(PaperSize)) count++;
                if (ActionableOnly) count++;
                return count;
            }
        }

        public bool Matches(PrintJob job)
        {
            if (!IsAll(Status) && !JobVisuals.IsStatus(job.Status, Status))
            {
                return false;
            }

            if (!IsAll(DocumentType) && !string.Equals(GetDocumentType(job), DocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsAll(PaperSize)
                && !string.Equals(job.PrintConfig?.PaperSize?.Trim(), PaperSize, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (ActionableOnly
                && !JobVisuals.IsStatus(job.Status, "ready")
                && !JobVisuals.IsStatus(job.Status, "pending"))
            {
                return false;
            }

            return true;
        }

        private static string GetDocumentType(PrintJob job)
        {
            var extension = Path.GetExtension(job.OriginalName)?.TrimStart('.').ToLowerInvariant();
            return extension switch
            {
                "pdf" => DocumentPdf,
                "jpg" or "jpeg" or "png" or "bmp" => DocumentImage,
                _ => DocumentOther
            };
        }

        private static string Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? All : value.Trim();
        }

        private static bool IsAll(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, All, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class StatusPill : Control
    {
        private readonly string _text;
        private readonly Color _accent;
        private readonly Color _fill;
        private readonly Color _border;

        public StatusPill(string? status)
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            var style = JobVisuals.GetStatusStyle(status);
            _text = style.Label;
            _accent = style.Accent;
            _fill = style.Fill;
            _border = style.Border;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var path = UiDrawing.CreateRoundedRectangle(rect, 8);
            using var fillBrush = new SolidBrush(_fill);
            using var borderPen = new Pen(_border, 1F);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            using var dotBrush = new SolidBrush(_accent);
            e.Graphics.FillEllipse(dotBrush, 12, rect.Height / 2 - 4, 8, 8);

            TextRenderer.DrawText(
                e.Graphics,
                _text,
                Font,
                new Rectangle(28, 0, rect.Width - 32, rect.Height),
                _accent,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class JobCommandButton : Control
    {
        private bool _isHovered;
        private bool _isPressed;

        public bool Filled { get; set; }
        public Color AccentColor { get; set; } = UiTheme.Accent;
        public JobGlyph Glyph { get; set; } = JobGlyph.None;
        public int CornerRadius { get; set; } = 10;

        public JobCommandButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 10.2F, FontStyle.Bold);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var backgroundBrush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, UiTheme.PageBackground));
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            var fill = ResolveFillColor();
            var border = Enabled ? AccentColor : UiTheme.Border;
            var content = ResolveContentColor();

            using var path = UiDrawing.CreateRoundedRectangle(rect, CornerRadius);
            using var fillBrush = new SolidBrush(fill);
            e.Graphics.FillPath(fillBrush, path);

            if (!Filled || !Enabled)
            {
                using var pen = new Pen(border, 1.15F);
                e.Graphics.DrawPath(pen, path);
            }

            DrawContent(e.Graphics, rect, content);
        }

        private Color ResolveFillColor()
        {
            if (!Enabled)
            {
                return UiTheme.DisabledBackground;
            }

            if (Filled)
            {
                if (_isPressed)
                {
                    return ControlPaint.Dark(AccentColor, 0.12F);
                }

                if (_isHovered)
                {
                    return ControlPaint.Light(AccentColor, 0.08F);
                }

                return AccentColor;
            }

            if (_isPressed)
            {
                return Color.FromArgb(255, 246, 242);
            }

            if (_isHovered)
            {
                return Color.FromArgb(255, 251, 249);
            }

            return Color.White;
        }

        private Color ResolveContentColor()
        {
            if (!Enabled)
            {
                return UiTheme.DisabledText;
            }

            return Filled ? Color.White : AccentColor;
        }

        private void DrawContent(Graphics graphics, Rectangle rect, Color contentColor)
        {
            var text = Text ?? string.Empty;
            var hasIcon = Glyph != JobGlyph.None;
            var hasText = !string.IsNullOrWhiteSpace(text);
            var iconSize = hasIcon ? 20 : 0;
            var gap = hasIcon && hasText ? 8 : 0;

            var textSize = hasText
                ? TextRenderer.MeasureText(
                    graphics,
                    text,
                    Font,
                    new Size(rect.Width, rect.Height),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine)
                : Size.Empty;

            var totalWidth = iconSize + gap + textSize.Width;
            var startX = rect.X + Math.Max(0, (rect.Width - totalWidth) / 2);
            var centerY = rect.Y + rect.Height / 2;

            if (hasIcon)
            {
                var iconRect = new Rectangle(startX, centerY - iconSize / 2, iconSize, iconSize);
                JobGlyphPainter.Draw(graphics, Glyph, iconRect, contentColor, 2F);
                startX += iconSize + gap;
            }

            if (!hasText)
            {
                return;
            }

            var textRect = new Rectangle(
                startX,
                rect.Y,
                Math.Min(textSize.Width + 8, rect.Right - startX),
                rect.Height);

            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                textRect,
                contentColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _isPressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isPressed)
            {
                _isPressed = false;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }
    }

    internal sealed class GlyphView : Control
    {
        public JobGlyph Glyph { get; set; }
        public Color GlyphColor { get; set; } = UiTheme.Text;

        public GlyphView()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, UiTheme.PageBackground));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            JobGlyphPainter.Draw(e.Graphics, Glyph, new Rectangle(2, 2, Width - 4, Height - 4), GlyphColor, 2.2F);
        }
    }

    internal sealed class JobHeroIcon : Control
    {
        public JobHeroIcon()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (JobLucideAssets.TryDrawNamed(
                    e.Graphics,
                    new[] { "file-up", "lucide-file-up", "file-arrow-up" },
                    new Rectangle(8, 4, 96, 96),
                    UiTheme.Text,
                    tint: false))
            {
                return;
            }

            var docRect = new Rectangle(10, 8, 68, 88);
            using var docPath = BuildDocumentPath(docRect, 20);
            using var shadowBrush = new SolidBrush(Color.FromArgb(24, 15, 23, 42));
            using var fillBrush = new SolidBrush(Color.White);
            using var borderPen = new Pen(UiTheme.Text, 3F)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            e.Graphics.TranslateTransform(0, 2);
            e.Graphics.FillPath(shadowBrush, docPath);
            e.Graphics.TranslateTransform(0, -2);
            e.Graphics.FillPath(fillBrush, docPath);
            e.Graphics.DrawPath(borderPen, docPath);

            var fold = new[]
            {
                new Point(docRect.Right - 20, docRect.Top + 2),
                new Point(docRect.Right + 1, docRect.Top + 22),
                new Point(docRect.Right - 20, docRect.Top + 22)
            };
            using var foldBrush = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillPolygon(foldBrush, fold);

            using var linePen = new Pen(UiTheme.Text, 3.2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(linePen, docRect.Left + 18, docRect.Top + 38, docRect.Left + 48, docRect.Top + 38);
            e.Graphics.DrawLine(linePen, docRect.Left + 18, docRect.Top + 54, docRect.Left + 50, docRect.Top + 54);
            e.Graphics.DrawLine(linePen, docRect.Left + 18, docRect.Top + 70, docRect.Left + 44, docRect.Top + 70);

            var circle = new Rectangle(60, 58, 44, 44);
            using var accentBrush = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillEllipse(accentBrush, circle);
            using var arrowPen = new Pen(Color.White, 4F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLine(arrowPen, circle.Left + 22, circle.Bottom - 12, circle.Left + 22, circle.Top + 13);
            e.Graphics.DrawLine(arrowPen, circle.Left + 12, circle.Top + 23, circle.Left + 22, circle.Top + 13);
            e.Graphics.DrawLine(arrowPen, circle.Left + 32, circle.Top + 23, circle.Left + 22, circle.Top + 13);
        }

        private static GraphicsPath BuildDocumentPath(Rectangle rect, int fold)
        {
            var path = new GraphicsPath();
            path.AddLine(rect.Left, rect.Top, rect.Right - fold, rect.Top);
            path.AddLine(rect.Right - fold, rect.Top, rect.Right, rect.Top + fold);
            path.AddLine(rect.Right, rect.Top + fold, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.AddLine(rect.Left, rect.Bottom, rect.Left, rect.Top);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class DocumentTypeIcon : Control
    {
        public string? Extension { get; set; }

        public DocumentTypeIcon()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var ext = (Extension ?? string.Empty).TrimStart('.').ToUpperInvariant();
            var isImage = ext is "PNG" or "JPG" or "JPEG" or "BMP";
            var accent = isImage ? UiTheme.Success : JobVisuals.Danger;
            var label = string.IsNullOrWhiteSpace(ext) ? "FILE" : (isImage ? "IMG" : ext.Length > 3 ? ext[..3] : ext);

            var rect = new Rectangle(4, 2, Width - 9, Height - 5);
            var fold = 9;
            using var path = new GraphicsPath();
            path.AddLine(rect.Left, rect.Top, rect.Right - fold, rect.Top);
            path.AddLine(rect.Right - fold, rect.Top, rect.Right, rect.Top + fold);
            path.AddLine(rect.Right, rect.Top + fold, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.AddLine(rect.Left, rect.Bottom, rect.Left, rect.Top);
            path.CloseFigure();

            using var pen = new Pen(accent, 1.8F)
            {
                LineJoin = LineJoin.Round
            };
            using var fillBrush = new SolidBrush(Color.White);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(pen, path);

            using var foldBrush = new SolidBrush(Color.FromArgb(238, accent));
            e.Graphics.FillPolygon(
                foldBrush,
                new[]
                {
                    new Point(rect.Right - fold, rect.Top + 1),
                    new Point(rect.Right - 1, rect.Top + fold),
                    new Point(rect.Right - fold, rect.Top + fold)
                });

            if (isImage)
            {
                using var iconBrush = new SolidBrush(accent);
                e.Graphics.FillPolygon(
                    iconBrush,
                    new[]
                    {
                        new Point(rect.Left + 6, rect.Bottom - 9),
                        new Point(rect.Left + 14, rect.Bottom - 18),
                        new Point(rect.Left + 20, rect.Bottom - 11),
                        new Point(rect.Left + 25, rect.Bottom - 17),
                        new Point(rect.Right - 5, rect.Bottom - 9)
                    });
                e.Graphics.FillEllipse(iconBrush, rect.Left + 8, rect.Top + 12, 5, 5);
                return;
            }

            TextRenderer.DrawText(
                e.Graphics,
                label,
                new Font("Segoe UI", 6.4F, FontStyle.Bold),
                new Rectangle(rect.Left + 2, rect.Bottom - 17, rect.Width - 4, 14),
                accent,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);
        }
    }

    internal enum JobGlyph
    {
        None,
        Search,
        Filter,
        Refresh,
        CheckCircle,
        ArrowLeft
    }

    internal static class JobGlyphPainter
    {
        public static void Draw(Graphics graphics, JobGlyph glyph, Rectangle bounds, Color color, float strokeWidth)
        {
            if (glyph == JobGlyph.None)
            {
                return;
            }

            if (JobLucideAssets.TryDraw(graphics, glyph, bounds, color))
            {
                return;
            }

            using var pen = new Pen(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var brush = new SolidBrush(color);

            switch (glyph)
            {
                case JobGlyph.Search:
                    DrawSearch(graphics, pen, bounds);
                    break;

                case JobGlyph.Filter:
                    DrawFilter(graphics, pen, bounds);
                    break;

                case JobGlyph.Refresh:
                    DrawRefresh(graphics, pen, bounds);
                    break;

                case JobGlyph.CheckCircle:
                    DrawCheckCircle(graphics, pen, brush, bounds);
                    break;

                case JobGlyph.ArrowLeft:
                    DrawArrowLeft(graphics, pen, bounds);
                    break;
            }
        }

        private static void DrawSearch(Graphics graphics, Pen pen, Rectangle bounds)
        {
            var circle = Rectangle.Inflate(bounds, -5, -5);
            circle.Width -= 4;
            circle.Height -= 4;
            graphics.DrawEllipse(pen, circle);
            graphics.DrawLine(pen, circle.Right - 1, circle.Bottom - 1, bounds.Right - 4, bounds.Bottom - 4);
        }

        private static void DrawFilter(Graphics graphics, Pen pen, Rectangle bounds)
        {
            var top = bounds.Top + bounds.Height / 5;
            var left = bounds.Left + bounds.Width / 5;
            var right = bounds.Right - bounds.Width / 5;
            var midX = bounds.Left + bounds.Width / 2;
            var neckTop = bounds.Top + bounds.Height / 2;
            var bottom = bounds.Bottom - bounds.Height / 6;

            using var path = new GraphicsPath();
            path.AddLine(left, top, right, top);
            path.AddLine(right, top, midX + 4, neckTop);
            path.AddLine(midX + 4, neckTop, midX + 4, bottom);
            path.AddLine(midX + 4, bottom, midX - 4, bottom - 4);
            path.AddLine(midX - 4, bottom - 4, midX - 4, neckTop);
            path.AddLine(midX - 4, neckTop, left, top);
            graphics.DrawPath(pen, path);
        }

        private static void DrawRefresh(Graphics graphics, Pen pen, Rectangle bounds)
        {
            var rect = Rectangle.Inflate(bounds, -3, -3);
            graphics.DrawArc(pen, rect, 35, 250);
            graphics.DrawArc(pen, rect, 215, 90);

            var arrow1 = new[]
            {
                new Point(rect.Right - 2, rect.Top + rect.Height / 2 - 5),
                new Point(rect.Right - 2, rect.Top + rect.Height / 2 + 5),
                new Point(rect.Right - 11, rect.Top + rect.Height / 2 + 2)
            };
            graphics.DrawLines(pen, arrow1);

            var arrow2 = new[]
            {
                new Point(rect.Left + 2, rect.Top + rect.Height / 2 + 5),
                new Point(rect.Left + 2, rect.Top + rect.Height / 2 - 5),
                new Point(rect.Left + 11, rect.Top + rect.Height / 2 - 2)
            };
            graphics.DrawLines(pen, arrow2);
        }

        private static void DrawCheckCircle(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            var circle = Rectangle.Inflate(bounds, -1, -1);
            graphics.FillEllipse(brush, circle);

            using var checkPen = new Pen(Color.White, Math.Max(2F, pen.Width))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            graphics.DrawLines(
                checkPen,
                new[]
                {
                    new Point(circle.Left + circle.Width / 4, circle.Top + circle.Height / 2),
                    new Point(circle.Left + circle.Width / 2 - 2, circle.Bottom - circle.Height / 4),
                    new Point(circle.Right - circle.Width / 5, circle.Top + circle.Height / 3)
                });
        }

        private static void DrawArrowLeft(Graphics graphics, Pen pen, Rectangle bounds)
        {
            var centerY = bounds.Top + bounds.Height / 2;
            var left = bounds.Left + bounds.Width / 5;
            var right = bounds.Right - bounds.Width / 5;

            graphics.DrawLine(pen, right, centerY, left, centerY);
            graphics.DrawLine(pen, left, centerY, left + bounds.Width / 4, centerY - bounds.Height / 4);
            graphics.DrawLine(pen, left, centerY, left + bounds.Width / 4, centerY + bounds.Height / 4);
        }
    }

    internal static class JobLucideAssets
    {
        private static readonly Dictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryDraw(Graphics graphics, JobGlyph glyph, Rectangle destination, Color color)
        {
            var names = glyph switch
            {
                JobGlyph.Search => new[] { "search", "lucide-search" },
                JobGlyph.Filter => new[] { "funnel", "filter", "lucide-funnel", "lucide-filter" },
                JobGlyph.Refresh => new[] { "refresh-cw", "refresh-ccw", "rotate-cw", "lucide-refresh-cw" },
                JobGlyph.CheckCircle => new[] { "circle-check", "check-circle", "lucide-circle-check", "lucide-check-circle" },
                JobGlyph.ArrowLeft => new[] { "arrow-left", "lucide-arrow-left" },
                _ => Array.Empty<string>()
            };

            return TryDrawNamed(graphics, names, destination, color, tint: true);
        }

        public static bool TryDrawNamed(
            Graphics graphics,
            IReadOnlyList<string> names,
            Rectangle destination,
            Color color,
            bool tint)
        {
            foreach (var name in names)
            {
                if (TryGet(name, out var image))
                {
                    DrawImage(graphics, image, destination, color, tint);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGet(string name, out Image image)
        {
            image = null!;

            foreach (var path in GetCandidatePaths(name))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                if (Cache.TryGetValue(path, out var cached))
                {
                    image = cached;
                    return true;
                }

                try
                {
                    using var stream = File.OpenRead(path);
                    using var source = Image.FromStream(stream);
                    image = new Bitmap(source);
                    Cache[path] = image;
                    return true;
                }
                catch
                {
                    // Fallback ke painter vektor lama bila PNG belum valid.
                }
            }

            return false;
        }

        private static IEnumerable<string> GetCandidatePaths(string name)
        {
            var fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name}.png";

            yield return Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
            yield return Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Lucide", fileName);
        }

        private static void DrawImage(Graphics graphics, Image image, Rectangle destination, Color color, bool tint)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;

            var drawRect = CenterAspectFit(image.Size, destination);

            if (!tint)
            {
                graphics.DrawImage(image, drawRect);
                return;
            }

            using var attributes = CreateTintAttributes(color);
            graphics.DrawImage(
                image,
                drawRect,
                0,
                0,
                image.Width,
                image.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        private static Rectangle CenterAspectFit(Size imageSize, Rectangle bounds)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return bounds;
            }

            var scale = Math.Min(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
            var width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
            var height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));

            return new Rectangle(
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height);
        }

        private static ImageAttributes CreateTintAttributes(Color color)
        {
            var r = color.R / 255F;
            var g = color.G / 255F;
            var b = color.B / 255F;
            var a = color.A / 255F;

            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, a, 0 },
                new float[] { r, g, b, 0, 1 }
            });

            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return attributes;
        }
    }

    internal static class JobColumnLayout
    {
        public const int ColumnCount = 7;

        public static void Apply(TableLayoutPanel table)
        {
            table.ColumnStyles.Clear();
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        }
    }

    internal static class JobVisuals
    {
        public static readonly Color Warning = Color.FromArgb(245, 137, 13);
        public static readonly Color Danger = Color.FromArgb(236, 35, 28);
        public static readonly Color Info = Color.FromArgb(31, 125, 225);
        public static readonly Color Done = Color.FromArgb(20, 132, 81);

        public static bool IsStatus(string? status, string expected)
        {
            return string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetStatusLabel(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "-";
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "ready" => "Siap",
                "pending" => "Tertunda",
                "failed" => "Gagal",
                "printing" => "Mencetak",
                "done" => "Berhasil",
                "rejected" => "Batal",
                "available" => "Tersedia",
                "tersedia" => "Tersedia",
                _ => status
            };
        }

        public static JobStatusStyle GetStatusStyle(string? status)
        {
            if (IsStatus(status, "ready"))
            {
                return new JobStatusStyle("Siap", UiTheme.Success, UiTheme.SuccessSoft, Color.FromArgb(172, 224, 193));
            }

            if (IsStatus(status, "pending"))
            {
                return new JobStatusStyle("Tertunda", Warning, Color.FromArgb(255, 247, 236), Color.FromArgb(249, 200, 139));
            }

            if (IsStatus(status, "failed"))
            {
                return new JobStatusStyle("Gagal", Danger, Color.FromArgb(255, 241, 241), Color.FromArgb(250, 187, 187));
            }

            if (IsStatus(status, "printing"))
            {
                return new JobStatusStyle("Mencetak", Info, Color.FromArgb(236, 246, 255), Color.FromArgb(174, 211, 248));
            }

            if (IsStatus(status, "done"))
            {
                return new JobStatusStyle("Berhasil", Done, Color.FromArgb(232, 247, 238), Color.FromArgb(172, 224, 193));
            }

            if (IsStatus(status, "available") || IsStatus(status, "tersedia"))
            {
                return new JobStatusStyle("Tersedia", UiTheme.Success, UiTheme.SuccessSoft, Color.FromArgb(172, 224, 193));
            }

            if (IsStatus(status, "rejected"))
            {
                return new JobStatusStyle("Batal", UiTheme.MutedText, Color.FromArgb(244, 246, 248), UiTheme.Border);
            }

            return new JobStatusStyle(GetStatusLabel(status), UiTheme.MutedText, Color.FromArgb(244, 246, 248), UiTheme.Border);
        }
    }

    internal readonly record struct JobStatusStyle(string Label, Color Accent, Color Fill, Color Border);
}
