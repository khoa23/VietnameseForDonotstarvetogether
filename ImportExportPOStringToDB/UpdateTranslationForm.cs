using System.ComponentModel;

namespace ImportPOStringToDB;

public sealed class UpdateTranslationForm : Form
{
    private readonly AppSettings _settings;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Button _btnUpdate = null!;
    private Button _btnCancel = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;
    private TextBox _txtResult = null!;
    private CancellationTokenSource? _cancellationTokenSource;

    public UpdateTranslationForm(AppSettings settings)
    {
        _settings = settings;
        InitializeUi();
        UpdateStatus("Sẵn sàng.");
    }

    private void InitializeUi()
    {
        Text = "Cập nhật Bản dịch Từ Database";
        Width = 800;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(600, 400);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        // Row 0: File Path Panel
        var filePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        root.Controls.Add(filePanel, 0, 0);

        var lblFilePath = new Label
        {
            Text = "Đường dẫn file PO:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        filePanel.Controls.Add(lblFilePath, 0, 0);

        _txtFilePath = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = false,
            Text = _settings.SourcePath ?? string.Empty,
            Margin = new Padding(0, 0, 8, 0)
        };
        filePanel.Controls.Add(_txtFilePath, 0, 0);

        _btnBrowse = new Button
        {
            Text = "Chọn File",
            Width = 80,
            Height = 32,
            Dock = DockStyle.Fill
        };
        _btnBrowse.Click += BtnBrowse_Click;
        filePanel.Controls.Add(_btnBrowse, 1, 0);

        // Row 1: Progress Bar
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(_progressBar, 0, 1);

        // Row 2: Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(buttonPanel, 0, 2);

        _btnUpdate = new Button
        {
            Text = "Cập nhật",
            Width = 100,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0)
        };
        _btnUpdate.Click += BtnUpdate_Click;
        buttonPanel.Controls.Add(_btnUpdate);

        _btnCancel = new Button
        {
            Text = "Hủy",
            Width = 100,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0),
            Enabled = false
        };
        _btnCancel.Click += BtnCancel_Click;
        buttonPanel.Controls.Add(_btnCancel);

        // Row 3: Result Text
        var lblResult = new Label
        {
            Text = "Kết quả:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        root.Controls.Add(lblResult, 0, 3);

        _txtResult = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(_txtResult, 0, 3);

        // Row 4: Status Bar
        _lblStatus = new Label
        {
            Text = "Sẵn sàng.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        root.Controls.Add(_lblStatus, 0, 4);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PO Files (*.po)|*.po|All Files (*.*)|*.*",
            InitialDirectory = _settings.SourcePath ?? string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _txtFilePath.Text = dialog.FileName;
        }
    }

    private void BtnUpdate_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtFilePath.Text))
        {
            MessageBox.Show(
                "Vui lòng chọn file PO.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(_txtFilePath.Text))
        {
            MessageBox.Show(
                $"File không tồn tại:\n{_txtFilePath.Text}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _btnUpdate.Enabled = false;
        _btnBrowse.Enabled = false;
        _txtFilePath.ReadOnly = true;
        _btnCancel.Enabled = true;
        _txtResult.Clear();
        _progressBar.Value = 0;

        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<string>(message =>
        {
            UpdateStatus(message);
            AppendResult(message);
        });

        _ = PerformUpdateAsync(progress, _cancellationTokenSource.Token);
    }

    private async Task PerformUpdateAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = _txtFilePath.Text;
            UpdateStatus("Đang xử lý...");

            var result = await Services.UpdateTranslationService.UpdateTranslationsFromDbAsync(
                _settings.ConnectionString,
                filePath,
                progress,
                cancellationToken);

            _progressBar.Value = 100;

            var message = $"\n=== Kết quả ===\n" +
                $"Tổng cộng: {result.TotalCount}\n" +
                $"Đã cập nhật: {result.UpdatedCount}\n" +
                $"Bỏ qua (không có bản dịch đề xuất): {result.SkippedCount}\n" +
                $"Không tìm thấy: {result.NotFoundCount}\n" +
                $"Thông báo: {result.Message}";

            AppendResult(message);
            UpdateStatus(result.Message);

            if (result.UpdatedCount > 0)
            {
                MessageBox.Show(
                    $"Cập nhật thành công {result.UpdatedCount} mục!\n\n" +
                    $"Chi tiết:\n" +
                    $"Bỏ qua: {result.SkippedCount}\n" +
                    $"Không tìm thấy: {result.NotFoundCount}",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    result.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Đã hủy.");
            AppendResult("Thao tác bị hủy bởi người dùng.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Lỗi: {ex.Message}");
            AppendResult($"\nLỗi: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show(
                $"Lỗi:\n{ex.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _btnUpdate.Enabled = true;
            _btnBrowse.Enabled = true;
            _txtFilePath.ReadOnly = false;
            _btnCancel.Enabled = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        UpdateStatus("Đang hủy...");
    }

    private void UpdateStatus(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(message));
            return;
        }

        _lblStatus.Text = message;
    }

    private void AppendResult(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendResult(message));
            return;
        }

        if (_txtResult.Text.Length > 0)
        {
            _txtResult.AppendText($"\n{message}");
        }
        else
        {
            _txtResult.Text = message;
        }
    }
}
