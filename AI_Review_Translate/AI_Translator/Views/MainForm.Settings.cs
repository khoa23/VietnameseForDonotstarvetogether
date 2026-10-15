
using static AI_Translator.Views.UiFactory;

namespace AI_Translator.Views;

public partial class MainForm
{
    private TabPage _configPage = null!;
    private TabPage _promptPage = null!;

    // Cấu hình SQL
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

    // Cấu hình AnythingLLM
    private TextBox _txtBaseUrl = null!;
    private TextBox _txtApiKey = null!;
    private TextBox _txtWorkspaceSlug = null!;
    private ComboBox _cmbMode = null!;
    private NumericUpDown _nudTimeoutSeconds = null!;
    private TextBox _txtSessionPrefix = null!;

    // Cấu hình Gemini
    private TextBox _txtGeminiApiKeys = null!;
    private TextBox _txtGeminiModels = null!;
    private TextBox _txtGeminiBaseUrl = null!;
    private NumericUpDown _nudGeminiTimeoutSeconds = null!;

    private TextBox _txtPromptTemplate = null!;

    #region Tab Cấu hình (SQL + AI)

    private TabPage BuildConfigTab()
    {
        var page = new TabPage("Cấu hình") { AutoScroll = true, Padding = new Padding(6) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(layout);

        var sqlGroup = BuildSqlGroup();
        layout.Controls.Add(sqlGroup, 0, 0);
        layout.SetColumnSpan(sqlGroup, 2);
        layout.Controls.Add(BuildGeminiGroup(), 0, 1);
        layout.Controls.Add(BuildAnythingGroup(), 1, 1);

        return page;
    }

    private GroupBox BuildSqlGroup()
    {
        var (group, table) = CreateConfigGroup("SQL Server", columnCount: 4);

        _txtConnectionString = CreateTextBox(multilineHeight: 46);
        _txtSourceQuery = CreateTextBox(multilineHeight: 60);
        _txtSourceTable = CreateTextBox();
        _txtTargetTable = CreateTextBox();
        _txtKeyColumn = CreateTextBox();
        _txtSuggestedTranslationColumn = CreateTextBox();
        _txtRatingColumn = CreateTextBox();
        _txtTranslationLockedColumn = CreateTextBox();
        _txtAllTextColumn = CreateTextBox();
        _txtMsgCtxtColumn = CreateTextBox();
        _txtMsgIdColumn = CreateTextBox();
        _txtMsgStrColumn = CreateTextBox();
        _txtSourceFilePathColumn = CreateTextBox();
        _txtImportedAtUtcColumn = CreateTextBox();

        var row = 0;
        AddField(table, ref row, "Connection string", _txtConnectionString);
        AddField(table, ref row, "Source query", _txtSourceQuery);
        AddFieldPair(table, ref row, "Source table", _txtSourceTable, "Target table", _txtTargetTable);
        AddSectionLabel(table, ref row, "Tên cột");
        AddFieldPair(table, ref row, "Key", _txtKeyColumn, "SuggestedTranslation", _txtSuggestedTranslationColumn);
        AddFieldPair(table, ref row, "Rating", _txtRatingColumn, "TranslationLocked", _txtTranslationLockedColumn);
        AddFieldPair(table, ref row, "MsgId", _txtMsgIdColumn, "MsgStr", _txtMsgStrColumn);
        AddFieldPair(table, ref row, "MsgCtxt", _txtMsgCtxtColumn, "AllText", _txtAllTextColumn);
        AddFieldPair(table, ref row, "SourceFilePath", _txtSourceFilePathColumn, "ImportedAtUtc", _txtImportedAtUtcColumn);
        AddFooter(table, ref row,
            "Để trống Source query để tự dựng câu lệnh từ Source table. Nếu query đã alias đúng tên cột, chỉ cần đổi Source query và Target table.",
            CreateButton("Test SQL", TestSqlClicked));

        return group;
    }

    private GroupBox BuildGeminiGroup()
    {
        var (group, table) = CreateConfigGroup("Gemini / OpenRouter", columnCount: 2);

        _txtGeminiApiKeys = CreateTextBox(multilineHeight: 60);
        _txtGeminiModels = CreateTextBox(multilineHeight: 60);
        _txtGeminiBaseUrl = CreateTextBox();
        _nudGeminiTimeoutSeconds = CreateNumericUpDown(15, 600, 120);

        var row = 0;
        AddField(table, ref row, "API keys (mỗi dòng 1 key)", _txtGeminiApiKeys);
        AddField(table, ref row, "Models (mỗi dòng 1 model)", _txtGeminiModels);
        AddField(table, ref row, "API base URL", _txtGeminiBaseUrl);
        AddField(table, ref row, "Timeout (giây)", _nudGeminiTimeoutSeconds);
        AddFooter(table, ref row,
            "Gemini trực tiếp: https://generativelanguage.googleapis.com, model không prefix (vd: gemini-2.0-flash). " +
            "OpenRouter: https://openrouter.ai/api/v1, model dạng deepseek/deepseek-chat (:free = miễn phí). " +
            "Giới hạn RPM/RPD theo model chỉnh ở ModelConfigs trong appsettings.json.",
            CreateButton("Test Gemini", TestGeminiClicked));

        return group;
    }

    private GroupBox BuildAnythingGroup()
    {
        var (group, table) = CreateConfigGroup("AnythingLLM", columnCount: 2);

        _txtBaseUrl = CreateTextBox();
        _txtApiKey = CreateTextBox(password: true);
        _txtWorkspaceSlug = CreateTextBox();
        _cmbMode = CreateComboBox("query", "chat");
        _nudTimeoutSeconds = CreateNumericUpDown(15, 600, 120);
        _txtSessionPrefix = CreateTextBox();

        var row = 0;
        AddField(table, ref row, "API base URL", _txtBaseUrl);
        AddField(table, ref row, "API key", _txtApiKey);
        AddField(table, ref row, "Workspace slug", _txtWorkspaceSlug);
        AddField(table, ref row, "Mode", _cmbMode);
        AddField(table, ref row, "Timeout (giây)", _nudTimeoutSeconds);
        AddField(table, ref row, "Session prefix", _txtSessionPrefix);
        AddFooter(table, ref row,
            "Khuyến nghị mode = query cho dịch thuần; dùng chat nếu workspace cần RAG.",
            CreateButton("Test AnythingLLM", TestAnythingClicked));

        return group;
    }

    #endregion

    #region Tab Prompt

    private TabPage BuildPromptTab()
    {
        var page = new TabPage("Prompt") { Padding = new Padding(8) };

        _txtPromptTemplate = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var note = new Label
        {
            Text = "Placeholder: {{Id}}, {{AllText}}, {{MsgCtxt}}, {{MsgId}}, {{MsgStr}}, {{SuggestedTranslation}}, {{Rating}}, {{SourceFilePath}}, {{ImportedAtUtc}}, {{TranslationLocked}}.",
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ForeColor = NoteColor,
            Padding = new Padding(0, 6, 0, 0)
        };

        page.Controls.Add(_txtPromptTemplate);
        page.Controls.Add(note);
        return page;
    }

    #endregion

    #region Đồng bộ cấu hình <-> UI

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

        // Normalize() đã gộp ApiKey/Model đơn lẻ vào danh sách
        _txtGeminiApiKeys.Text = string.Join(Environment.NewLine, settings.Gemini.ApiKeys);
        _txtGeminiModels.Text = string.Join(Environment.NewLine, settings.Gemini.Models);
        _txtGeminiBaseUrl.Text = settings.Gemini.BaseUrl;
        _nudGeminiTimeoutSeconds.Value = Math.Clamp(settings.Gemini.RequestTimeoutSeconds, (int)_nudGeminiTimeoutSeconds.Minimum, (int)_nudGeminiTimeoutSeconds.Maximum);

        _txtPromptTemplate.Text = settings.AnythingLLM.PromptTemplate.ReplaceLineEndings("\r\n");

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
                PromptTemplate = _txtPromptTemplate.Text.ReplaceLineEndings("\n")
            },
            Gemini = new GeminiSettings
            {
                ApiKeys = SplitLines(_txtGeminiApiKeys.Text),
                Models = SplitLines(_txtGeminiModels.Text),
                // ModelConfigs không có trên UI → giữ nguyên giá trị đã nạp
                ModelConfigs = _settings.Gemini.ModelConfigs,
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
            },
            Logging = _settings.Logging
        };

        return settings.Normalize();
    }

    private static List<string> SplitLines(string text)
    {
        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    #endregion

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
            throw new InvalidOperationException("Gemini API Key chưa được điền. Vui lòng nhập key ở tab Cấu hình.");
        }

        if (models.Count == 0 || models.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Gemini Model chưa được điền. Vui lòng nhập model ở tab Cấu hình.");
        }
    }


}
