
using static AI_Translator.Views.UiFactory;

namespace AI_Translator.Views;

public partial class MainForm
{
    private TabPage _glossaryPage = null!;
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

    private TabPage BuildGlossaryCheckTab()
    {
        var page = new TabPage("Glossary Check") { Padding = new Padding(6) };

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

        // Hàng 1: nguồn dữ liệu + nút kiểm tra
        var sourceGroup = new GroupBox
        {
            Text = "Nguồn kiểm tra",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 6)
        };
        var sourceTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3
        };
        sourceTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sourceTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        sourceTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sourceGroup.Controls.Add(sourceTable);

        _txtGlossaryCheckCsvPath = CreateTextBox();
        _chkScanAllDatabase = CreateOptionCheckBox("Quét toàn bộ CSDL (bỏ chọn = chỉ các dòng đang có ở tab Xử lý)", isChecked: true);
        _chkExcludeLocked = CreateOptionCheckBox("Chỉ dòng chưa khóa (Locked ≠ 1)", isChecked: true);
        _btnRunGlossaryCheck = CreateButton("Kiểm tra từ điển", RunGlossaryCheckClicked, primary: true);
        _btnRunGlossaryCheck.Dock = DockStyle.Fill;
        _btnRunGlossaryCheck.Margin = new Padding(12, 3, 0, 3);

        AddCell(sourceTable, CreateLabel("Từ điển CSV"), 0, 0);
        AddCell(sourceTable, CreateFileBrowser(_txtGlossaryCheckCsvPath, "Chọn file CSV từ điển Anh - Việt"), 1, 0);
        AddCell(sourceTable, _btnRunGlossaryCheck, 2, 0, rowSpan: 2);
        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        options.Controls.AddRange(new Control[] { _chkScanAllDatabase, _chkExcludeLocked });
        AddCell(sourceTable, options, 1, 1);
        layout.Controls.Add(sourceGroup, 0, 0);

        // Hàng 2: thao tác trên kết quả
        _chkOnlyMismatch = CreateOptionCheckBox("Chỉ hiện từ chưa khớp", isChecked: true);
        _chkOnlyMismatch.CheckedChanged += (_, _) => ApplyGlossaryCheckFilter();
        _chkSelectAllGlossaryResults = CreateOptionCheckBox("Chọn tất cả", isChecked: false);
        _chkSelectAllGlossaryResults.CheckedChanged += (_, _) =>
        {
            foreach (var result in _allGlossaryCheckResults)
            {
                result.Selected = _chkSelectAllGlossaryResults.Checked;
            }
            ApplyGlossaryCheckFilter();
        };
        _btnClearGlossaryTranslations = CreateButton("Xóa SuggestedTranslation + Rating", ClearSelectedGlossaryTranslationsClicked);
        _btnClearGlossaryTranslations.Enabled = false;
        _btnExportGlossaryCheck = CreateButton("Xuất Excel...", ExportGlossaryCheckClicked);
        _btnExportGlossaryCheck.Enabled = false;
        _lblGlossaryCheckStatus = CreateStatusLabel("Chưa kiểm tra.");
        layout.Controls.Add(CreateActionBar(
            _chkOnlyMismatch,
            _chkSelectAllGlossaryResults,
            _btnClearGlossaryTranslations,
            _btnExportGlossaryCheck,
            _lblGlossaryCheckStatus), 0, 1);

        _glossaryCheckGrid = CreateGrid(readOnly: false);
        AddSelectColumn(_glossaryCheckGrid, nameof(GlossaryCheckResult.Selected));
        AddTextColumn(_glossaryCheckGrid, "Id", nameof(GlossaryCheckResult.RowId), 70);
        AddTextColumn(_glossaryCheckGrid, "MsgId", nameof(GlossaryCheckResult.MsgId), 220);
        AddTextColumn(_glossaryCheckGrid, "MsgStr (Gốc)", nameof(GlossaryCheckResult.MsgStr), 180);
        AddTextColumn(_glossaryCheckGrid, "SuggestedTranslation", nameof(GlossaryCheckResult.SuggestedTranslation), 220);
        AddTextColumn(_glossaryCheckGrid, "English (Từ điển)", nameof(GlossaryCheckResult.EnglishTerm), 140);
        AddTextColumn(_glossaryCheckGrid, "Vietnamese (Từ điển)", nameof(GlossaryCheckResult.ExpectedVietnamese), 170);
        AddTextColumn(_glossaryCheckGrid, "Từ trùng MsgId/MsgStr", nameof(GlossaryCheckResult.CommonWords), 150);
        AddTextColumn(_glossaryCheckGrid, "Trạng thái", nameof(GlossaryCheckResult.MatchStatus), 100);
        FillLastColumn(_glossaryCheckGrid);

        // Tô màu dòng dựa trên trạng thái khớp
        _glossaryCheckGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _glossaryCheckGrid.Rows.Count)
                return;

            var row = _glossaryCheckGrid.Rows[e.RowIndex];
            if (row.DataBoundItem is GlossaryCheckResult result)
            {
                row.DefaultCellStyle.BackColor = result.IsMatch ? Color.FromArgb(235, 255, 235) : Color.FromArgb(255, 235, 235);
                row.DefaultCellStyle.ForeColor = result.IsMatch ? Color.DarkGreen : Color.DarkRed;
            }
        };
        WireSelectionColumn(_glossaryCheckGrid, nameof(GlossaryCheckResult.Selected),
            () => _btnClearGlossaryTranslations.Enabled = _allGlossaryCheckResults.Any(r => r.Selected));
        layout.Controls.Add(_glossaryCheckGrid, 0, 2);

        return page;
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
                    MessageBox.Show(this, "Chưa có dữ liệu trong bảng. Hãy tích chọn 'Quét toàn bộ CSDL' hoặc nhấn 'Load Pending' ở tab Xử lý dịch.", "Glossary Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

}
