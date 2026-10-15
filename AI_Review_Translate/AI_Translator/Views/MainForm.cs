using System.Text;
using static AI_Translator.Views.UiFactory;

namespace AI_Translator.Views;

/// <summary>Cửa sổ chính: khung bố cục, nhật ký, thanh trạng thái. Mỗi tab nằm trong một file partial riêng.</summary>
public partial class MainForm : Form
{
    private readonly AppSettingsStore _settingsStore = new();
    private AppSettings _settings = AppSettings.CreateDefault();

    private readonly object _anythingLlmLogLock = new();
    private string _anythingLlmRawLogPath = string.Empty;

    private CancellationTokenSource? _processingCts;
    private bool _pendingClose;

    private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000 };
    private SplitContainer _rootSplit = null!;
    private TabControl _mainTabs = null!;

    private ToolStripButton _btnReload = null!;
    private ToolStripButton _btnSave = null!;
    private TextBox _logBox = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _progressPercentLabel = null!;
    private ToolStripProgressBar _progressBar = null!;

    public MainForm()
    {
        InitializeComponent();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 700);
        Size = new Size(1400, 900);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "AI Translator";
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

    #region Bố cục chính

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();

        // Thứ tự add: Fill trước, Top/Bottom sau
        _rootSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6
        };
        Controls.Add(_rootSplit);

        _mainTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 5)
        };
        _mainTabs.TabPages.Add(BuildProcessingTab());
        _mainTabs.TabPages.Add(_glossaryPage = BuildGlossaryCheckTab());
        _mainTabs.TabPages.Add(_escapePage = BuildEscapeRepairTab());
        _mainTabs.TabPages.Add(_configPage = BuildConfigTab());
        _mainTabs.TabPages.Add(_promptPage = BuildPromptTab());
        _rootSplit.Panel1.Controls.Add(_mainTabs);
        _rootSplit.Panel1.Padding = new Padding(6, 6, 6, 0);

        _rootSplit.Panel2.Controls.Add(BuildLogPanel());
        _rootSplit.Panel2.Padding = new Padding(6, 0, 6, 4);

        Controls.Add(BuildTopToolStrip());
        Controls.Add(BuildStatusStrip());

        ResumeLayout(performLayout: true);
    }

    private ToolStrip BuildTopToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 2, 6, 2),
            RenderMode = ToolStripRenderMode.System
        };

        _btnSave = new ToolStripButton("Lưu cấu hình") { ToolTipText = "Lưu toàn bộ cấu hình vào appsettings.json" };
        _btnSave.Click += SaveConfigClicked;
        _btnReload = new ToolStripButton("Nạp lại cấu hình") { ToolTipText = "Đọc lại appsettings.json (bỏ thay đổi chưa lưu)" };
        _btnReload.Click += ReloadConfigClicked;

        toolStrip.Items.Add(_btnSave);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_btnReload);
        return toolStrip;
    }

    private StatusStrip BuildStatusStrip()
    {
        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            Dock = DockStyle.Bottom
        };
        _statusLabel = new ToolStripStatusLabel
        {
            Text = "Sẵn sàng",
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoToolTip = true
        };
        _progressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            AutoSize = false,
            Size = new Size(220, 18),
            Style = ProgressBarStyle.Continuous
        };
        _progressPercentLabel = new ToolStripStatusLabel
        {
            Text = "0%",
            AutoSize = false,
            Size = new Size(42, 22),
            TextAlign = ContentAlignment.MiddleRight
        };
        statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _progressBar, _progressPercentLabel });
        return statusStrip;
    }

    private Control BuildLogPanel()
    {
        var group = new GroupBox
        {
            Text = "Nhật ký",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6)
        };
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
        group.Controls.Add(_logBox);
        return group;
    }

    private void ApplyInitialSplitterDistances()
    {
        const int logHeight = 170;
        ConfigureSplitterSafely(_rootSplit, 360, 90, _rootSplit.ClientSize.Height - logHeight);
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

    #endregion

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
        foreach (var control in new Control[] { _processingOptions, _glossaryPage, _escapePage, _configPage, _promptPage, _btnLoad, _btnExport, _btnStart })
        {
            control.Enabled = !running;
        }
        _btnSave.Enabled = !running;
        _btnReload.Enabled = !running;
        _btnStop.Enabled = running;
        _btnStop.UseWaitCursor = false;
        _statusLabel.Text = running ? "Đang xử lý..." : "Sẵn sàng";
        _progressPercentLabel.Text = "0%";
        _progressBar.Value = 0;
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
            if (IsDisposed || _logBox == null)
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
