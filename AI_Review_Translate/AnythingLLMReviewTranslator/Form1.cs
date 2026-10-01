using System.ComponentModel;
using System.Drawing;
using System.Text;

namespace AnythingLLMReviewTranslator;

public partial class Form1 : Form
{
    private readonly AppSettingsStore _settingsStore = new();
    private AppSettings _settings = AppSettings.CreateDefault();

    private readonly BindingList<ReviewRowViewModel> _rows = new();
    private readonly Dictionary<long, ReviewRowViewModel> _rowIndex = new();
    private readonly Dictionary<long, int> _rowGridIndex = new();
    private readonly object _anythingLlmLogLock = new();
    private string _anythingLlmRawLogPath = string.Empty;

    private CancellationTokenSource? _processingCts;
    private bool _pendingClose;

    private SplitContainer _rootSplit = null!;
    private SplitContainer _resultsSplit = null!;

    private TabControl _settingsTabs = null!;
    private TextBox _txtConnectionString = null!;
    private TextBox _txtSourceQuery = null!;
    private TextBox _txtSourceTable = null!;
    private TextBox _txtTargetTable = null!;
    private TextBox _txtKeyColumn = null!;
    private TextBox _txtAllTextColumn = null!;
    private TextBox _txtMsgCtxtColumn = null!;
    private TextBox _txtMsgIdColumn = null!;
    private TextBox _txtMsgStrColumn = null!;
    private TextBox _txtSuggestedTranslationColumn = null!;
    private TextBox _txtRatingColumn = null!;
    private TextBox _txtSourceFilePathColumn = null!;
    private TextBox _txtImportedAtUtcColumn = null!;
    private TextBox _txtTranslationLockedColumn = null!;

    private TextBox _txtBaseUrl = null!;
    private TextBox _txtApiKey = null!;
    private TextBox _txtWorkspaceSlug = null!;
    private ComboBox _cmbMode = null!;
    private NumericUpDown _nudTimeoutSeconds = null!;
    private TextBox _txtSessionPrefix = null!;

    private ComboBox _cmbProvider = null!;
    private TextBox _txtGeminiApiKey = null!;
    private ComboBox _cmbGeminiModel = null!;
    private TextBox _txtGeminiBaseUrl = null!;
    private NumericUpDown _nudGeminiTimeoutSeconds = null!;

    private TextBox _txtPromptTemplate = null!;

    private TextBox _txtDictionaryCsvPath = null!;
    private CheckBox _chkRespectLockedRows = null!;
    private CheckBox _chkSkipExisting = null!;
    private NumericUpDown _nudMaxConcurrentRequests = null!;
    private NumericUpDown _nudRequestsPerMinute = null!;
    private NumericUpDown _nudMaxRows = null!;
    private NumericUpDown _nudDelayMs = null!;
    private NumericUpDown _nudMaxRetries = null!;
    private NumericUpDown _nudRetryDelayMs = null!;

    private Button _btnReload = null!;
    private Button _btnSave = null!;
    private Button _btnLoad = null!;
    private Button _btnTestSql = null!;
    private Button _btnTestAnything = null!;
    private Button _btnTestGemini = null!;
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private Button _btnExport = null!;

    private DataGridView _grid = null!;
    private TextBox _logBox = null!;
    private ToolStripStatusLabel _statusLabel = null!;

    // Glossary Check tab controls
    private DataGridView _glossaryCheckGrid = null!;
    private Button _btnRunGlossaryCheck = null!;
    private Button _btnExportGlossaryCheck = null!;
    private Button _btnClearGlossaryTranslations = null!;
    private Label _lblGlossaryCheckStatus = null!;
    private CheckBox _chkOnlyMismatch = null!;
    private CheckBox _chkScanAllDatabase = null!;
    private CheckBox _chkExcludeLocked = null!;
    private CheckBox _chkSelectAllGlossaryResults = null!;
    private TextBox _txtGlossaryCheckCsvPath = null!;
    private List<GlossaryCheckResult> _allGlossaryCheckResults = new();
    private DataGridView _escapeRepairGrid = null!;
    private Button _btnScanEscapeIssues = null!;
    private Button _btnApplyEscapeRepairs = null!;
    private CheckBox _chkExcludeLockedEscapeRows = null!;
    private CheckBox _chkSelectAllEscapeRows = null!;
    private Label _lblEscapeRepairStatus = null!;
    private List<EscapeRepairResult> _allEscapeRepairResults = new();
    private ToolStripStatusLabel _progressPercentLabel = null!;
    private ToolStripProgressBar _progressBar = null!;

    public Form1()
    {
        InitializeComponent();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1360, 900);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "AnythingLLM Review Translator";
        BuildUi();
        Shown += (_, _) => ApplyInitialSplitterDistances();

        try
        {
            _settings = _settingsStore.LoadOrCreate();
            ApplySettingsToUi(_settings);
            AppendLog($"Đã nạp cấu hình từ {_settingsStore.FilePath}.");
            InitializeLoggingPath();
        }
        catch (Exception ex)
        {
            _settings = AppSettings.CreateDefault();
            ApplySettingsToUi(_settings);
            AppendLog($"Không đọc được appsettings.json, đang dùng mặc định. {ex.Message}");
            InitializeLoggingPath();
        }

        _grid.DataSource = _rows;
    }

    private void InitializeLoggingPath()
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, _settings.Logging.LogDirectory);
        try
        {
            Directory.CreateDirectory(logDirectory);
            var providerSlug = string.IsNullOrWhiteSpace(_settings.Provider) ? "translator" : _settings.Provider.ToLowerInvariant();
            _anythingLlmRawLogPath = Path.Combine(logDirectory, $"{providerSlug}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(
                _anythingLlmRawLogPath,
                $"=== Translation Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Provider: {_settings.Provider} ==={Environment.NewLine}{Environment.NewLine}",
                new UTF8Encoding(false));
            AppendLog($"Log chi tiết API được lưu tại: {_anythingLlmRawLogPath}");
        }
        catch (Exception ex)
        {
            _anythingLlmRawLogPath = Path.Combine(AppContext.BaseDirectory, $"translator-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            AppendLog($"Lưu ý khởi tạo log: {ex.Message}");
        }
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();

        _rootSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
               FixedPanel = FixedPanel.None,
               IsSplitterFixed = false,
               SplitterWidth = 8,
               BackColor = SystemColors.ControlDark
        };
        Controls.Add(_rootSplit);

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = false
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        _rootSplit.Panel1.Controls.Add(topLayout);

        _settingsTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        topLayout.Controls.Add(_settingsTabs, 0, 0);

        _settingsTabs.TabPages.Add(BuildSqlTab());
        _settingsTabs.TabPages.Add(BuildAnythingTab());
        _settingsTabs.TabPages.Add(BuildGeminiTab());
        _settingsTabs.TabPages.Add(BuildPromptTab());
        _settingsTabs.TabPages.Add(BuildProcessingTab());
        _settingsTabs.TabPages.Add(BuildGlossaryCheckTab());
        _settingsTabs.TabPages.Add(BuildEscapeRepairTab());

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            AutoScroll = true
        };
        topLayout.Controls.Add(buttonsPanel, 0, 1);

        _btnReload = CreateButton("Reload Config", ReloadConfigClicked);
        _btnSave = CreateButton("Save Config", SaveConfigClicked);
        _btnLoad = CreateButton("Load Pending", LoadPendingClicked);
        _btnTestSql = CreateButton("Test SQL", TestSqlClicked);
        _btnTestAnything = CreateButton("Test AnythingLLM", TestAnythingClicked);
        _btnTestGemini = CreateButton("Test Gemini", TestGeminiClicked);
        _btnExport = CreateButton("Export XLSX", ExportClicked);
        _btnStart = CreateButton("Start", StartClicked);
        _btnStop = CreateButton("Stop", StopClicked);

        buttonsPanel.Controls.AddRange(new Control[]
        {
            _btnReload,
            _btnSave,
            _btnLoad,
            _btnTestSql,
            _btnTestAnything,
            _btnTestGemini,
            _btnExport,
            _btnStart,
            _btnStop
        });

        _btnStop.Enabled = false;

        _resultsSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None
        };
        _rootSplit.Panel2.Controls.Add(_resultsSplit);

        BuildGrid(_resultsSplit.Panel1);
        BuildLog(_resultsSplit.Panel2);

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            Dock = DockStyle.Bottom,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        _statusLabel = new ToolStripStatusLabel
        {
            Text = "Sẵn sàng",
            Spring = false,
            AutoSize = false,
            Size = new Size(420, 22),
            Overflow = ToolStripItemOverflow.Never,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoToolTip = true
        };
        _progressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            AutoSize = false,
            Size = new Size(180, 18),
            Overflow = ToolStripItemOverflow.Never,
            Style = ProgressBarStyle.Continuous
        };
        _progressPercentLabel = new ToolStripStatusLabel
        {
            Text = "0%",
            AutoSize = false,
            Size = new Size(42, 22),
            Overflow = ToolStripItemOverflow.Never,
            TextAlign = ContentAlignment.MiddleRight
        };
        if (_progressBar.Control is ProgressBar progressBarControl)
        {
            progressBarControl.ForeColor = Color.DodgerBlue;
        }
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_progressBar);
        statusStrip.Items.Add(_progressPercentLabel);
        Controls.Add(statusStrip);

        ResumeLayout(performLayout: true);
    }

    private void ApplyInitialSplitterDistances()
    {
        ConfigureSplitterSafely(_rootSplit, 320, 260, 380);
        ConfigureSplitterSafely(_resultsSplit, 220, 180, 440);
    }

    private static void ConfigureSplitterSafely(
        SplitContainer splitContainer,
        int desiredPanel1MinSize,
        int desiredPanel2MinSize,
        int desiredDistance)
    {
        var availableLength = splitContainer.Orientation == Orientation.Horizontal
            ? splitContainer.ClientSize.Height
            : splitContainer.ClientSize.Width;

        if (availableLength <= 0)
        {
            return;
        }

        var panel1Min = Math.Max(1, desiredPanel1MinSize);
        var panel2Min = Math.Max(1, desiredPanel2MinSize);

        if (panel1Min + panel2Min >= availableLength)
        {
            var usableLength = Math.Max(2, availableLength - 1);
            var totalDesired = Math.Max(2, desiredPanel1MinSize + desiredPanel2MinSize);
            var panel1Share = desiredPanel1MinSize / (double)totalDesired;
            panel1Min = Math.Max(1, (int)Math.Floor(usableLength * panel1Share));
            panel2Min = Math.Max(1, usableLength - panel1Min);
        }

        var minDistance = panel1Min;
        var maxDistance = availableLength - panel2Min;

        if (maxDistance < minDistance)
        {
            return;
        }

        var clampedDistance = Math.Clamp(desiredDistance, minDistance, maxDistance);

        if (splitContainer.SplitterDistance != clampedDistance)
        {
            splitContainer.SplitterDistance = clampedDistance;
        }

        splitContainer.Panel1MinSize = panel1Min;
        splitContainer.Panel2MinSize = panel2Min;

        if (splitContainer.SplitterDistance != clampedDistance)
        {
            splitContainer.SplitterDistance = clampedDistance;
        }
    }

    private TabPage BuildSqlTab()
    {
        var page = new TabPage("SQL")
        {
            AutoScroll = true
        };

        var table = CreateFieldTable();
        page.Controls.Add(table);

        var row = 0;
        AddFullWidthRow(table, ref row, "Connection string", out _txtConnectionString, height: 64);
        AddFullWidthRow(table, ref row, "Source query (leave blank to auto-build from SourceTable)", out _txtSourceQuery, height: 84);
        AddPairRow(table, ref row, "Source table", out _txtSourceTable, "Target table", out _txtTargetTable);
        AddPairRow(table, ref row, "Key column", out _txtKeyColumn, "Suggested column", out _txtSuggestedTranslationColumn);
        AddPairRow(table, ref row, "Rating column", out _txtRatingColumn, "TranslationLocked column", out _txtTranslationLockedColumn);
        AddPairRow(table, ref row, "AllText column", out _txtAllTextColumn, "MsgCtxt column", out _txtMsgCtxtColumn);
        AddPairRow(table, ref row, "MsgId column", out _txtMsgIdColumn, "MsgStr column", out _txtMsgStrColumn);
        AddPairRow(table, ref row, "SourceFilePath column", out _txtSourceFilePathColumn, "ImportedAtUtc column", out _txtImportedAtUtcColumn);
        AddNoteRow(table, ref row, "Tip: nếu query của bạn đã alias đúng tên cột, chỉ cần đổi SourceQuery và TargetTable.");

        return page;
    }

    private TabPage BuildAnythingTab()
    {
        var page = new TabPage("AnythingLLM")
        {
            AutoScroll = true
        };

        var table = CreateFieldTable();
        page.Controls.Add(table);

        var row = 0;
        AddPairRow(table, ref row, "API base URL", out _txtBaseUrl, "Workspace slug", out _txtWorkspaceSlug);
        AddFullWidthRow(table, ref row, "API key", out _txtApiKey, height: 30, password: true);
        AddPairRow(table, ref row, "Mode", out _cmbMode, "Timeout (seconds)", out _nudTimeoutSeconds);
        AddFullWidthRow(table, ref row, "Session prefix", out _txtSessionPrefix, height: 30);
        AddNoteRow(table, ref row, "Khuyến nghị dùng mode = query cho bài toán dịch thuần. Chuyển sang chat nếu workspace cần RAG.");

        return page;
    }

    private TabPage BuildPromptTab()
    {
        var page = new TabPage("Prompt")
        {
            AutoScroll = true
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(8),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        page.Controls.Add(table);

        var label = new Label
        {
            Text = "Prompt template",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 4)
        };
        table.Controls.Add(label, 0, 0);

        _txtPromptTemplate = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Height = 260,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        table.Controls.Add(_txtPromptTemplate, 0, 1);

        var note = new Label
        {
            Text = "Có thể dùng các placeholder: {{Id}}, {{AllText}}, {{MsgCtxt}}, {{MsgId}}, {{MsgStr}}, {{SuggestedTranslation}}, {{Rating}}, {{SourceFilePath}}, {{ImportedAtUtc}}, {{TranslationLocked}}.",
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0)
        };
        table.Controls.Add(note, 0, 2);

        return page;
    }

    private TabPage BuildGeminiTab()
    {
        var page = new TabPage("Online AI (OpenRouter / Gemini)")
        {
            AutoScroll = true
        };

        var table = CreateFieldTable();
        page.Controls.Add(table);

        var row = 0;
        AddFullWidthRow(table, ref row, "API Key", out _txtGeminiApiKey, height: 30, password: false);
        AddPairRow(table, ref row, "Model", out _cmbGeminiModel, "Timeout (seconds)", out _nudGeminiTimeoutSeconds);
        _cmbGeminiModel.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbGeminiModel.Items.Clear();
        _cmbGeminiModel.Items.AddRange(new object[]
        {
            // ── OpenRouter: Free models ──────────────────────────────────────────────────────
            "deepseek/deepseek-r1-0528:free",
            "deepseek/deepseek-chat-v3-0324:free",
            "google/gemini-2.5-flash:free",
            "google/gemini-2.0-flash-001:free",
            "microsoft/phi-4-reasoning:free",
            "qwen/qwen3-235b-a22b:free",
            "meta-llama/llama-4-maverick:free",
            // ── OpenRouter: Paid models ──────────────────────────────────────────────────────
            "deepseek/deepseek-r1",
            "deepseek/deepseek-chat",
            "google/gemini-2.5-flash",
            "anthropic/claude-sonnet-4-5",
            "openai/gpt-4o-mini",
            // ── Google Gemini trực tiếp (thay BaseURL) ───────────────────────────────────────
            "gemini-2.5-flash",
            "gemini-2.0-flash",
            "gemini-1.5-flash"
        });
        AddFullWidthRow(table, ref row, "API Base URL", out _txtGeminiBaseUrl, height: 30);
        AddNoteRow(table, ref row,
            "OpenRouter (https://openrouter.ai/api/v1): Hỗ trợ DeepSeek, Gemini, Claude,... Model có :free = miễn phí. " +
            "Google Gemini trực tiếp: đổi URL thành https://generativelanguage.googleapis.com và dùng model không prefix.");

        return page;
    }

    private TabPage BuildProcessingTab()
    {
        var page = new TabPage("Processing")
        {
            AutoScroll = true
        };

        var table = CreateFieldTable();
        page.Controls.Add(table);

        var row = 0;
        AddFullWidthRow(table, ref row, "AI Provider", out _cmbProvider);
        _cmbProvider.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbProvider.Items.Clear();
        _cmbProvider.Items.AddRange(new object[] { "Gemini", "AnythingLLM" });

        AddFileBrowseRow(table, ref row, "Từ điển CSV (Anh - Việt)", out _txtDictionaryCsvPath, "CSV files (*.csv)|*.csv|All files (*.*)|*.*", "Chọn file CSV từ điển Anh - Việt");
        AddNoteRow(table, ref row, "Lưu ý từ điển: File CSV gồm 2 cột (Cột 1 = Tiếng Anh, Cột 2 = Tiếng Việt). Khi gửi câu hỏi lên AI, hệ thống chỉ lọc và gửi các từ/cụm từ có trong MsgId của dòng cần dịch.");

        AddCheckBoxRow(table, ref row, "Respect TranslationLocked", out _chkRespectLockedRows, "Skip if SuggestedTranslation exists", out _chkSkipExisting);
        AddPairRow(table, ref row, "Max concurrent requests", out _nudMaxConcurrentRequests, "Requests Per Minute (RPM)", out _nudRequestsPerMinute);
        _nudMaxConcurrentRequests.Minimum = 1;
        _nudMaxConcurrentRequests.Maximum = 16;
        _nudMaxConcurrentRequests.Value = 4;

        _nudRequestsPerMinute.Minimum = 0;
        _nudRequestsPerMinute.Maximum = 1000;
        _nudRequestsPerMinute.Value = 5;

        AddPairRow(table, ref row, "Max rows (0 = all)", out _nudMaxRows, "Delay between requests (ms)", out _nudDelayMs);
        AddPairRow(table, ref row, "Max retries", out _nudMaxRetries, "Retry delay (ms)", out _nudRetryDelayMs);
        AddNoteRow(table, ref row, "Mẹo: Với Gemini Free Tier (limit 5 request/phút), hãy đặt RPM = 5 và Max concurrent = 1. Khi dính 429, ứng dụng sẽ tự động chờ và thử lại.");

        return page;
    }

    private TabPage BuildGlossaryCheckTab()
    {
        var page = new TabPage("Glossary Check")
        {
            Padding = new Padding(6)
        };

        // Hàng chọn file CSV
        var csvPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 0)
        };
        csvPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        csvPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        csvPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));

        var csvLabel = new Label
        {
            Text = "Từ điển CSV:",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0)
        };
        _txtGlossaryCheckCsvPath = new TextBox
        {
            Dock = DockStyle.Fill
        };
        var btnBrowseGlossaryCsv = new Button
        {
            Text = "Chọn file...",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0)
        };
        btnBrowseGlossaryCsv.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Chọn file CSV từ điển Anh - Việt"
            };
            if (!string.IsNullOrWhiteSpace(_txtGlossaryCheckCsvPath.Text))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_txtGlossaryCheckCsvPath.Text);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        dialog.InitialDirectory = dir;
                }
                catch { /* ignore invalid path */ }
            }
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _txtGlossaryCheckCsvPath.Text = dialog.FileName;
        };
        csvPanel.Controls.Add(csvLabel, 0, 0);
        csvPanel.Controls.Add(_txtGlossaryCheckCsvPath, 1, 0);
        csvPanel.Controls.Add(btnBrowseGlossaryCsv, 2, 0);

        // Hàng nút kiểm tra + checkbox + trạng thái
        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 4)
        };

        _btnRunGlossaryCheck = new Button
        {
            Text = "Kiểm tra từ điển",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _btnRunGlossaryCheck.Click += RunGlossaryCheckClicked;

        _chkScanAllDatabase = new CheckBox
        {
            Text = "Quét toàn bộ CSDL (SQL)",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 6, 8, 0)
        };

        _chkExcludeLocked = new CheckBox
        {
            Text = "Locked khác 1",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 6, 8, 0)
        };

        _chkOnlyMismatch = new CheckBox
        {
            Text = "Chỉ hiện từ chưa khớp",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        _chkOnlyMismatch.CheckedChanged += (_, _) => ApplyGlossaryCheckFilter();

        _chkSelectAllGlossaryResults = new CheckBox
        {
            Text = "Chọn tất cả",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        _chkSelectAllGlossaryResults.CheckedChanged += (_, _) =>
        {
            foreach (var result in _allGlossaryCheckResults)
            {
                result.Selected = _chkSelectAllGlossaryResults.Checked;
            }
            ApplyGlossaryCheckFilter();
        };

        _btnClearGlossaryTranslations = new Button
        {
            Text = "Xóa SuggestTranslation + Rating",
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 0, 8, 0),
            Enabled = false
        };
        _btnClearGlossaryTranslations.Click += ClearSelectedGlossaryTranslationsClicked;

        _btnExportGlossaryCheck = new Button
        {
            Text = "Xuất Excel...",
            AutoSize = true,
            Enabled = false,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _btnExportGlossaryCheck.Click += ExportGlossaryCheckClicked;

        _lblGlossaryCheckStatus = new Label
        {
            Text = "Chưa kiểm tra.",
            AutoSize = true,
            Margin = new Padding(0, 7, 0, 0)
        };

        topPanel.Controls.Add(_btnRunGlossaryCheck);
        topPanel.Controls.Add(_chkScanAllDatabase);
        topPanel.Controls.Add(_chkExcludeLocked);
        topPanel.Controls.Add(_chkOnlyMismatch);
        topPanel.Controls.Add(_chkSelectAllGlossaryResults);
        topPanel.Controls.Add(_btnClearGlossaryTranslations);
        topPanel.Controls.Add(_btnExportGlossaryCheck);
        topPanel.Controls.Add(_lblGlossaryCheckStatus);

        _glossaryCheckGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };

        _glossaryCheckGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Chọn",
            DataPropertyName = nameof(GlossaryCheckResult.Selected),
            Width = 55,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.Automatic
        });

        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Id",
            DataPropertyName = nameof(GlossaryCheckResult.RowId),
            Width = 70,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "MsgId",
            DataPropertyName = nameof(GlossaryCheckResult.MsgId),
            Width = 200,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "MsgStr (Gốc)",
            DataPropertyName = nameof(GlossaryCheckResult.MsgStr),
            Width = 180,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "SuggestedTranslation",
            DataPropertyName = nameof(GlossaryCheckResult.SuggestedTranslation),
            Width = 200,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "English (Từ điển)",
            DataPropertyName = nameof(GlossaryCheckResult.EnglishTerm),
            Width = 140,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Vietnamese (Từ điển)",
            DataPropertyName = nameof(GlossaryCheckResult.ExpectedVietnamese),
            Width = 180,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Từ trùng MsgId/MsgStr",
            DataPropertyName = nameof(GlossaryCheckResult.CommonWords),
            Width = 160,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        _glossaryCheckGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Trạng thái",
            DataPropertyName = nameof(GlossaryCheckResult.MatchStatus),
            Width = 110,
            SortMode = DataGridViewColumnSortMode.Automatic
        });

        // Tô màu dòng dựa trên trạng thái khớp
        _glossaryCheckGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _glossaryCheckGrid.Rows.Count)
                return;

            var row = _glossaryCheckGrid.Rows[e.RowIndex];
            if (row.DataBoundItem is GlossaryCheckResult result)
            {
                if (!result.IsMatch)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                    row.DefaultCellStyle.ForeColor = Color.DarkRed;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(235, 255, 235);
                    row.DefaultCellStyle.ForeColor = Color.DarkGreen;
                }
            }
        };
        foreach (DataGridViewColumn column in _glossaryCheckGrid.Columns)
        {
            if (column.DataPropertyName != nameof(GlossaryCheckResult.Selected))
            {
                column.ReadOnly = true;
            }
        }
        _glossaryCheckGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_glossaryCheckGrid.IsCurrentCellDirty)
            {
                _glossaryCheckGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _glossaryCheckGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _glossaryCheckGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(GlossaryCheckResult.Selected))
            {
                _btnClearGlossaryTranslations.Enabled = _allGlossaryCheckResults.Any(r => r.Selected);
            }
        };

        // Thứ tự add ngược lại do Dock: Fill cần add trước, Top add sau
        page.Controls.Add(_glossaryCheckGrid);
        page.Controls.Add(topPanel);
        page.Controls.Add(csvPanel);

        return page;
    }

    private TabPage BuildEscapeRepairTab()
    {
        var page = new TabPage("Sửa escape")
        {
            Padding = new Padding(6)
        };
        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 4)
        };

        _btnScanEscapeIssues = new Button
        {
            Text = "Quét lỗi escape",
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _btnScanEscapeIssues.Click += ScanEscapeIssuesClicked;
        _chkExcludeLockedEscapeRows = new CheckBox
        {
            Text = "Bỏ qua dòng đã khóa",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        _chkSelectAllEscapeRows = new CheckBox
        {
            Text = "Chọn tất cả",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        _chkSelectAllEscapeRows.CheckedChanged += (_, _) =>
        {
            foreach (var result in _allEscapeRepairResults)
            {
                result.Selected = _chkSelectAllEscapeRows.Checked;
            }
            RefreshEscapeRepairGrid();
        };
        _btnApplyEscapeRepairs = new Button
        {
            Text = "Sửa các dòng đã chọn",
            AutoSize = true,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0),
            Enabled = false
        };
        _btnApplyEscapeRepairs.Click += ApplyEscapeRepairsClicked;
        _lblEscapeRepairStatus = new Label
        {
            Text = "Chưa quét.",
            AutoSize = true,
            Margin = new Padding(0, 7, 0, 0)
        };
        topPanel.Controls.AddRange(new Control[]
        {
            _btnScanEscapeIssues,
            _chkExcludeLockedEscapeRows,
            _chkSelectAllEscapeRows,
            _btnApplyEscapeRepairs,
            _lblEscapeRepairStatus
        });

        _escapeRepairGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
        _escapeRepairGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Chọn",
            DataPropertyName = nameof(EscapeRepairResult.Selected),
            Width = 55,
            ReadOnly = false
        });
        AddEscapeRepairColumn("Id", nameof(EscapeRepairResult.RowId), 75);
        AddEscapeRepairColumn("MsgId", nameof(EscapeRepairResult.MsgId), 230);
        AddEscapeRepairColumn("SuggestedTranslation hiện tại", nameof(EscapeRepairResult.OriginalTranslation), 260);
        AddEscapeRepairColumn("Bản sau khi sửa", nameof(EscapeRepairResult.RepairedTranslation), 260);
        AddEscapeRepairColumn("Lỗi phát hiện", nameof(EscapeRepairResult.Issues), 150);
        foreach (DataGridViewColumn column in _escapeRepairGrid.Columns)
        {
            if (column.DataPropertyName != nameof(EscapeRepairResult.Selected))
            {
                column.ReadOnly = true;
            }
        }
        _escapeRepairGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_escapeRepairGrid.IsCurrentCellDirty)
            {
                _escapeRepairGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _escapeRepairGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _escapeRepairGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(EscapeRepairResult.Selected))
            {
                _btnApplyEscapeRepairs.Enabled = _allEscapeRepairResults.Any(result => result.Selected);
            }
        };

        page.Controls.Add(_escapeRepairGrid);
        page.Controls.Add(topPanel);
        return page;
    }

    private void AddEscapeRepairColumn(string headerText, string dataPropertyName, int width)
    {
        _escapeRepairGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = headerText,
            DataPropertyName = dataPropertyName,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private async void ScanEscapeIssuesClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadSettingsFromUi();
            ValidateSqlSettings(settings);
            _btnScanEscapeIssues.Enabled = false;
            _btnApplyEscapeRepairs.Enabled = false;
            _lblEscapeRepairStatus.Text = "Đang tải dữ liệu từ CSDL...";

            var repository = new MssqlTranslationRepository(settings.SqlServer, settings.Processing, 300);
            var rows = await repository.LoadAllRowsForGlossaryCheckAsync(_chkExcludeLockedEscapeRows.Checked);
            _allEscapeRepairResults = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.MsgId) && !string.IsNullOrWhiteSpace(row.SuggestedTranslation))
                .Select(row =>
                {
                    var original = row.SuggestedTranslation!;
                    var repaired = TranslationEscapeRepair.Repair(row.MsgId!, original);
                    return repaired == original
                        ? null
                        : new EscapeRepairResult
                        {
                            RowId = row.Id,
                            MsgId = row.MsgId!,
                            OriginalTranslation = original,
                            RepairedTranslation = repaired,
                            Issues = TranslationEscapeRepair.DescribeIssues(row.MsgId!, original)
                        };
                })
                .Where(result => result is not null)
                .Cast<EscapeRepairResult>()
                .ToList();

            _chkSelectAllEscapeRows.Checked = false;
            RefreshEscapeRepairGrid();
            _lblEscapeRepairStatus.Text = $"Đã quét {rows.Count:N0} dòng; tìm thấy {_allEscapeRepairResults.Count:N0} dòng cần sửa.";
            AppendLog($"Sửa escape: Đã quét {rows.Count:N0} dòng, phát hiện {_allEscapeRepairResults.Count:N0} dòng cần sửa.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Lỗi quét escape", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblEscapeRepairStatus.Text = "Lỗi khi quét.";
        }
        finally
        {
            _btnScanEscapeIssues.Enabled = true;
            _btnApplyEscapeRepairs.Enabled = _allEscapeRepairResults.Any(result => result.Selected);
        }
    }

    private async void ApplyEscapeRepairsClicked(object? sender, EventArgs e)
    {
        var selected = _allEscapeRepairResults.Where(result => result.Selected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Cập nhật SuggestedTranslation của {selected.Count:N0} dòng đã chọn? Rating sẽ được giữ nguyên.",
            "Xác nhận sửa escape",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var settings = ReadSettingsFromUi();
            ValidateSqlSettings(settings);
            _btnApplyEscapeRepairs.Enabled = false;
            var updates = selected.ToDictionary(result => result.RowId, result => result.RepairedTranslation);
            var repository = new MssqlTranslationRepository(settings.SqlServer, settings.Processing, 300);
            await repository.UpdateSuggestedTranslationsAsync(updates, CancellationToken.None);

            _allEscapeRepairResults.RemoveAll(result => result.Selected);
            _chkSelectAllEscapeRows.Checked = false;
            RefreshEscapeRepairGrid();
            _lblEscapeRepairStatus.Text = $"Đã sửa {selected.Count:N0} dòng.";
            AppendLog($"Sửa escape: Đã cập nhật SuggestedTranslation của {selected.Count:N0} dòng.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Lỗi cập nhật escape", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblEscapeRepairStatus.Text = "Lỗi khi cập nhật.";
        }
        finally
        {
            _btnApplyEscapeRepairs.Enabled = _allEscapeRepairResults.Any(result => result.Selected);
        }
    }

    private void RefreshEscapeRepairGrid()
    {
        _escapeRepairGrid.DataSource = null;
        _escapeRepairGrid.DataSource = _allEscapeRepairResults.ToList();
        _btnApplyEscapeRepairs.Enabled = _allEscapeRepairResults.Any(result => result.Selected);
    }

    private async void RunGlossaryCheckClicked(object? sender, EventArgs e)
    {
        try
        {
            var csvPath = _txtGlossaryCheckCsvPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                MessageBox.Show(this, "Chưa chọn file từ điển CSV. Vui lòng chọn file ở trên.", "Glossary Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!File.Exists(csvPath))
            {
                MessageBox.Show(this, $"File từ điển không tồn tại: {csvPath}", "Glossary Check", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var settings = ReadSettingsFromUi();

            _btnRunGlossaryCheck.Enabled = false;
            _btnExportGlossaryCheck.Enabled = false;
            _lblGlossaryCheckStatus.Text = "Đang chuẩn bị...";

            List<GlossaryCheckRow> rowsToCheck;
            bool excludeLocked = _chkExcludeLocked.Checked;

            if (_chkScanAllDatabase.Checked)
            {
                ValidateSqlSettings(settings);
                _lblGlossaryCheckStatus.Text = "Đang kết nối CSDL và nạp dữ liệu...";
                AppendLog($"Glossary Check: Bắt đầu tải dữ liệu từ CSDL ({(excludeLocked ? "Locked <> 1" : "Toàn bộ")})...");

                var repository = new MssqlTranslationRepository(settings.SqlServer, settings.Processing, 300);
                var loadProgress = new Progress<int>(count =>
                {
                    _lblGlossaryCheckStatus.Text = $"Đang tải từ CSDL: {count:N0} dòng...";
                });

                rowsToCheck = await repository.LoadAllRowsForGlossaryCheckAsync(excludeLocked, loadProgress);
                AppendLog($"Glossary Check: Đã nạp thành công {rowsToCheck.Count:N0} dòng từ CSDL.");
            }
            else
            {
                if (_rows.Count == 0)
                {
                    MessageBox.Show(this, "Chưa có dữ liệu trong bảng. Hãy tích chọn 'Quét toàn bộ CSDL' hoặc nhấn 'Load Pending' ở tab Xử lý.", "Glossary Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sourceRows = excludeLocked
                    ? _rows.Where(r => r.TranslationLocked != true)
                    : _rows;

                rowsToCheck = sourceRows.Select(r => new GlossaryCheckRow
                {
                    Id = r.Id,
                    MsgId = r.MsgId,
                    MsgStr = r.MsgStr,
                    SuggestedTranslation = r.SuggestedTranslation
                }).ToList();
            }

            _lblGlossaryCheckStatus.Text = $"Đang đọc từ điển và quét {rowsToCheck.Count:N0} dòng...";
            var results = await Task.Run(() =>
            {
                var glossary = GlossaryDictionary.LoadFromCsv(csvPath);
                var scanResults = new List<GlossaryCheckResult>();

                foreach (var row in rowsToCheck)
                {
                    if (string.IsNullOrWhiteSpace(row.MsgId))
                    {
                        continue;
                    }

                    var matches = glossary.ScanRow(row.Id, row.MsgId, row.MsgStr, row.SuggestedTranslation);
                    if (matches.Count > 0)
                    {
                        scanResults.AddRange(matches);
                    }
                }

                return scanResults;
            });

            _allGlossaryCheckResults = results;
            _chkSelectAllGlossaryResults.Checked = false;
            ApplyGlossaryCheckFilter();

            var totalMismatch = _allGlossaryCheckResults.Count(r => !r.IsMatch);
            var totalMatch = _allGlossaryCheckResults.Count(r => r.IsMatch);
            _lblGlossaryCheckStatus.Text = $"Tổng: {_allGlossaryCheckResults.Count:N0} | ✅ Khớp: {totalMatch:N0} | ❌ Chưa khớp: {totalMismatch:N0}";
            AppendLog($"Glossary Check: Đã quét {rowsToCheck.Count:N0} dòng. Tìm thấy {_allGlossaryCheckResults.Count:N0} kết quả đối chiếu ({totalMismatch:N0} chưa khớp, {totalMatch:N0} khớp).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Glossary Check Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"Glossary Check lỗi: {ex.Message}");
            _lblGlossaryCheckStatus.Text = "Lỗi khi kiểm tra.";
        }
        finally
        {
            _btnRunGlossaryCheck.Enabled = true;
            _btnExportGlossaryCheck.Enabled = _allGlossaryCheckResults.Count > 0;
            _btnClearGlossaryTranslations.Enabled = _allGlossaryCheckResults.Any(r => r.Selected);
        }
    }

    private async void ClearSelectedGlossaryTranslationsClicked(object? sender, EventArgs e)
    {
        var selectedIds = _allGlossaryCheckResults
            .Where(r => r.Selected)
            .Select(r => r.RowId)
            .Distinct()
            .ToList();

        if (selectedIds.Count == 0)
        {
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Xóa SuggestedTranslation và Rating của {selectedIds.Count:N0} dòng đã chọn?",
            "Xác nhận xóa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var settings = ReadSettingsFromUi();
            ValidateSqlSettings(settings);
            _btnClearGlossaryTranslations.Enabled = false;
            var repository = new MssqlTranslationRepository(settings.SqlServer, settings.Processing, 300);
            await repository.ClearTranslationsAsync(selectedIds, CancellationToken.None);

            foreach (var result in _allGlossaryCheckResults.Where(r => selectedIds.Contains(r.RowId)))
            {
                result.Selected = false;
                result.SuggestedTranslation = null;
                result.IsMatch = false;
            }

            _chkSelectAllGlossaryResults.Checked = false;
            ApplyGlossaryCheckFilter();
            _lblGlossaryCheckStatus.Text = $"Đã xóa SuggestTranslation và Rating của {selectedIds.Count:N0} dòng.";
            AppendLog($"Glossary Check: Đã xóa SuggestTranslation và Rating của {selectedIds.Count:N0} dòng.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Lỗi xóa dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnClearGlossaryTranslations.Enabled = _allGlossaryCheckResults.Any(r => r.Selected);
        }
    }

    private void ExportGlossaryCheckClicked(object? sender, EventArgs e)
    {
        try
        {
            var dataToExport = _chkOnlyMismatch.Checked
                ? _allGlossaryCheckResults.Where(r => !r.IsMatch).ToList()
                : _allGlossaryCheckResults;

            if (dataToExport.Count == 0)
            {
                MessageBox.Show(this, "Không có dữ liệu kết quả để xuất.", "Xuất Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Xuất kết quả kiểm tra từ điển ra Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"Glossary_Check_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ExcelExportService.ExportGlossaryCheckResults(dialog.FileName, dataToExport);
            AppendLog($"Đã xuất file Excel kiểm tra từ điển: {dialog.FileName}");
            MessageBox.Show(this, $"Đã xuất {dataToExport.Count:N0} dòng kết quả ra file:\n{dialog.FileName}", "Xuất Excel thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Lỗi xuất Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyGlossaryCheckFilter()
    {
        var filtered = _chkOnlyMismatch.Checked
            ? _allGlossaryCheckResults.Where(r => !r.IsMatch).ToList()
            : _allGlossaryCheckResults;

        _glossaryCheckGrid.DataSource = null;
        _glossaryCheckGrid.DataSource = filtered;
        _btnClearGlossaryTranslations.Enabled = _allGlossaryCheckResults.Any(r => r.Selected);
    }

    private void BuildGrid(Control parent)
    {
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };

        AddGridColumn("Id", nameof(ReviewRowViewModel.Id), 80);
        AddGridColumn("MsgId", nameof(ReviewRowViewModel.MsgId), 160);
        AddGridColumn("MsgCtxt", nameof(ReviewRowViewModel.MsgCtxt), 180);
        AddGridColumn("MsgStr", nameof(ReviewRowViewModel.MsgStr), 200);
        AddGridColumn("SuggestedTranslation", nameof(ReviewRowViewModel.SuggestedTranslation), 220);
        AddGridColumn("Rating", nameof(ReviewRowViewModel.Rating), 80);
        AddGridColumn("Locked", nameof(ReviewRowViewModel.TranslationLocked), 70);
        AddGridColumn("SourceFilePath", nameof(ReviewRowViewModel.SourceFilePath), 200);
        AddGridColumn("ImportedAtUtc", nameof(ReviewRowViewModel.ImportedAtUtc), 170);
        AddGridColumn("Status", nameof(ReviewRowViewModel.Status), 110);
        AddGridColumn("Error", nameof(ReviewRowViewModel.Error), 260);

        parent.Controls.Add(_grid);
    }

    private void BuildLog(Control parent)
    {
        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.White
        };
        parent.Controls.Add(_logBox);
    }

    private TableLayoutPanel CreateFieldTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Padding = new Padding(8),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        return table;
    }

    private static Button CreateButton(string text, EventHandler clickHandler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(12, 6, 12, 6)
        };
        button.Click += clickHandler;
        return button;
    }

    private void AddGridColumn(string headerText, string dataPropertyName, int width)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = headerText,
            DataPropertyName = dataPropertyName,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        _grid.Columns.Add(column);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0)
        };
    }

    private TextBox CreateTextBox(int height = 30, bool multiline = false, bool password = false)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Height = height,
            Multiline = multiline,
            AcceptsReturn = multiline,
            AcceptsTab = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            UseSystemPasswordChar = password
        };
    }

    private NumericUpDown CreateNumericUpDown(decimal minimum, decimal maximum, decimal value)
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Min(Math.Max(value, minimum), maximum),
            TextAlign = HorizontalAlignment.Right
        };
    }

    private ComboBox CreateModeComboBox()
    {
        return new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "query", "chat" }
        };
    }

    private void AddFullWidthRow(TableLayoutPanel table, ref int row, string labelText, out TextBox textBox, int height = 30, bool password = false)
    {
        textBox = CreateTextBox(height, multiline: height > 40, password: password);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(labelText), 0, row);
        table.Controls.Add(textBox, 1, row);
        table.SetColumnSpan(textBox, 3);
        row++;
    }

    private void AddFullWidthRow(TableLayoutPanel table, ref int row, string labelText, out ComboBox comboBox, int height = 30)
    {
        comboBox = CreateModeComboBox();
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(labelText), 0, row);
        table.Controls.Add(comboBox, 1, row);
        table.SetColumnSpan(comboBox, 3);
        row++;
    }

    private void AddFullWidthRow(TableLayoutPanel table, ref int row, string labelText, out NumericUpDown numericUpDown, int height = 30)
    {
        numericUpDown = CreateNumericUpDown(0, 100000, 0);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(labelText), 0, row);
        table.Controls.Add(numericUpDown, 1, row);
        table.SetColumnSpan(numericUpDown, 3);
        row++;
    }

    private void AddFileBrowseRow(
        TableLayoutPanel table,
        ref int row,
        string labelText,
        out TextBox textBox,
        string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
        string dialogTitle = "Chọn file CSV")
    {
        textBox = CreateTextBox();

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browseButton = new Button
        {
            Text = "Chọn file...",
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Height = textBox.Height
        };

        var targetTextBox = textBox;
        browseButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = dialogTitle
            };
            if (!string.IsNullOrWhiteSpace(targetTextBox.Text))
            {
                try
                {
                    var dir = Path.GetDirectoryName(targetTextBox.Text);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        dialog.InitialDirectory = dir;
                    }
                }
                catch
                {
                    // ignore invalid path syntax
                }
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                targetTextBox.Text = dialog.FileName;
            }
        };

        panel.Controls.Add(textBox, 0, 0);
        panel.Controls.Add(browseButton, 1, 0);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(labelText), 0, row);
        table.Controls.Add(panel, 1, row);
        table.SetColumnSpan(panel, 3);
        row++;
    }

    private void AddPairRow(TableLayoutPanel table, ref int row, string label1, out TextBox control1, string label2, out TextBox control2)
    {
        control1 = CreateTextBox();
        control2 = CreateTextBox();
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(label1), 0, row);
        table.Controls.Add(control1, 1, row);
        table.Controls.Add(CreateLabel(label2), 2, row);
        table.Controls.Add(control2, 3, row);
        row++;
    }

    private void AddPairRow(TableLayoutPanel table, ref int row, string label1, out ComboBox control1, string label2, out NumericUpDown control2)
    {
        control1 = CreateModeComboBox();
        control2 = CreateNumericUpDown(15, 600, 120);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(label1), 0, row);
        table.Controls.Add(control1, 1, row);
        table.Controls.Add(CreateLabel(label2), 2, row);
        table.Controls.Add(control2, 3, row);
        row++;
    }

    private void AddPairRow(TableLayoutPanel table, ref int row, string label1, out NumericUpDown control1, string label2, out NumericUpDown control2)
    {
        control1 = CreateNumericUpDown(0, 100000, 0);
        control2 = CreateNumericUpDown(0, 100000, 0);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(CreateLabel(label1), 0, row);
        table.Controls.Add(control1, 1, row);
        table.Controls.Add(CreateLabel(label2), 2, row);
        table.Controls.Add(control2, 3, row);
        row++;
    }

    private void AddCheckBoxRow(TableLayoutPanel table, ref int row, string label1, out CheckBox control1, string label2, out CheckBox control2)
    {
        control1 = new CheckBox { Text = label1, AutoSize = true, Dock = DockStyle.Fill };
        control2 = new CheckBox { Text = label2, AutoSize = true, Dock = DockStyle.Fill };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control1, 0, row);
        table.Controls.Add(control2, 2, row);
        table.SetColumnSpan(control1, 2);
        table.SetColumnSpan(control2, 2);
        row++;
    }

    private void AddSingleCheckBoxRow(TableLayoutPanel table, ref int row, string label, out CheckBox control)
    {
        control = new CheckBox { Text = label, AutoSize = true, Dock = DockStyle.Fill };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 4);
        row++;
    }

    private void AddNoteRow(TableLayoutPanel table, ref int row, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0)
        };

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 4);
        row++;
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        settings = settings.Normalize();

        _cmbProvider.SelectedItem = settings.Provider;

        _txtConnectionString.Text = settings.SqlServer.ConnectionString;
        _txtSourceQuery.Text = settings.SqlServer.SourceQuery;
        _txtSourceTable.Text = settings.SqlServer.SourceTable;
        _txtTargetTable.Text = settings.SqlServer.TargetTable;
        _txtKeyColumn.Text = settings.SqlServer.KeyColumn;
        _txtAllTextColumn.Text = settings.SqlServer.AllTextColumn;
        _txtMsgCtxtColumn.Text = settings.SqlServer.MsgCtxtColumn;
        _txtMsgIdColumn.Text = settings.SqlServer.MsgIdColumn;
        _txtMsgStrColumn.Text = settings.SqlServer.MsgStrColumn;
        _txtSuggestedTranslationColumn.Text = settings.SqlServer.SuggestedTranslationColumn;
        _txtRatingColumn.Text = settings.SqlServer.RatingColumn;
        _txtSourceFilePathColumn.Text = settings.SqlServer.SourceFilePathColumn;
        _txtImportedAtUtcColumn.Text = settings.SqlServer.ImportedAtUtcColumn;
        _txtTranslationLockedColumn.Text = settings.SqlServer.TranslationLockedColumn;

        _txtBaseUrl.Text = settings.AnythingLLM.BaseUrl;
        _txtApiKey.Text = settings.AnythingLLM.ApiKey;
        _txtWorkspaceSlug.Text = settings.AnythingLLM.WorkspaceSlug;
        _cmbMode.SelectedItem = settings.AnythingLLM.Mode;
        _nudTimeoutSeconds.Value = Math.Clamp(settings.AnythingLLM.RequestTimeoutSeconds, (int)_nudTimeoutSeconds.Minimum, (int)_nudTimeoutSeconds.Maximum);
        _txtSessionPrefix.Text = settings.AnythingLLM.SessionPrefix;

        _txtGeminiApiKey.Text = settings.Gemini.ApiKey;
        _cmbGeminiModel.Text = settings.Gemini.Model;
        _txtGeminiBaseUrl.Text = settings.Gemini.BaseUrl;
        _nudGeminiTimeoutSeconds.Value = Math.Clamp(settings.Gemini.RequestTimeoutSeconds, (int)_nudGeminiTimeoutSeconds.Minimum, (int)_nudGeminiTimeoutSeconds.Maximum);

        _txtPromptTemplate.Text = settings.AnythingLLM.PromptTemplate;

        _txtDictionaryCsvPath.Text = settings.Processing.DictionaryCsvPath;
        _txtGlossaryCheckCsvPath.Text = settings.Processing.DictionaryCsvPath;
        _chkRespectLockedRows.Checked = settings.Processing.RespectTranslationLocked;
        _chkSkipExisting.Checked = settings.Processing.SkipIfSuggestedExists;
        _nudMaxConcurrentRequests.Value = Math.Clamp(settings.Processing.MaxConcurrentRequests, (int)_nudMaxConcurrentRequests.Minimum, (int)_nudMaxConcurrentRequests.Maximum);
        _nudRequestsPerMinute.Value = Math.Clamp(settings.Processing.RequestsPerMinute, (int)_nudRequestsPerMinute.Minimum, (int)_nudRequestsPerMinute.Maximum);
        _nudMaxRows.Value = Math.Clamp(settings.Processing.MaxRows, (int)_nudMaxRows.Minimum, (int)_nudMaxRows.Maximum);
        _nudDelayMs.Value = Math.Clamp(settings.Processing.DelayBetweenRequestsMs, (int)_nudDelayMs.Minimum, (int)_nudDelayMs.Maximum);
        _nudMaxRetries.Value = Math.Clamp(settings.Processing.MaxRetries, (int)_nudMaxRetries.Minimum, (int)_nudMaxRetries.Maximum);
        _nudRetryDelayMs.Value = Math.Clamp(settings.Processing.RetryDelayMs, (int)_nudRetryDelayMs.Minimum, (int)_nudRetryDelayMs.Maximum);

        if (_cmbMode.SelectedItem is null)
        {
            _cmbMode.SelectedIndex = 0;
        }

        if (_cmbProvider.SelectedItem is null)
        {
            _cmbProvider.SelectedIndex = 0;
        }
    }

    private AppSettings ReadSettingsFromUi()
    {
        var settings = new AppSettings
        {
            Provider = Convert.ToString(_cmbProvider.SelectedItem) ?? "Gemini",
            SqlServer = new SqlServerSettings
            {
                ConnectionString = _txtConnectionString.Text,
                SourceQuery = _txtSourceQuery.Text,
                SourceTable = _txtSourceTable.Text,
                TargetTable = _txtTargetTable.Text,
                KeyColumn = _txtKeyColumn.Text,
                AllTextColumn = _txtAllTextColumn.Text,
                MsgCtxtColumn = _txtMsgCtxtColumn.Text,
                MsgIdColumn = _txtMsgIdColumn.Text,
                MsgStrColumn = _txtMsgStrColumn.Text,
                SuggestedTranslationColumn = _txtSuggestedTranslationColumn.Text,
                RatingColumn = _txtRatingColumn.Text,
                SourceFilePathColumn = _txtSourceFilePathColumn.Text,
                ImportedAtUtcColumn = _txtImportedAtUtcColumn.Text,
                TranslationLockedColumn = _txtTranslationLockedColumn.Text
            },
            AnythingLLM = new AnythingLlmSettings
            {
                BaseUrl = _txtBaseUrl.Text,
                ApiKey = _txtApiKey.Text,
                WorkspaceSlug = _txtWorkspaceSlug.Text,
                Mode = Convert.ToString(_cmbMode.SelectedItem) ?? "query",
                RequestTimeoutSeconds = (int)_nudTimeoutSeconds.Value,
                SessionPrefix = _txtSessionPrefix.Text,
                PromptTemplate = _txtPromptTemplate.Text
            },
            Gemini = new GeminiSettings
            {
                ApiKey = _txtGeminiApiKey.Text,
                Model = _cmbGeminiModel.Text,
                BaseUrl = _txtGeminiBaseUrl.Text,
                RequestTimeoutSeconds = (int)_nudGeminiTimeoutSeconds.Value
            },
            Processing = new ProcessingSettings
            {
                DictionaryCsvPath = _txtDictionaryCsvPath.Text.Trim(),
                MaxConcurrentRequests = (int)_nudMaxConcurrentRequests.Value,
                RequestsPerMinute = (int)_nudRequestsPerMinute.Value,
                RespectTranslationLocked = _chkRespectLockedRows.Checked,
                SkipIfSuggestedExists = _chkSkipExisting.Checked,
                MaxRows = (int)_nudMaxRows.Value,
                DelayBetweenRequestsMs = (int)_nudDelayMs.Value,
                MaxRetries = (int)_nudMaxRetries.Value,
                RetryDelayMs = (int)_nudRetryDelayMs.Value
            }
        };

        return settings.Normalize();
    }

    private void ReloadConfigClicked(object? sender, EventArgs e)
    {
        try
        {
            _settings = _settingsStore.LoadOrCreate();
            ApplySettingsToUi(_settings);
            AppendLog("Đã nạp lại cấu hình từ appsettings.json.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Reload Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveConfigClicked(object? sender, EventArgs e)
    {
        try
        {
            _settings = ReadSettingsFromUi();
            _settingsStore.Save(_settings);
            AppendLog($"Đã lưu cấu hình vào {_settingsStore.FilePath}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void LoadPendingClicked(object? sender, EventArgs e)
    {
        try
        {
            await LoadRowsIntoGridAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load Pending", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TestSqlClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadSettingsFromUi();
            ValidateSqlSettings(settings);
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(settings.SqlServer.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync();
            AppendLog("Kết nối SQL OK.");
            MessageBox.Show(this, "Kết nối SQL OK.", "Test SQL", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test SQL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TestAnythingClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadSettingsFromUi();
            ValidateAnythingSettings(settings);
            using var client = new AnythingLlmClient(settings.AnythingLLM);
            client.ResponseReceived += AppendAnythingLlmRawResponse;
            var body = await client.TestWorkspaceAsync(settings.AnythingLLM.WorkspaceSlug, CancellationToken.None);
            AppendLog($"AnythingLLM OK. Response: {TrimForLog(body, 500)}");
            MessageBox.Show(this, "Kết nối AnythingLLM OK.", "Test AnythingLLM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test AnythingLLM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TestGeminiClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadSettingsFromUi();
            ValidateGeminiSettings(settings);
            using var client = new GeminiClient(settings.Gemini, settings.AnythingLLM.PromptTemplate);
            client.ResponseReceived += AppendAnythingLlmRawResponse;
            var apiBody = await client.TestApiAsync(CancellationToken.None);
            AppendLog($"Gemini OK. Response: {TrimForLog(apiBody, 500)}");
            MessageBox.Show(this, "Kết nối Gemini API OK.", "Test Gemini", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test Gemini", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void StartClicked(object? sender, EventArgs e)
    {
        if (_processingCts is not null)
        {
            return;
        }

        _processingCts = new CancellationTokenSource();
        try
        {
            _settings = ReadSettingsFromUi();
            ValidateSqlSettings(_settings);
            ValidateProviderSettings(_settings);
            _settingsStore.Save(_settings);
            InitializeLoggingPath();
            SetRunningState(true);
            await LoadRowsIntoGridAsync(_processingCts.Token);

            if (_rows.Count == 0)
            {
                AppendLog("Không có dòng nào cần xử lý.");
                return;
            }

            GlossaryDictionary? glossary = null;
            if (!string.IsNullOrWhiteSpace(_settings.Processing.DictionaryCsvPath))
            {
                if (File.Exists(_settings.Processing.DictionaryCsvPath))
                {
                    try
                    {
                        glossary = GlossaryDictionary.LoadFromCsv(_settings.Processing.DictionaryCsvPath);
                        AppendLog($"Đã nạp {glossary.Count} mục từ điển từ: {_settings.Processing.DictionaryCsvPath}");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Cảnh báo: Không thể nạp file từ điển CSV ({ex.Message}). Tiếp tục xử lý không kèm từ điển.");
                    }
                }
                else
                {
                    AppendLog($"Cảnh báo: Đường dẫn từ điển CSV không tồn tại: {_settings.Processing.DictionaryCsvPath}");
                }
            }

            using var client = CreateTranslationClient(_settings, glossary, out var timeoutSeconds);
            client.ResponseReceived += AppendAnythingLlmRawResponse;

            AppendLog($"Đã nạp {_rows.Count} dòng. Bắt đầu dịch bằng {_settings.Provider}...");

            var repository = new MssqlTranslationRepository(_settings.SqlServer, _settings.Processing, timeoutSeconds);
            var processor = new TranslationProcessor(repository, client, _settings.Processing, glossary);
            var progress = new Progress<ProcessorProgress>(HandleProgressUpdate);

            await processor.ProcessAsync(_rows, progress, _processingCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Đã hủy xử lý.");
        }
        catch (Exception ex)
        {
            AppendLog($"Lỗi: {ex.Message}");
            MessageBox.Show(this, ex.ToString(), "Processing", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _processingCts?.Dispose();
            _processingCts = null;
            SetRunningState(false);

            if (_pendingClose)
            {
                BeginInvoke(new Action(Close));
            }
        }
    }

    private void StopClicked(object? sender, EventArgs e)
    {
        _processingCts?.Cancel();
        AppendLog("Đã yêu cầu hủy xử lý.");
    }

    private async void ExportClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_rows.Count == 0)
            {
                MessageBox.Show(this, "Chưa có dữ liệu để export.", "Export XLSX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export review rows",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"AnythingLLM_Review_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await Task.Run(() => ExcelExportService.ExportReviewRows(dialog.FileName, _rows.ToList()));
            AppendLog($"Đã export Excel: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export XLSX", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadRowsIntoGridAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettingsFromUi();
        ValidateSqlSettings(settings);

        var repository = new MssqlTranslationRepository(settings.SqlServer, settings.Processing, settings.AnythingLLM.RequestTimeoutSeconds);
        AppendLog("Đang nạp dữ liệu từ SQL...");
        var rows = await repository.LoadRowsAsync(cancellationToken);
        BindRows(rows);
        AppendLog($"Đã nạp {rows.Count} dòng.");
    }

    private void BindRows(IEnumerable<ReviewRowViewModel> rows)
    {
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        _rowIndex.Clear();
        _rowGridIndex.Clear();

        var gridIndex = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Status))
            {
                row.Status = "Loaded";
            }

            _rows.Add(row);
            _rowIndex[row.Id] = row;
            _rowGridIndex[row.Id] = gridIndex++;
        }

        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        UpdateProgress(0, Math.Max(_rows.Count, 1), "Đã nạp dữ liệu.");
    }

    private void HandleProgressUpdate(ProcessorProgress update)
    {
        if (update.IsSummary)
        {
            _statusLabel.Text = update.Message;
            _progressBar.Value = 100;
            _progressPercentLabel.Text = "100%";
            AppendLog(update.Message);
            return;
        }

        if (update.RowId is not null && _rowIndex.TryGetValue(update.RowId.Value, out var row))
        {
            if (update.SuggestedTranslation is not null)
            {
                row.SuggestedTranslation = update.SuggestedTranslation;
            }

            if (update.Rating.HasValue)
            {
                row.Rating = update.Rating;
            }

            if (!string.IsNullOrWhiteSpace(update.Status))
            {
                row.Status = update.Status!;
            }

            row.Error = update.Error;
            SelectGridRow(update.RowId.Value);
        }

        UpdateProgress(update.Completed, update.Total, update.Message);
        if (!string.IsNullOrWhiteSpace(update.Error))
        {
            AppendLog($"{update.Message} | Error: {update.Error}");
        }
        else
        {
            AppendLog(update.Message);
        }
    }

    private void SelectGridRow(long rowId)
    {
        if (!_rowGridIndex.TryGetValue(rowId, out var gridIndex) || gridIndex >= _grid.Rows.Count)
        {
            return;
        }

        var gridRow = _grid.Rows[gridIndex];
        if (!gridRow.Visible || gridRow.Cells.Count == 0)
        {
            return;
        }

        _grid.ClearSelection();
        gridRow.Selected = true;
        _grid.CurrentCell = gridRow.Cells[0];
        _grid.FirstDisplayedScrollingRowIndex = gridIndex;
    }

    private void UpdateProgress(int completed, int total, string message)
    {
        var percent = total <= 0 ? 0 : Math.Clamp((int)Math.Round(completed * 100.0 / total), 0, 100);
        _progressBar.Value = percent;
        _progressPercentLabel.Text = $"{percent}%";
        _statusLabel.Text = message;
    }

    private void SetRunningState(bool running)
    {
        UseWaitCursor = running;
        _settingsTabs.Enabled = !running;
        _btnReload.Enabled = !running;
        _btnSave.Enabled = !running;
        _btnLoad.Enabled = !running;
        _btnTestSql.Enabled = !running;
        _btnTestAnything.Enabled = !running;
        _btnTestGemini.Enabled = !running;
        _btnExport.Enabled = !running;
        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
        _statusLabel.Text = running ? "Đang xử lý..." : "Sẵn sàng";
        _progressPercentLabel.Text = "0%";
        if (running)
        {
            _progressBar.Value = 0;
        }
        else
        {
            _progressBar.Value = 0;
        }
    }

    private static void ValidateSqlSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SqlServer.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.TargetTable))
        {
            throw new InvalidOperationException("TargetTable chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.KeyColumn))
        {
            throw new InvalidOperationException("KeyColumn chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.SourceQuery) &&
            string.IsNullOrWhiteSpace(settings.SqlServer.SourceTable))
        {
            throw new InvalidOperationException("Bạn cần điền SourceTable hoặc SourceQuery.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.SuggestedTranslationColumn))
        {
            throw new InvalidOperationException("SuggestedTranslationColumn chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.RatingColumn))
        {
            throw new InvalidOperationException("RatingColumn chưa được điền.");
        }
    }

    private static void ValidateAnythingSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.BaseUrl))
        {
            throw new InvalidOperationException("AnythingLLM BaseUrl chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.ApiKey))
        {
            throw new InvalidOperationException("AnythingLLM ApiKey chưa được điền.");
        }

        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.WorkspaceSlug))
        {
            throw new InvalidOperationException("AnythingLLM WorkspaceSlug chưa được điền.");
        }
    }


    private static void ValidateProviderSettings(AppSettings settings)
    {
        if (string.Equals(settings.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            ValidateGeminiSettings(settings);
            return;
        }

        ValidateAnythingSettings(settings);
    }

    private static ITranslationClient CreateTranslationClient(AppSettings settings, GlossaryDictionary? glossary, out int timeoutSeconds)
    {
        if (string.Equals(settings.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            timeoutSeconds = settings.Gemini.RequestTimeoutSeconds;
            return new GeminiClient(settings.Gemini, settings.AnythingLLM.PromptTemplate, glossary);
        }

        timeoutSeconds = settings.AnythingLLM.RequestTimeoutSeconds;
        return new AnythingLlmClient(settings.AnythingLLM, glossary);
    }

    private static void ValidateGeminiSettings(AppSettings settings)
    {
        var apiKeys = settings.Gemini.ApiKeys.Count > 0
            ? settings.Gemini.ApiKeys
            : (string.IsNullOrWhiteSpace(settings.Gemini.ApiKey) ? new List<string>() : new List<string> { settings.Gemini.ApiKey });

        var models = settings.Gemini.Models.Count > 0
            ? settings.Gemini.Models
            : (string.IsNullOrWhiteSpace(settings.Gemini.Model) ? new List<string>() : new List<string> { settings.Gemini.Model });

        if (apiKeys.Count == 0 || apiKeys.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Gemini API Key chưa được điền. Vui lòng nhập key trong tab Gemini Online hoặc cấu hình ApiKeys.");
        }

        if (models.Count == 0 || models.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Gemini Model chưa được điền. Vui lòng nhập model hoặc cấu hình Models.");
        }
    }


    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        
        try
        {
            // Kiểm tra xem form và logBox đã được khởi tạo chưa
            if (!IsHandleCreated || IsDisposed || _logBox == null)
            {
                System.Diagnostics.Debug.WriteLine(line);
                return;
            }

            // Nếu gọi từ worker thread, sử dụng BeginInvoke
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AppendLogToBox(line)));
            }
            else
            {
                AppendLogToBox(line);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in AppendLog: {ex.Message}");
        }
    }

    private void AppendLogToBox(string line)
    {
        try
        {
            _logBox.AppendText(line);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error appending to log box: {ex.Message}");
        }
    }

    private void AppendAnythingLlmRawResponse(AnythingLlmApiResponse response)
    {
        if (!_settings.Logging.EnableFileLogging)
        {
            return;
        }

        var logEntry =
            $"======================================== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================{Environment.NewLine}" +
            $"[URL]: {response.RequestUrl}{Environment.NewLine}" +
            $"[INPUT / PROMPT]:{Environment.NewLine}{response.RequestPayload ?? "(N/A)"}{Environment.NewLine}" +
            $"----------------------------------------------------------------------------------------------------{Environment.NewLine}" +
            $"[OUTPUT / RESPONSE]:{Environment.NewLine}{response.Body}{Environment.NewLine}" +
            $"================================================================================--------------------{Environment.NewLine}{Environment.NewLine}";

        try
        {
            if (string.IsNullOrWhiteSpace(_anythingLlmRawLogPath))
            {
                AppendLog("Log path chưa được khởi tạo.");
                return;
            }

            lock (_anythingLlmLogLock)
            {
                var logDir = Path.GetDirectoryName(_anythingLlmRawLogPath);
                if (!string.IsNullOrWhiteSpace(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(_anythingLlmRawLogPath, logEntry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                    AppendLog($"AnythingLLM raw response saved: {_anythingLlmRawLogPath}")));
            }
        }
        catch (Exception ex)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() => AppendLog($"Could not write AnythingLLM raw log: {ex.Message}")));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Error writing log: {ex.Message}");
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_processingCts is not null)
        {
            _pendingClose = true;
            _processingCts.Cancel();
            AppendLog("Đang hủy xử lý trước khi đóng ứng dụng...");
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private static string TrimForLog(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }
}



