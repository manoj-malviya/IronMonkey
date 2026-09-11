namespace IronMonkey.Web.Components.Shared;

/// <summary>
/// Load state for one independently-fetched dashboard panel.
///
/// Each widget owns an instance, which is what makes partial failure work: the page awaits
/// all the fetches but never lets one failure decide what the others render. A widget that
/// fails after previously succeeding keeps <see cref="Data"/> and reports
/// <see cref="IsStale"/>, so good figures are labelled rather than discarded.
/// </summary>
public sealed class WidgetState<T> where T : class
{
    public T? Data { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Data is present but predates the most recent failed refresh.</summary>
    public bool IsStale { get; private set; }

    public bool HasData => Data is not null;
    public bool HasError => ErrorMessage is not null;

    /// <summary>When the currently displayed data was successfully fetched.</summary>
    public DateTime? LoadedAt { get; private set; }

    public void BeginLoad()
    {
        IsLoading = true;

        // The previous error is cleared only once a new attempt starts, so a failed panel
        // keeps showing why it failed until there is a fresher answer.
        ErrorMessage = null;
    }

    public void Succeed(T data)
    {
        Data = data;
        IsLoading = false;
        ErrorMessage = null;
        IsStale = false;
        LoadedAt = DateTime.UtcNow;
    }

    public void Fail(string message)
    {
        IsLoading = false;
        ErrorMessage = message;

        // Only meaningful when something is still on screen; with no data the widget shows
        // its error state instead and staleness would be a contradiction.
        IsStale = Data is not null;
    }

    /// <summary>
    /// Runs a fetch and records the outcome, translating both a null body (the API client
    /// returns default on a non-success status) and a thrown exception into a failure the
    /// widget can display and retry.
    /// </summary>
    public async Task LoadAsync(Func<Task<T?>> fetch, string failureMessage)
    {
        BeginLoad();
        try
        {
            var result = await fetch();
            if (result is null)
                Fail(failureMessage);
            else
                Succeed(result);
        }
        catch (Exception ex)
        {
            Fail($"{failureMessage} ({ex.Message})");
        }
    }
}
