namespace AI_Translator.Views;

/// <summary>Các hàm dựng control dùng chung cho mọi tab.</summary>
internal static class UiFactory
{
    public static readonly Color NoteColor = Color.DimGray;

    public static DataGridView CreateGrid(bool readOnly)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = readOnly,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            Margin = new Padding(0, 4, 0, 0)
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 251);
        grid.RowTemplate.Height = 24;
        return grid;
    }

    public static void AddTextColumn(DataGridView grid, string headerText, string dataPropertyName, int width, bool sortable = true)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = headerText,
            DataPropertyName = dataPropertyName,
            Width = width,
            ReadOnly = true,
            SortMode = sortable ? DataGridViewColumnSortMode.Automatic : DataGridViewColumnSortMode.NotSortable
        });
    }

    public static void FillLastColumn(DataGridView grid)
    {
        var last = grid.Columns[grid.Columns.Count - 1];
        last.MinimumWidth = last.Width;
        last.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    public static void AddSelectColumn(DataGridView grid, string dataPropertyName)
    {
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Chọn",
            DataPropertyName = dataPropertyName,
            Width = 50,
            ReadOnly = false
        });
    }

    /// <summary>Commit ngay khi tick checkbox và gọi lại <paramref name="onChanged"/>.</summary>
    public static void WireSelectionColumn(DataGridView grid, string dataPropertyName, Action onChanged)
    {
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                grid.Columns[e.ColumnIndex].DataPropertyName == dataPropertyName)
            {
                onChanged();
            }
        };
    }

    public static FlowLayoutPanel CreateActionBar(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 2),
            Margin = new Padding(0)
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static Button CreateButton(string text, EventHandler clickHandler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 2, 10, 2)
        };
        if (primary)
        {
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        }
        button.Click += clickHandler;
        return button;
    }

    public static CheckBox CreateOptionCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = isChecked,
            Margin = new Padding(4, 7, 12, 0)
        };
    }

    public static Label CreateStatusLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = NoteColor,
            Margin = new Padding(8, 8, 0, 0)
        };
    }

    public static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(3, 7, 8, 3)
        };
    }

    public static TextBox CreateTextBox(int multilineHeight = 0, bool password = false)
    {
        var multiline = multilineHeight > 0;
        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            AcceptsReturn = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            UseSystemPasswordChar = password
        };
        if (multiline)
        {
            textBox.Height = multilineHeight;
        }
        return textBox;
    }

    public static NumericUpDown CreateNumericUpDown(decimal minimum, decimal maximum, decimal value)
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Min(Math.Max(value, minimum), maximum),
            TextAlign = HorizontalAlignment.Right,
            ThousandsSeparator = true
        };
    }

    public static ComboBox CreateComboBox(params object[] items)
    {
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        comboBox.Items.AddRange(items);
        return comboBox;
    }

    public static Control CreateFileBrowser(TextBox textBox, string dialogTitle)
    {
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
            Margin = new Padding(4, 2, 3, 2)
        };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = dialogTitle
            };
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                try
                {
                    var dir = Path.GetDirectoryName(textBox.Text);
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

            if (dialog.ShowDialog(textBox.FindForm()) == DialogResult.OK)
            {
                textBox.Text = dialog.FileName;
            }
        };

        textBox.Margin = new Padding(3, 4, 0, 3);
        panel.Controls.Add(textBox, 0, 0);
        panel.Controls.Add(browseButton, 1, 0);
        return panel;
    }

    public static void AddCell(TableLayoutPanel table, Control control, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        table.Controls.Add(control, column, row);
        if (columnSpan > 1)
        {
            table.SetColumnSpan(control, columnSpan);
        }
        if (rowSpan > 1)
        {
            table.SetRowSpan(control, rowSpan);
        }
    }

    /// <summary>GroupBox chứa bảng nhãn/giá trị; columnCount = 2 (1 cặp/hàng) hoặc 4 (2 cặp/hàng).</summary>
    public static (GroupBox Group, TableLayoutPanel Table) CreateConfigGroup(string title, int columnCount)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 6, 8, 8),
            Margin = new Padding(3, 3, 3, 8)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columnCount,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        for (var i = 0; i < columnCount / 2; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / (columnCount / 2)));
        }
        group.Controls.Add(table);
        return (group, table);
    }

    public static void AddField(TableLayoutPanel table, ref int row, string labelText, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddCell(table, CreateLabel(labelText), 0, row);
        AddCell(table, control, 1, row, columnSpan: table.ColumnCount - 1);
        row++;
    }

    public static void AddFieldPair(TableLayoutPanel table, ref int row, string label1, Control control1, string label2, Control control2)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddCell(table, CreateLabel(label1), 0, row);
        AddCell(table, control1, 1, row);
        AddCell(table, CreateLabel(label2), 2, row);
        AddCell(table, control2, 3, row);
        row++;
    }

    public static void AddSectionLabel(TableLayoutPanel table, ref int row, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(3, 10, 3, 2)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddCell(table, label, 0, row, columnSpan: table.ColumnCount);
        row++;
    }

    /// <summary>Hàng cuối của nhóm cấu hình: ghi chú bên trái, nút test bên phải.</summary>
    public static void AddFooter(TableLayoutPanel table, ref int row, string note, Button button)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 6, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var noteLabel = new Label
        {
            Text = note,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = NoteColor,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(3, 4, 12, 0)
        };
        // Label AutoSize không tự xuống dòng theo độ rộng cột → giới hạn theo chiều rộng thực tế
        footer.SizeChanged += (_, _) =>
            noteLabel.MaximumSize = new Size(Math.Max(100, footer.Width - button.Width - 24), 0);
        button.Margin = new Padding(0);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        footer.Controls.Add(noteLabel, 0, 0);
        footer.Controls.Add(button, 1, 0);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddCell(table, footer, 0, row, columnSpan: table.ColumnCount);
        row++;
    }

}
