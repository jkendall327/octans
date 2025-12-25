namespace Octans.Client.Components.StatusBar;

public class StatusBarViewmodel(StatusService service)
{
    public string? MediaInfo => service.MediaInfo;
    public string? GenericText => service.GenericText;
    public string? WorkingText => service.WorkingText;
    public bool IsSearching => service.IsSearching;
    public int ProgressPercent => service.ProgressPercent;
}

public class StatusService
{
    private string? _mediaInfo;
    private string? _genericText;
    private string? _workingText;
    private bool _isSearching;
    private int _progressPercent;

    public event Action? StateChanged;

    public string? MediaInfo
    {
        get => _mediaInfo;
        set
        {
            if (_mediaInfo != value)
            {
                _mediaInfo = value;
                NotifyStateChanged();
            }
        }
    }

    public string? GenericText
    {
        get => _genericText;
        set
        {
            if (_genericText != value)
            {
                _genericText = value;
                NotifyStateChanged();
            }
        }
    }

    public string? WorkingText
    {
        get => _workingText;
        set
        {
            if (_workingText != value)
            {
                _workingText = value;
                NotifyStateChanged();
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (_isSearching != value)
            {
                _isSearching = value;
                NotifyStateChanged();
            }
        }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (_progressPercent != value)
            {
                _progressPercent = value;
                NotifyStateChanged();
            }
        }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
