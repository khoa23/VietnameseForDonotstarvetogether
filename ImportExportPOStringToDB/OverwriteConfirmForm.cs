using System.ComponentModel;
using ImportPOStringToDB.Models;

namespace ImportPOStringToDB;

public class OverwriteItemModel : INotifyPropertyChanged
{
    private bool _shouldOverwrite = true;
    private string _msgId = string.Empty;
    private string _oldMsgStr = string.Empty;
    private string _newMsgStr = string.Empty;

    public bool ShouldOverwrite
    {
        get => _shouldOverwrite;
        set
        {
            if (_shouldOverwrite != value)
            {
                _shouldOverwrite = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldOverwrite)));
            }
        }
    }

    public PoTranslation ExistingInDb { get; set; } = null!;
    public PoTranslation NewFromPo { get; set; } = null!;

    public bool IsLocked { get; set; }

    public string MsgId
    {
        get => !string.IsNullOrEmpty(_msgId) ? _msgId : (NewFromPo?.MsgId ?? string.Empty);
        set => _msgId = value;
    }

    public string OldMsgStr
    {
        get => !string.IsNullOrEmpty(_oldMsgStr) ? _oldMsgStr : (ExistingInDb?.MsgStr ?? string.Empty);
        set => _oldMsgStr = value;
    }

    public string NewMsgStr
    {
        get => !string.IsNullOrEmpty(_newMsgStr) ? _newMsgStr : (NewFromPo?.MsgStr ?? string.Empty);
        set => _newMsgStr = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class OverwriteConfirmForm : Form
{
    private readonly List<OverwriteItemModel> _allAvailableItems;
    private BindingList<OverwriteItemModel> _displayedItems = new();
    private readonly BindingSource _bindingSource = new();
    private DataGridView _grid = null!;
    private CheckBox _chkShowLocked = null!;
    private Label _lblHeader = null!;
    private DataGridViewCheckBoxColumn _colLocked = null!;

    public List<PoTranslation> SelectedItemsToOverwrite =>
        _displayedItems.Where(x => x.ShouldOverwrite).Select(x => x.NewFromPo).ToList();

    public List<PoTranslation> UnselectedItemsToLock =>
        _displayedItems.Where(x => !x.ShouldOverwrite).Select(x => x.NewFromPo).ToList();

    public OverwriteConfirmForm(List<OverwriteItemModel> items)
    {
        _allAvailableItems = items;
        InitializeUi();
        ApplyFilter();
    }

    private void InitializeUi()
    {
        Text = "Xác nhận ghi đè bản dịch Tiếng Việt (MsgStr)";
        Width = 1200;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
        Controls.Add(mainLayout);

        var topPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        _lblHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };

        _chkShowLocked = new CheckBox
        {
            Text = "Hiện các dòng đã khóa (TranslationLocked / Rating = 0)",
            Dock = DockStyle.Bottom,
            Height = 24,
            Checked = false,
            Font = new Font("Segoe UI", 9F)
        };
        _chkShowLocked.CheckedChanged += (_, _) => ApplyFilter();

        topPanel.Controls.Add(_chkShowLocked);
        topPanel.Controls.Add(_lblHeader);
        mainLayout.Controls.Add(topPanel, 0, 0);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(OverwriteItemModel.ShouldOverwrite),
            HeaderText = "Ghi đè",
            FillWeight = 8F
        });

        _colLocked = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(OverwriteItemModel.IsLocked),
            HeaderText = "Đã khóa (DB)",
            ReadOnly = true,
            Visible = false,
            FillWeight = 10F
        };
        _grid.Columns.Add(_colLocked);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OverwriteItemModel.MsgId),
            HeaderText = "msgid (Tiếng Anh)",
            ReadOnly = true,
            FillWeight = 32F
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OverwriteItemModel.OldMsgStr),
            HeaderText = "Tiếng Việt Cũ (DB)",
            ReadOnly = true,
            FillWeight = 30F
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OverwriteItemModel.NewMsgStr),
            HeaderText = "Tiếng Việt Mới (File PO)",
            ReadOnly = true,
            FillWeight = 30F
        });

        _grid.DataSource = _bindingSource;
        mainLayout.Controls.Add(_grid, 0, 1);

        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var btnSelectAll = new Button
        {
            Text = "Chọn tất cả",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        btnSelectAll.Click += (_, _) => SetAllCheck(true);

        var btnDeselectAll = new Button
        {
            Text = "Bỏ chọn tất cả",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        };
        btnDeselectAll.Click += (_, _) => SetAllCheck(false);

        var btnOk = new Button
        {
            Text = "Đồng ý Import",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Margin = new Padding(20, 6, 8, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        var btnCancel = new Button
        {
            Text = "Hủy thao tác",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        bottomPanel.Controls.Add(btnSelectAll);
        bottomPanel.Controls.Add(btnDeselectAll);
        bottomPanel.Controls.Add(btnOk);
        bottomPanel.Controls.Add(btnCancel);

        mainLayout.Controls.Add(bottomPanel, 0, 2);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void ApplyFilter()
    {
        var showLocked = _chkShowLocked.Checked;
        _colLocked.Visible = showLocked;

        var filtered = showLocked
            ? _allAvailableItems
            : _allAvailableItems.Where(x => !x.IsLocked).ToList();

        var batch = filtered.Take(50).ToList();
        _displayedItems = new BindingList<OverwriteItemModel>(batch);
        _bindingSource.DataSource = _displayedItems;

        var totalMatching = filtered.Count;
        if (totalMatching > batch.Count)
        {
            _lblHeader.Text = $"Phát hiện {totalMatching:N0} dòng thay đổi. Đang hiển thị {batch.Count:N0} dòng (đợt 50 dòng) để duyệt. Chọn các dòng bạn muốn ghi đè vào DB:";
        }
        else if (batch.Count > 0)
        {
            _lblHeader.Text = $"Phát hiện {batch.Count:N0} dòng thay đổi. Chọn các dòng bạn muốn ghi đè vào DB:";
        }
        else
        {
            _lblHeader.Text = showLocked
                ? "Không có dòng nào thay đổi."
                : "Không có dòng mới chưa khóa. (Tick 'Hiện các dòng đã khóa' ở dưới để xem các dòng đã khóa)";
        }
    }

    private void SetAllCheck(bool check)
    {
        foreach (var item in _displayedItems)
        {
            item.ShouldOverwrite = check;
        }
        _bindingSource.ResetBindings(false);
    }
}
