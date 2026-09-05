using System.ComponentModel;
using System.Drawing;
using System.Text;

namespace AnythingLLMReviewTranslator;

public partial class Form1 : Form
{
    private readonly AppSettingsStore _settingsStore = new();
    private AppSettings _settings = AppSettings.CreateDefault();

    private readonly BindingList<ReviewRowViewModel> _rows = new();
    private readonly BindingSource _rowsBindingSource = new();
    private readonly Dictionary<long, ReviewRowViewModel> _rowIndex = new();
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

        _rowsBindingSource.DataSource = _rows;
        _grid.DataSource = _rowsBindingSource;
    }

    private void InitializeLoggingPath()
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, _settings.Logging.LogDirectory);
        _anythingLlmRawLogPath = Path.Combine(logDirectory, $"anythingllm-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();

        _rootSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1
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
        AddNoteRow(table, ref row, "Tip: náº¿u query cá»§a báº¡n Ä‘Ã£ alias Ä‘Ãºng tÃªn cá»™t, chá»‰ cáº§n Ä‘á»•i SourceQuery vÃ  TargetTable.");

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
        AddNoteRow(table, ref row, "Khuyáº¿n nghá»‹ dÃ¹ng mode = query cho bÃ i toÃ¡n dá»‹ch thuáº§n. Chuyá»ƒn sang chat náº¿u workspace cáº§n RAG.");

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
            // â”€â”€ OpenRouter: Free models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            "deepseek/deepseek-r1-0528:free",
            "deepseek/deepseek-chat-v3-0324:free",
            "google/gemini-2.5-flash:free",
            "google/gemini-2.0-flash-001:free",
            "microsoft/phi-4-reasoning:free",
            "qwen/qwen3-235b-a22b:free",
            "meta-llama/llama-4-maverick:free",
            // â”€â”€ OpenRouter: Paid models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            "deepseek/deepseek-r1",
            "deepseek/deepseek-chat",
            "google/gemini-2.5-flash",
            "anthropic/claude-sonnet-4-5",
            "openai/gpt-4o-mini",
            // â”€â”€ Google Gemini trá»±c tiáº¿p (thay BaseURL) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            "gemini-2.5-flash",
            "gemini-2.0-flash",
            "gemini-1.5-flash"
        });
        AddFullWidthRow(table, ref row, "API Base URL", out _txtGeminiBaseUrl, height: 30);
        AddNoteRow(table, ref row,
            "OpenRouter (https://openrouter.ai/api/v1): Há»— trá»£ DeepSeek, Gemini, Claude,... Model cÃ³ :free = miá»…n phÃ­. " +
            "Google Gemini trá»±c tiáº¿p: Ä‘á»•i URL thÃ nh https://generativelanguage.googleapis.com vÃ  dÃ¹ng model khÃ´ng prefix.");

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
        AddNoteRow(table, ref row, "Máº¹o: Vá»›i Gemini Free Tier (limit 5 request/phÃºt), hÃ£y Ä‘áº·t RPM = 5 vÃ  Max concurrent = 1. Khi dÃ­nh 429, á»©ng dá»¥ng sáº½ tá»± Ä‘á»™ng chá» vÃ  thá»­ láº¡i.");

        return page;
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
            AppendLog("ÄÃ£ náº¡p láº¡i cáº¥u hÃ¬nh tá»« appsettings.json.");
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
            AppendLog($"ÄÃ£ lÆ°u cáº¥u hÃ¬nh vÃ o {_settingsStore.FilePath}.");
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
            AppendLog("Káº¿t ná»‘i SQL OK.");
            MessageBox.Show(this, "Káº¿t ná»‘i SQL OK.", "Test SQL", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(this, "Káº¿t ná»‘i AnythingLLM OK.", "Test AnythingLLM", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            using var client = CreateTranslationClient(_settings, out var timeoutSeconds);
            client.ResponseReceived += AppendAnythingLlmRawResponse;

            AppendLog($"Đã nạp {_rows.Count} dòng. Bắt đầu dịch bằng {_settings.Provider}...");

            var repository = new MssqlTranslationRepository(_settings.SqlServer, _settings.Processing, timeoutSeconds);
            var processor = new TranslationProcessor(repository, client, _settings.Processing);
            var progress = new Progress<ProcessorProgress>(HandleProgressUpdate);

            await processor.ProcessAsync(_rows, progress, _processingCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("ÄÃ£ há»§y xá»­ lÃ½.");
        }
        catch (Exception ex)
        {
            AppendLog($"Lá»—i: {ex.Message}");
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
        AppendLog("ÄÃ£ yÃªu cáº§u há»§y xá»­ lÃ½.");
    }

    private async void ExportClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_rows.Count == 0)
            {
                MessageBox.Show(this, "ChÆ°a cÃ³ dá»¯ liá»‡u Ä‘á»ƒ export.", "Export XLSX", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            AppendLog($"ÄÃ£ export Excel: {dialog.FileName}");
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
        AppendLog("Äang náº¡p dá»¯ liá»‡u tá»« SQL...");
        var rows = await repository.LoadRowsAsync(cancellationToken);
        BindRows(rows);
        AppendLog($"ÄÃ£ náº¡p {rows.Count} dÃ²ng.");
    }

    private void BindRows(IEnumerable<ReviewRowViewModel> rows)
    {
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        _rowIndex.Clear();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Status))
            {
                row.Status = "Loaded";
            }

            _rows.Add(row);
            _rowIndex[row.Id] = row;
        }

        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        UpdateProgress(0, Math.Max(_rows.Count, 1), "ÄÃ£ náº¡p dá»¯ liá»‡u.");
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
        _statusLabel.Text = running ? "Äang xá»­ lÃ½..." : "Sáºµn sÃ ng";
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
            throw new InvalidOperationException("ConnectionString chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.TargetTable))
        {
            throw new InvalidOperationException("TargetTable chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.KeyColumn))
        {
            throw new InvalidOperationException("KeyColumn chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.SourceQuery) &&
            string.IsNullOrWhiteSpace(settings.SqlServer.SourceTable))
        {
            throw new InvalidOperationException("Báº¡n cáº§n Ä‘iá»n SourceTable hoáº·c SourceQuery.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.SuggestedTranslationColumn))
        {
            throw new InvalidOperationException("SuggestedTranslationColumn chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.SqlServer.RatingColumn))
        {
            throw new InvalidOperationException("RatingColumn chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }
    }

    private static void ValidateAnythingSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.BaseUrl))
        {
            throw new InvalidOperationException("AnythingLLM BaseUrl chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.ApiKey))
        {
            throw new InvalidOperationException("AnythingLLM ApiKey chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
        }

        if (string.IsNullOrWhiteSpace(settings.AnythingLLM.WorkspaceSlug))
        {
            throw new InvalidOperationException("AnythingLLM WorkspaceSlug chÆ°a Ä‘Æ°á»£c Ä‘iá»n.");
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

    private static ITranslationClient CreateTranslationClient(AppSettings settings, out int timeoutSeconds)
    {
        if (string.Equals(settings.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            timeoutSeconds = settings.Gemini.RequestTimeoutSeconds;
            return new GeminiClient(settings.Gemini, settings.AnythingLLM.PromptTemplate);
        }

        timeoutSeconds = settings.AnythingLLM.RequestTimeoutSeconds;
        return new AnythingLlmClient(settings.AnythingLLM);
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



