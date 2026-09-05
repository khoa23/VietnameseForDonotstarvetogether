namespace AnythingLLMReviewTranslator;

public sealed class AppSettings
{
    public string Provider { get; set; } = "Gemini";

    public SqlServerSettings SqlServer { get; set; } = new();

    public AnythingLlmSettings AnythingLLM { get; set; } = new();

    public GeminiSettings Gemini { get; set; } = new();

    public ProcessingSettings Processing { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Provider = "Gemini",
            SqlServer = new SqlServerSettings(),
            AnythingLLM = new AnythingLlmSettings
            {
                BaseUrl = "http://localhost:3001",
                ApiKey = string.Empty,
                WorkspaceSlug = string.Empty,
                Mode = "query",
                RequestTimeoutSeconds = 120,
                SessionPrefix = "translation",
                PromptTemplate = AnythingLlmSettings.DefaultPromptTemplate
            },
            Gemini = new GeminiSettings
            {
                ApiKey = string.Empty,
                ApiKeys = new List<string>(),
                Model = string.Empty,
                Models = new List<string>(),
                ModelConfigs = new List<GeminiModelRateConfig>(),
                BaseUrl = "https://generativelanguage.googleapis.com",
                RequestTimeoutSeconds = 120
            },
            Processing = new ProcessingSettings(),
            Logging = new LoggingSettings()
        }.Normalize();
    }

    public AppSettings Normalize()
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? "Gemini" : Provider.Trim();
        if (Provider is not ("AnythingLLM" or "Gemini"))
        {
            Provider = "Gemini";
        }

        SqlServer ??= new SqlServerSettings();
        AnythingLLM ??= new AnythingLlmSettings();
        Gemini ??= new GeminiSettings();
        Processing ??= new ProcessingSettings();
        Logging ??= new LoggingSettings();

        SqlServer.Normalize();
        AnythingLLM.Normalize();
        Gemini.Normalize();
        Processing.Normalize();
        Logging.Normalize();
        return this;
    }
}

public sealed class GeminiModelRateConfig
{
    public string Name { get; set; } = string.Empty;

    public int Rpm { get; set; }

    public int Rpd { get; set; }

    public void Normalize()
    {
        Name = Name?.Trim() ?? string.Empty;
        Rpm = Math.Max(0, Rpm);
        Rpd = Math.Max(0, Rpd);
    }
}

public sealed class GeminiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public List<string> ApiKeys { get; set; } = new();

    public string Model { get; set; } = string.Empty;

    public List<string> Models { get; set; } = new();

    public List<GeminiModelRateConfig> ModelConfigs { get; set; } = new();

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    public int RequestTimeoutSeconds { get; set; } = 120;

    public void Normalize()
    {
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        ApiKeys = NormalizeStringList(ApiKeys);
        if (!string.IsNullOrWhiteSpace(ApiKey) && ApiKeys.Count == 0)
        {
            ApiKeys.Add(ApiKey);
        }

        Model = Model?.Trim() ?? string.Empty;
        Models = NormalizeStringList(Models);
        if (!string.IsNullOrWhiteSpace(Model) && Models.Count == 0)
        {
            Models.Add(Model);
        }

        if (ModelConfigs is null)
        {
            ModelConfigs = new List<GeminiModelRateConfig>();
        }

        ModelConfigs = ModelConfigs
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ModelConfigs.Count == 0 && Models.Count > 0)
        {
            ModelConfigs = Models
                .Select(name => new GeminiModelRateConfig { Name = name, Rpm = 0, Rpd = 0 })
                .ToList();
        }

        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://generativelanguage.googleapis.com" : BaseUrl.Trim();
        RequestTimeoutSeconds = RequestTimeoutSeconds <= 0 ? 120 : Math.Clamp(RequestTimeoutSeconds, 15, 600);
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return new List<string>();
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class SqlServerSettings
{
    public string ConnectionString { get; set; } = string.Empty;

    public string SourceQuery { get; set; } = string.Empty;

    public string SourceTable { get; set; } = "dbo.Translations";

    public string TargetTable { get; set; } = "dbo.Translations";

    public string KeyColumn { get; set; } = "Id";

    public string AllTextColumn { get; set; } = "AllText";

    public string MsgCtxtColumn { get; set; } = "MsgCtxt";

    public string MsgIdColumn { get; set; } = "MsgId";

    public string MsgStrColumn { get; set; } = "MsgStr";

    public string SuggestedTranslationColumn { get; set; } = "SuggestedTranslation";

    public string RatingColumn { get; set; } = "Rating";

    public string SourceFilePathColumn { get; set; } = "SourceFilePath";

    public string ImportedAtUtcColumn { get; set; } = "ImportedAtUtc";

    public string TranslationLockedColumn { get; set; } = "TranslationLocked";

    public void Normalize()
    {
        ConnectionString = ConnectionString?.Trim() ?? string.Empty;
        SourceQuery = SourceQuery?.Trim() ?? string.Empty;
        SourceTable = string.IsNullOrWhiteSpace(SourceTable) ? "dbo.Translations" : SourceTable.Trim();
        TargetTable = string.IsNullOrWhiteSpace(TargetTable) ? SourceTable : TargetTable.Trim();
        KeyColumn = string.IsNullOrWhiteSpace(KeyColumn) ? "Id" : KeyColumn.Trim();
        AllTextColumn = string.IsNullOrWhiteSpace(AllTextColumn) ? "AllText" : AllTextColumn.Trim();
        MsgCtxtColumn = string.IsNullOrWhiteSpace(MsgCtxtColumn) ? "MsgCtxt" : MsgCtxtColumn.Trim();
        MsgIdColumn = string.IsNullOrWhiteSpace(MsgIdColumn) ? "MsgId" : MsgIdColumn.Trim();
        MsgStrColumn = string.IsNullOrWhiteSpace(MsgStrColumn) ? "MsgStr" : MsgStrColumn.Trim();
        SuggestedTranslationColumn = string.IsNullOrWhiteSpace(SuggestedTranslationColumn) ? "SuggestedTranslation" : SuggestedTranslationColumn.Trim();
        RatingColumn = string.IsNullOrWhiteSpace(RatingColumn) ? "Rating" : RatingColumn.Trim();
        SourceFilePathColumn = string.IsNullOrWhiteSpace(SourceFilePathColumn) ? "SourceFilePath" : SourceFilePathColumn.Trim();
        ImportedAtUtcColumn = string.IsNullOrWhiteSpace(ImportedAtUtcColumn) ? "ImportedAtUtc" : ImportedAtUtcColumn.Trim();
        TranslationLockedColumn = string.IsNullOrWhiteSpace(TranslationLockedColumn) ? "TranslationLocked" : TranslationLockedColumn.Trim();
    }
}

public sealed class AnythingLlmSettings
{
    public string BaseUrl { get; set; } = "http://localhost:3001";

    public string ApiKey { get; set; } = string.Empty;

    public string WorkspaceSlug { get; set; } = string.Empty;

    public string Mode { get; set; } = "query";

    public int RequestTimeoutSeconds { get; set; } = 120;

    public string SessionPrefix { get; set; } = "translation";

    public string PromptTemplate { get; set; } = string.Empty;

    public static string DefaultPromptTemplate =>
        string.Join(
            Environment.NewLine,
            [
                "Bạn là chuyên gia hiệu đính và dịch thuật tiếng Việt cho game Don't Starve Together.",
                string.Empty,
                "Nhiệm vụ:",
                "1. Chấm điểm bản dịch hiện tại (MsgStr) so với chuỗi gốc tiếng Anh (MsgId) theo thang điểm rating từ 0.0 đến 10.0:",
                "   - 8.0 - 10.0: MsgStr dịch chính xác, tự nhiên, chuẩn văn phong game.",
                "   - 5.0 - 7.9: MsgStr hiểu được nhưng còn gượng gạo hoặc có sai sót nhỏ.",
                "   - 0.0 - 4.9: MsgStr dịch sai, dịch máy thô, hoặc chưa dịch (đang trống / giống hệt MsgId tiếng Anh).",
                "2. Đề xuất bản dịch tiếng Việt tối ưu nhất ở trường \"suggestedTranslation\": tự nhiên, ngắn gọn, chính xác với ngữ cảnh game.",
                string.Empty,
                "Quy tắc bảo toàn ký tự đặc biệt & định dạng (BẮT BUỘC):",
                "- Giữ nguyên tuyệt đối tất cả các ký tự đặc biệt, dấu gạch chéo ngược (\\), dấu ngoặc kép (\"), escaped quotes (\\\"), ký tự xuống dòng (\\n, \\r), tab (\\t), placeholder (%s, {0}, {name}), mã thẻ định dạng.",
                "- Ví dụ minh họa:",
                "  + Input: \\\"Not yet mid-summer\\\", you say? Well my friend, the early bird gets the worm!",
                "  + Output suggestedTranslation: \\\"Chưa đến giữa hè\\\", bạn nói? Bạn ơi, con chim sớm sẽ có sâu!",
                string.Empty,
                "Trả về CHỈ JSON hợp lệ, không bọc markdown (không dùng ```json), không giải thích thừa, theo mẫu:",
                "{\"suggestedTranslation\":\"Bản dịch tiếng Việt\",\"rating\":8.5}",
                string.Empty,
                "Dữ liệu:",
                "Id: {{Id}}",
                "MsgCtxt: {{MsgCtxt}}",
                "MsgId: {{MsgId}}",
                "MsgStr: {{MsgStr}}"
            ]);

    public void Normalize()
    {
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "http://localhost:3001/api/v1" : BaseUrl.Trim();
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        WorkspaceSlug = WorkspaceSlug?.Trim() ?? string.Empty;
        Mode = string.IsNullOrWhiteSpace(Mode) ? "query" : Mode.Trim().ToLowerInvariant();
        if (Mode is not ("chat" or "query"))
        {
            Mode = "query";
        }

        RequestTimeoutSeconds = RequestTimeoutSeconds <= 0 ? 120 : Math.Clamp(RequestTimeoutSeconds, 15, 600);
        SessionPrefix = string.IsNullOrWhiteSpace(SessionPrefix) ? "translation" : SessionPrefix.Trim();
        PromptTemplate = string.IsNullOrWhiteSpace(PromptTemplate) ? DefaultPromptTemplate : PromptTemplate;
    }
}

public sealed class ProcessingSettings
{
    public int MaxConcurrentRequests { get; set; } = 4;

    public int RequestsPerMinute { get; set; } = 5;

    public bool RespectTranslationLocked { get; set; } = true;

    public bool SkipIfSuggestedExists { get; set; } = true;

    public int MaxRows { get; set; } = 0;

    public int DelayBetweenRequestsMs { get; set; } = 0;

    public int MaxRetries { get; set; } = 2;

    public int RetryDelayMs { get; set; } = 1000;

    public void Normalize()
    {
        MaxConcurrentRequests = Math.Clamp(MaxConcurrentRequests, 1, 16);
        RequestsPerMinute = Math.Max(0, RequestsPerMinute);
        MaxRows = Math.Max(0, MaxRows);
        DelayBetweenRequestsMs = Math.Max(0, DelayBetweenRequestsMs);
        MaxRetries = Math.Max(0, MaxRetries);
        RetryDelayMs = Math.Max(0, RetryDelayMs);
    }
}

public sealed class LoggingSettings
{
    public string LogDirectory { get; set; } = "logs";

    public bool EnableFileLogging { get; set; } = true;

    public string LogLevel { get; set; } = "Info";

    public void Normalize()
    {
        LogDirectory = string.IsNullOrWhiteSpace(LogDirectory) ? "logs" : LogDirectory.Trim();
        LogLevel = string.IsNullOrWhiteSpace(LogLevel) ? "Info" : LogLevel.Trim();
    }
}

