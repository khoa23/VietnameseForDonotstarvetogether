using System.ComponentModel;
using ImportPOStringToDB.Models;
using ImportPOStringToDB.Services;

namespace ImportPOStringToDB;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private BindingList<PoTranslation> _rows = new();

    private readonly BindingSource _bindingSource = new();
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Button _btnImport = null!;
    private Label _lblStatus = null!;
    private Label _lblCount = null!;
    private DataGridView _grid = null!;
    private TextBox _txtAllText = null!;

    public MainForm(AppSettings settings)
    {
        _settings = settings;

        InitializeUi();
        UpdateStatus("Sẵn sàng.");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_grid.ContainsFocus && HandleTranslationLockShortcut(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void InitializeUi()
    {
        Text = "Import PO String To DB";
        Width = 1500;
        Height = 920;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
        Controls.Add(root);

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(topPanel, 0, 0);

        _txtFilePath = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            PlaceholderText = "Chọn file .po..."
        };
        topPanel.Controls.Add(_txtFilePath, 0, 0);

        _btnBrowse = new Button
        {
            Text = "Chọn file .po",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnBrowse.Click += async (_, _) => await BrowseAndLoadAsync();
        topPanel.Controls.Add(_btnBrowse, 1, 0);

        _btnImport = new Button
        {
            Text = "Import DB",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
            Enabled = false
        };
        _btnImport.Click += async (_, _) => await ImportAsync();
        topPanel.Controls.Add(_btnImport, 2, 0);

        var btnUpdateTranslation = new Button
        {
            Text = "Cập nhật từ DB",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0)
        };
        btnUpdateTranslation.Click += (_, _) => ShowUpdateTranslationForm();
        topPanel.Controls.Add(btnUpdateTranslation, 3, 0);

        _lblCount = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(12, 0, 0, 0)
        };
        topPanel.Controls.Add(_lblCount, 4, 0);

        _lblStatus = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        topPanel.SetColumnSpan(_lblStatus, 5);
        topPanel.Controls.Add(_lblStatus, 0, 1);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.SelectionChanged += (_, _) => UpdateAllTextPreview();
        _grid.DataBindingComplete += (_, _) => SelectFirstRowIfAny();

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.AllText),
            HeaderText = "AllText",
            Visible = false
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.MsgCtxt),
            HeaderText = "msgctxt",
            Visible = false
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.MsgId),
            HeaderText = "msgid (tiếng Anh)",
            ReadOnly = true,
            FillWeight = 28F
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.MsgStr),
            HeaderText = "msgstr (tiếng Việt)",
            ReadOnly = true,
            FillWeight = 28F
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.SuggestedTranslation),
            HeaderText = "Bản dịch đề xuất",
            FillWeight = 28F
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.Rating),
            HeaderText = "Điểm đánh giá",
            FillWeight = 16F,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "0.##"
            }
        });

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.TranslationLocked),
            HeaderText = "Translation Locked",
            FillWeight = 14F,
            TrueValue = true,
            FalseValue = false
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.SourceFilePath),
            HeaderText = "SourceFilePath",
            Visible = false
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.ImportedAtUtc),
            HeaderText = "ImportedAtUtc",
            Visible = false
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PoTranslation.LastUpdated),
            HeaderText = "LastUpdated",
            Visible = false
        });

        root.Controls.Add(_grid, 0, 1);

        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "AllText - cột chứa tất cả"
        };

        _txtAllText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F)
        };

        group.Controls.Add(_txtAllText);
        root.Controls.Add(group, 0, 2);

        _bindingSource.DataSource = _rows;
        _grid.DataSource = _bindingSource;

        Shown += (_, _) => LoadInitialFileIfPresent();
    }

    private void LoadInitialFileIfPresent()
    {
        var defaultPoPath = FindDefaultPoFile();
        if (defaultPoPath is null)
        {
            UpdateStatus($"Config: {_settings.SourcePath}");
            return;
        }

        _txtFilePath.Text = defaultPoPath;
        _ = LoadPoFileAsync(defaultPoPath);
    }

    private string? FindDefaultPoFile()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "vietnamese_1407.po");
        candidate = Path.GetFullPath(candidate);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.Combine(AppContext.BaseDirectory, "vietnamese_1407.po");
        candidate = Path.GetFullPath(candidate);
        return File.Exists(candidate) ? candidate : null;
    }

    private async Task BrowseAndLoadAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PO files (*.po)|*.po|All files (*.*)|*.*",
            Title = "Chọn file .po",
            InitialDirectory = GetInitialDirectory()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await LoadPoFileAsync(dialog.FileName);
    }

    private string GetInitialDirectory()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return Directory.Exists(sourceRoot) ? sourceRoot : AppContext.BaseDirectory;
    }

    private async Task LoadPoFileAsync(string filePath)
    {
        SetBusy(true);
        try
        {
            UpdateStatus($"Đang đọc file: {filePath}");
            _txtFilePath.Text = filePath;

            var parsedRows = await Task.Run(() => PoParser.Parse(filePath, _settings.SkipHeaderEntry));

            _rows = new BindingList<PoTranslation>(parsedRows);
            _bindingSource.DataSource = _rows;
            _grid.DataSource = _bindingSource;

            _lblCount.Text = $"Số dòng import: {_rows.Count:N0}";
            UpdateStatus($"Đã đọc {_rows.Count:N0} bản ghi từ file: {Path.GetFileName(filePath)}");
            _btnImport.Enabled = _rows.Count > 0;

            if (_rows.Count == 0)
            {
                _txtAllText.Clear();
            }
        }
        catch (Exception ex)
        {
            _rows = new BindingList<PoTranslation>();
            _bindingSource.DataSource = _rows;
            _grid.DataSource = _bindingSource;
            _btnImport.Enabled = false;
            _lblCount.Text = string.Empty;
            _txtAllText.Clear();
            MessageBox.Show(this, ex.Message, "Đọc file PO thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus("Không đọc được file PO.");
        }
        finally
        {
            SetBusy(false);
            UpdateAllTextPreview();
        }
    }

    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtFilePath.Text) || _rows.Count == 0)
        {
            MessageBox.Show(this, "Hãy chọn file .po trước khi import.", "Import PO String To DB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        const int batchSize = 50;
        int totalNewImported = 0;
        int totalOverwritten = 0;
        int totalLocked = 0;
        bool isFirstBatch = true;

        while (true)
        {
            SetBusy(true);
            try
            {
                UpdateStatus("Đang kiểm tra và so sánh dữ liệu với SQL Server...");

                var comparison = await PoImportService.CompareAsync(
                    _settings.ConnectionString,
                    _rows,
                    _txtFilePath.Text,
                    includeLocked: true);

                var changedToUpdate = new List<PoTranslation>();
                var itemsToLock = new List<PoTranslation>();
                var newItemsToImport = isFirstBatch ? comparison.NewItems : new List<PoTranslation>();

                if (comparison.ChangedCount > 0)
                {
                    var overwriteModels = comparison.ChangedItems
                        .Select(x => new OverwriteItemModel
                        {
                            ShouldOverwrite = true,
                            ExistingInDb = x.ExistingInDb,
                            NewFromPo = x.NewFromPo,
                            IsLocked = x.ExistingInDb.TranslationLocked || (x.ExistingInDb.Rating.HasValue && x.ExistingInDb.Rating.Value == 0),
                            MsgId = x.NewFromPo.MsgId,
                            OldMsgStr = x.ExistingInDb.MsgStr,
                            NewMsgStr = x.NewFromPo.MsgStr
                        })
                        .ToList();

                    using var dialog = new OverwriteConfirmForm(overwriteModels);
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        UpdateStatus("Đã dừng thao tác Import.");
                        break;
                    }

                    changedToUpdate = dialog.SelectedItemsToOverwrite;
                    itemsToLock = dialog.UnselectedItemsToLock;
                }

                if (newItemsToImport.Count == 0 && changedToUpdate.Count == 0 && itemsToLock.Count == 0)
                {
                    if (isFirstBatch)
                    {
                        MessageBox.Show(
                            this,
                            $"Không có dữ liệu mới hoặc dữ liệu cần ghi đè.\n\n" +
                            $"• Tổng số dòng trong file: {_rows.Count:N0}\n" +
                            $"• Đã trùng khớp / đã khóa trong DB: {comparison.UnchangedCount:N0}",
                            "Kết quả so sánh Import",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        UpdateStatus("Dữ liệu trong DB đã đồng bộ hoàn toàn với file PO.");
                    }
                    break;
                }

                UpdateStatus("Đang lưu thay đổi vào SQL Server...");

                var affected = await PoImportService.ExecuteImportAsync(
                    _settings.ConnectionString,
                    newItemsToImport,
                    changedToUpdate,
                    itemsToLock);

                totalNewImported += newItemsToImport.Count;
                totalOverwritten += changedToUpdate.Count;
                totalLocked += itemsToLock.Count;
                isFirstBatch = false;

                var processedInThisBatch = changedToUpdate.Count + itemsToLock.Count;
                var remainingChanged = comparison.ChangedCount - processedInThisBatch;

                if (remainingChanged > 0)
                {
                    var proceedNext = MessageBox.Show(
                        this,
                        $"Đã xử lý xong {processedInThisBatch} dòng trong đợt này (Ghi đè: {changedToUpdate.Count:N0}, Khóa/Rating=0: {itemsToLock.Count:N0}).\n\n" +
                        $"Còn lại: {remainingChanged:N0} dòng thay đổi chưa duyệt.\n\n" +
                        "Bạn có muốn tiếp tục duyệt 50 dòng tiếp theo không?",
                        "Tiếp tục đợt tiếp theo",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (proceedNext == DialogResult.Yes)
                    {
                        continue;
                    }
                }

                var msg = $"Import hoàn tất!\n\n" +
                          (totalNewImported > 0 ? $"• Thêm mới: {totalNewImported:N0} dòng\n" : "") +
                          $"• Ghi đè: {totalOverwritten:N0} dòng\n" +
                          (totalLocked > 0 ? $"• Khóa / Rating=0 (Bỏ qua): {totalLocked:N0} dòng\n" : "") +
                          (remainingChanged > 0 ? $"• Còn lại chưa duyệt: {remainingChanged:N0} dòng\n" : "") +
                          $"• Đã đồng bộ / Giữ nguyên: {comparison.UnchangedCount:N0} dòng";

                UpdateStatus($"Import xong: Đã cập nhật thành công.");
                MessageBox.Show(this, msg, "Import PO String To DB", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Import thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Import thất bại.");
                break;
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _btnBrowse.Enabled = !busy;
        _btnImport.Enabled = !busy && _rows.Count > 0;
        _grid.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void UpdateStatus(string message)
    {
        _lblStatus.Text = $"{message} | Config: {_settings.SourcePath} | DB: {MaskConnectionString(_settings.ConnectionString)}";
    }

    private static string MaskConnectionString(string connectionString)
    {
        var pieces = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
                pieces[i].StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
            {
                pieces[i] = "Password=***";
            }
        }

        return string.Join("; ", pieces);
    }

    private void SelectFirstRowIfAny()
    {
        if (_grid.Rows.Count == 0)
        {
            _txtAllText.Clear();
            return;
        }

        if (_grid.SelectedRows.Count == 0)
        {
            _grid.ClearSelection();
            _grid.Rows[0].Selected = true;
        }

        UpdateAllTextPreview();
    }

    private void UpdateAllTextPreview()
    {
        if (_grid.CurrentRow?.DataBoundItem is PoTranslation row)
        {
            _txtAllText.Text = row.AllText;
            return;
        }

        if (_grid.SelectedRows.Count > 0 &&
            _grid.SelectedRows[0].DataBoundItem is PoTranslation selected)
        {
            _txtAllText.Text = selected.AllText;
            return;
        }

        _txtAllText.Clear();
    }

    private bool HandleTranslationLockShortcut(Keys keyData)
    {
        if (keyData != Keys.Enter &&
            keyData != (Keys.Control | Keys.Enter) &&
            keyData != (Keys.Control | Keys.Shift | Keys.Enter))
        {
            return false;
        }

        var targetRows = GetLockTargetRows();
        if (targetRows.Count == 0)
        {
            return false;
        }

        if (keyData == (Keys.Control | Keys.Enter))
        {
            SetTranslationLocked(targetRows, true);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Enter))
        {
            SetTranslationLocked(targetRows, false);
            return true;
        }

        var reference = _grid.CurrentRow?.DataBoundItem as PoTranslation ?? targetRows[0];
        SetTranslationLocked(targetRows, !reference.TranslationLocked);
        return true;
    }

    private List<PoTranslation> GetLockTargetRows()
    {
        var selectedRows = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .OfType<PoTranslation>()
            .ToList();

        if (selectedRows.Count > 0)
        {
            return selectedRows;
        }

        return _grid.CurrentRow?.DataBoundItem is PoTranslation current
            ? new List<PoTranslation> { current }
            : [];
    }

    private void SetTranslationLocked(IEnumerable<PoTranslation> rows, bool locked)
    {
        foreach (var row in rows)
        {
            row.TranslationLocked = locked;
        }

        _bindingSource.ResetBindings(false);
        _grid.Refresh();
        UpdateAllTextPreview();
    }

    private void ShowUpdateTranslationForm()
    {
        using var form = new UpdateTranslationForm(_settings);
        form.ShowDialog(this);
    }
}
