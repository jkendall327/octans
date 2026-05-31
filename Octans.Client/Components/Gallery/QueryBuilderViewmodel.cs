using Octans.Core.Querying;
using Octans.Core.Tags;

namespace Octans.Client.Components.Gallery;

public sealed class QueryBuilderViewmodel(IOctansClient client) : ViewmodelBase, IDisposable
{
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _requestCts;

    private bool _initialized;

    private readonly List<QueryParameter> _parameters = [];
    private readonly List<QuerySuggestionDto> _suggestions = [];

    public Func<List<QueryParameter>, Task>? QueryChanged { get; set; }

    public IReadOnlyList<QueryParameter> Parameters => _parameters;
    public IReadOnlyList<QuerySuggestionDto> Suggestions => _suggestions;

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

        await NotifyStateChanged();
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

        await NotifyStateChanged();

        await DebouncedFetchAsync(Current, debounceMs);
    }

    public async Task RemoveAtAsync(QueryParameter index)
    {
        var removed = _parameters.Remove(index);

        if (removed)
        {
            await NotifyStateChanged();
            await NotifyQueryChangedAsync();
        }
    }

    private async Task ClearSuggestions()
    {
        _suggestions.Clear();
        await NotifyStateChanged();
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
            var results = await client.GetQuerySuggestionsAsync(term, cancellationToken: _requestCts.Token);

            _suggestions.Clear();

            _suggestions.AddRange(results
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Subtag)
                .Select(MapSuggestion));

            await NotifyStateChanged();
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

        await NotifyStateChanged();

        await ClearSuggestions();

        await NotifyQueryChangedAsync();
    }

    public async Task ApplySuggestion(QuerySuggestionDto tag)
    {
        Current = $"{tag.Namespace}:{tag.Subtag}";

        await NotifyStateChanged();
        await AddCurrentAsync();
    }

    private static QuerySuggestionDto MapSuggestion(TagModel tag) => new(tag.Namespace, tag.Subtag);

    private async Task NotifyQueryChangedAsync()
    {
        if (QueryChanged is null)
        {
            return;
        }

        await QueryChanged.Invoke(_parameters);
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _requestCts?.Cancel();
        _requestCts?.Dispose();
    }
}

public sealed record QuerySuggestionDto(string Namespace, string Subtag);
