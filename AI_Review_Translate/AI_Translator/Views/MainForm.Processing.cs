using System.ComponentModel;

using static AI_Translator.Views.UiFactory;

namespace AI_Translator.Views;

public partial class MainForm
{
    // Tab Xử lý dịch
    private readonly BindingList<ReviewRowViewModel> _rows = new();
    private readonly Dictionary<long, ReviewRowViewModel> _rowIndex = new();
    private readonly Dictionary<long, int> _rowGridIndex = new();
    private Control _processingOptions = null!;
    private ComboBox _cmbProvider = null!;
    private TextBox _txtDictionaryCsvPath = null!;
    private CheckBox _chkRespectLockedRows = null!;
    private CheckBox _chkSkipExisting = null!;
    private NumericUpDown _nudMaxConcurrentRequests = null!;
    private NumericUpDown _nudRequestsPerMinute = null!;
    private NumericUpDown _nudMaxRows = null!;
    private NumericUpDown _nudDelayMs = null!;
    private NumericUpDown _nudMaxRetries = null!;
    private NumericUpDown _nudRetryDelayMs = null!;
    private Button _btnLoad = null!;
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private Button _btnExport = null!;

    private DataGridView _grid = null!;

    private TabPage BuildProcessingTab()
    {
        var page = new TabPage("Xử lý dịch") { Padding = new Padding(6) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        page.Controls.Add(layout);

        _processingOptions = BuildProcessingOptions();
        layout.Controls.Add(_processingOptions, 0, 0);

        _btnLoad = CreateButton("Load Pending", LoadPendingClicked);
        _btnStart = CreateButton("▶  Bắt đầu dịch", StartClicked, primary: true);
        _btnStop = CreateButton("■  Dừng", StopClicked);
        _btnExport = CreateButton("Export XLSX", ExportClicked);
        _btnStop.Enabled = false;
        _toolTip.SetToolTip(_btnLoad, "Nạp các dòng cần dịch từ SQL vào bảng bên dưới (không gọi AI).");
        _toolTip.SetToolTip(_btnStart, "Lưu cấu hình, nạp dữ liệu và bắt đầu dịch.");
        layout.Controls.Add(CreateActionBar(_btnStart, _btnStop, _btnLoad, _btnExport), 0, 1);

        _grid = CreateGrid(readOnly: true);
        AddTextColumn(_grid, "Id", nameof(ReviewRowViewModel.Id), 70, sortable: false);
        AddTextColumn(_grid, "Status", nameof(ReviewRowViewModel.Status), 100, sortable: false);
        AddTextColumn(_grid, "MsgCtxt", nameof(ReviewRowViewModel.MsgCtxt), 170, sortable: false);
        AddTextColumn(_grid, "MsgId", nameof(ReviewRowViewModel.MsgId), 240, sortable: false);
        AddTextColumn(_grid, "MsgStr", nameof(ReviewRowViewModel.MsgStr), 220, sortable: false);
        AddTextColumn(_grid, "SuggestedTranslation", nameof(ReviewRowViewModel.SuggestedTranslation), 260, sortable: false);
        AddTextColumn(_grid, "Rating", nameof(ReviewRowViewModel.Rating), 60, sortable: false);
        AddTextColumn(_grid, "Locked", nameof(ReviewRowViewModel.TranslationLocked), 60, sortable: false);
        AddTextColumn(_grid, "Error", nameof(ReviewRowViewModel.Error), 240, sortable: false);
        AddTextColumn(_grid, "SourceFilePath", nameof(ReviewRowViewModel.SourceFilePath), 180, sortable: false);
        AddTextColumn(_grid, "ImportedAtUtc", nameof(ReviewRowViewModel.ImportedAtUtc), 140, sortable: false);
        layout.Controls.Add(_grid, 0, 2);

        return page;
    }

    private Control BuildProcessingOptions()
    {
        var group = new GroupBox
        {
            Text = "Tùy chọn xử lý",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 6)
        };

        // 4 cặp nhãn / giá trị trên mỗi hàng
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 8
        };
        for (var i = 0; i < 4; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        }
        group.Controls.Add(table);

        _cmbProvider = CreateComboBox("Gemini", "AnythingLLM");
        _txtDictionaryCsvPath = CreateTextBox();
        _nudMaxConcurrentRequests = CreateNumericUpDown(1, 16, 4);
        _nudRequestsPerMinute = CreateNumericUpDown(0, 1000, 5);
        _nudMaxRows = CreateNumericUpDown(0, 100000, 0);
        _nudDelayMs = CreateNumericUpDown(0, 100000, 0);
        _nudMaxRetries = CreateNumericUpDown(0, 100, 2);
        _nudRetryDelayMs = CreateNumericUpDown(0, 100000, 1000);
        _chkRespectLockedRows = new CheckBox { Text = "Bỏ qua dòng TranslationLocked", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        _chkSkipExisting = new CheckBox { Text = "Bỏ qua dòng đã có SuggestedTranslation", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

        AddCell(table, CreateLabel("AI Provider"), 0, 0);
        AddCell(table, _cmbProvider, 1, 0);
        AddCell(table, CreateLabel("Từ điển CSV"), 2, 0);
        AddCell(table, CreateFileBrowser(_txtDictionaryCsvPath, "Chọn file CSV từ điển Anh - Việt"), 3, 0, columnSpan: 5);

        AddCell(table, CreateLabel("Max concurrent"), 0, 1);
        AddCell(table, _nudMaxConcurrentRequests, 1, 1);
        AddCell(table, CreateLabel("Requests/phút (RPM)"), 2, 1);
        AddCell(table, _nudRequestsPerMinute, 3, 1);
        AddCell(table, CreateLabel("Max rows (0 = tất cả)"), 4, 1);
        AddCell(table, _nudMaxRows, 5, 1);
        AddCell(table, CreateLabel("Delay giữa request (ms)"), 6, 1);
        AddCell(table, _nudDelayMs, 7, 1);

        AddCell(table, CreateLabel("Max retries"), 0, 2);
        AddCell(table, _nudMaxRetries, 1, 2);
        AddCell(table, CreateLabel("Retry delay (ms)"), 2, 2);
        AddCell(table, _nudRetryDelayMs, 3, 2);
        var checkBoxes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        checkBoxes.Controls.AddRange(new Control[] { _chkRespectLockedRows, _chkSkipExisting });
        AddCell(table, checkBoxes, 4, 2, columnSpan: 4);

        _toolTip.SetToolTip(_txtDictionaryCsvPath,
            "File CSV gồm 2 cột (Tiếng Anh, Tiếng Việt). Khi gửi lên AI, chỉ các từ/cụm từ có trong MsgId của dòng đang dịch được đính kèm.");
        _toolTip.SetToolTip(_nudRequestsPerMinute,
            "Gemini Free Tier: đặt RPM = 5 và Max concurrent = 1. Khi gặp lỗi 429 ứng dụng tự chờ và thử lại. 0 = không giới hạn.");

        return group;
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
                FileName = $"AI_Translator_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
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

}
