
using static AI_Translator.Views.UiFactory;

namespace AI_Translator.Views;

public partial class MainForm
{
    private TabPage _escapePage = null!;
    private DataGridView _escapeRepairGrid = null!;
    private Button _btnScanEscapeIssues = null!;
    private Button _btnApplyEscapeRepairs = null!;
    private CheckBox _chkExcludeLockedEscapeRows = null!;
    private CheckBox _chkSelectAllEscapeRows = null!;
    private Label _lblEscapeRepairStatus = null!;
    private List<EscapeRepairResult> _allEscapeRepairResults = new();

    private TabPage BuildEscapeRepairTab()
    {
        var page = new TabPage("Sửa escape") { Padding = new Padding(6) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        page.Controls.Add(layout);

        _btnScanEscapeIssues = CreateButton("Quét lỗi escape", ScanEscapeIssuesClicked, primary: true);
        _chkExcludeLockedEscapeRows = CreateOptionCheckBox("Bỏ qua dòng đã khóa", isChecked: true);
        _chkSelectAllEscapeRows = CreateOptionCheckBox("Chọn tất cả", isChecked: false);
        _chkSelectAllEscapeRows.CheckedChanged += (_, _) =>
        {
            foreach (var result in _allEscapeRepairResults)
            {
                result.Selected = _chkSelectAllEscapeRows.Checked;
            }
            RefreshEscapeRepairGrid();
        };
        _btnApplyEscapeRepairs = CreateButton("Sửa các dòng đã chọn", ApplyEscapeRepairsClicked);
        _btnApplyEscapeRepairs.Enabled = false;
        _lblEscapeRepairStatus = CreateStatusLabel("Chưa quét.");
        layout.Controls.Add(CreateActionBar(
            _btnScanEscapeIssues,
            _chkExcludeLockedEscapeRows,
            _chkSelectAllEscapeRows,
            _btnApplyEscapeRepairs,
            _lblEscapeRepairStatus), 0, 0);

        _escapeRepairGrid = CreateGrid(readOnly: false);
        AddSelectColumn(_escapeRepairGrid, nameof(EscapeRepairResult.Selected));
        AddTextColumn(_escapeRepairGrid, "Id", nameof(EscapeRepairResult.RowId), 75);
        AddTextColumn(_escapeRepairGrid, "MsgId", nameof(EscapeRepairResult.MsgId), 260);
        AddTextColumn(_escapeRepairGrid, "SuggestedTranslation hiện tại", nameof(EscapeRepairResult.OriginalTranslation), 280);
        AddTextColumn(_escapeRepairGrid, "Bản sau khi sửa", nameof(EscapeRepairResult.RepairedTranslation), 280);
        AddTextColumn(_escapeRepairGrid, "Lỗi phát hiện", nameof(EscapeRepairResult.Issues), 160);
        FillLastColumn(_escapeRepairGrid);
        WireSelectionColumn(_escapeRepairGrid, nameof(EscapeRepairResult.Selected),
            () => _btnApplyEscapeRepairs.Enabled = _allEscapeRepairResults.Any(result => result.Selected));
        layout.Controls.Add(_escapeRepairGrid, 0, 1);

        return page;
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

}
