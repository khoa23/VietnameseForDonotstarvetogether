using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AnythingLLMReviewTranslator;

public sealed class ReviewRowViewModel : INotifyPropertyChanged
{
    private long _id;
    private string? _allText;
    private string? _msgCtxt;
    private string? _msgId;
    private string? _msgStr;
    private string? _suggestedTranslation;
    private decimal? _rating;
    private string? _sourceFilePath;
    private DateTime? _importedAtUtc;
    private bool? _translationLocked;
    private string _status = "Pending";
    private string? _error;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string? AllText
    {
        get => _allText;
        set => SetProperty(ref _allText, value);
    }

    public string? MsgCtxt
    {
        get => _msgCtxt;
        set => SetProperty(ref _msgCtxt, value);
    }

    public string? MsgId
    {
        get => _msgId;
        set => SetProperty(ref _msgId, value);
    }

    public string? MsgStr
    {
        get => _msgStr;
        set => SetProperty(ref _msgStr, value);
    }

    public string? SuggestedTranslation
    {
        get => _suggestedTranslation;
        set => SetProperty(ref _suggestedTranslation, value);
    }

    public decimal? Rating
    {
        get => _rating;
        set => SetProperty(ref _rating, value);
    }

    public string? SourceFilePath
    {
        get => _sourceFilePath;
        set => SetProperty(ref _sourceFilePath, value);
    }

    public DateTime? ImportedAtUtc
    {
        get => _importedAtUtc;
        set => SetProperty(ref _importedAtUtc, value);
    }

    public bool? TranslationLocked
    {
        get => _translationLocked;
        set => SetProperty(ref _translationLocked, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? string.Empty);
    }

    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        if (propertyName is not null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        return true;
    }
}
