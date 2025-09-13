using Octans.Client.Components.Pages;
using Octans.Core.Communication;
using Octans.Core.Models.Tagging;
using Octans.Core.Querying;

namespace Octans.Client.Components.Gallery;

public sealed class QueryBuilderViewmodel(QuerySuggestionFinder suggestionFinder) : IDisposable, INotifyStateChanged
{
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _requestCts;

    private bool _initialized;

    private readonly List<QueryParameter> _parameters = [];
    private readonly List<Tag> _suggestions = [];

    public Func<Task>? StateChanged { get; set; }
    public Func<List<QueryParameter>, Task>? QueryChanged { get; set; }

    public IReadOnlyList<QueryParameter> Parameters => _parameters;
    public IReadOnlyList<Tag> Suggestions => _suggestions;

    public string Current { get; private set; } = string.Empty;

    public async Task Initialize(IEnumerable<QueryParameter>? initial)
    {
        if (_initialized)
        {
            return;
        }

        _parameters.Clear();

        if (initial is not null)
        {
            _parameters.AddRange(initial);
        }

        _initialized = true;

        await InvokeStateHasChanged();
    }

    public async Task HandleKeyDownAsync(string key)
    {
        if (key == "Enter")
        {
            await ClearSuggestions();
            await AddCurrentAsync();
        }
        else if (key == "Escape")
        {
            await ClearSuggestions();
        }
    }

    public async Task OnInputAsync(string? value, int debounceMs = 200)
    {
        Current = value ?? string.Empty;

        await InvokeStateHasChanged();

        await DebouncedFetchAsync(Current, debounceMs);
    }

    public async Task RemoveAtAsync(QueryParameter index)
    {
        var removed = _parameters.Remove(index);

        if (removed)
        {
            await InvokeStateHasChanged();
            await NotifyQueryChangedAsync();
        }
    }

    private async Task ClearSuggestions()
    {
        _suggestions.Clear();
        await InvokeStateHasChanged();
    }

    private async Task DebouncedFetchAsync(string term, int delayMs)
    {
        if (_debounceCts is not null)
        {
            await _debounceCts.CancelAsync();
            _debounceCts.Dispose();
        }

        _debounceCts = new();

        try
        {
            await Task.Delay(delayMs, _debounceCts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by another keystroke
        }

        await FetchSuggestionsAsync(term);
    }

    private async Task FetchSuggestionsAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            await ClearSuggestions();

            return;
        }

        if (_requestCts is not null)
        {
            await _requestCts.CancelAsync();
            _requestCts.Dispose();
        }

        _requestCts = new();

        try
        {
            var results = await suggestionFinder.GetAutocompleteTagIds(term, false, _requestCts.Token);

            _suggestions.Clear();

            _suggestions.AddRange(results
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Subtag));

            await InvokeStateHasChanged();
        }
        catch (OperationCanceledException)
        {
            // ignore — a newer request superseded this one
        }
        catch
        {
            await ClearSuggestions();
        }
    }

    private async Task AddCurrentAsync()
    {
        var trimmed = Current.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var kind = trimmed.StartsWith("system:", StringComparison.OrdinalIgnoreCase)
            ? QueryKind.System
            : QueryKind.Normal;

        _parameters.Add(new(trimmed, kind));

        Current = string.Empty;

        await InvokeStateHasChanged();

        await ClearSuggestions();

        await NotifyQueryChangedAsync();
    }

    public async Task ApplySuggestion(Tag tag)
    {
        Current = $"{tag.Namespace}:{tag.Subtag}";

        await InvokeStateHasChanged();
        await AddCurrentAsync();
    }

    private async Task NotifyQueryChangedAsync()
    {
        if (QueryChanged is null)
        {
            return;
        }

        await QueryChanged.Invoke(_parameters);
    }

    private async Task InvokeStateHasChanged()
    {
        if (StateChanged is not null)
        {
            await StateChanged.Invoke();
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _requestCts?.Cancel();
        _requestCts?.Dispose();
    }
}