using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Search;

/// <summary>What the user typed. Text is already truncated to the protocol limit; Locale is "en" or "ru".</summary>
public sealed record SearchQuery(string Text, string Locale);

/// <summary>
/// One provider. Providers are stateless with respect to a request: QueryAsync must return within <see cref="Timeout"/> (the engine cancels it otherwise) and must
/// observe the token. Providers never throw to the engine for "no results"; they return an empty list.
/// </summary>
public interface ISearchProvider
{
    SearchProviderId Id { get; }
    /// <summary>Per-provider time budget (apps/calc/settings/commands/windows 200 ms, files 1500 ms, web immediate).</summary>
    TimeSpan Timeout { get; }
    Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct);
}

/// <summary>A result as a provider produces it, before the engine assigns its request-scoped id.</summary>
public sealed record SearchHit(
    SearchProviderId Provider,
    string Title,
    string? Subtitle,
    string? IconUrl,
    double Score,
    IReadOnlyList<HitAction> Actions,
    SearchPreview? Preview = null,
    string? PreviewPath = null,
    bool PreviewIsApp = false);

/// <summary>
/// An action of a result. <see cref="Confirm"/> marks destructive operations: they run only when the UI names the action id "confirm" explicitly, never as an
/// implicit primary action.
/// </summary>
public sealed record HitAction(string Id, string Label, bool Primary, SearchOperation Operation);

/// <summary>
/// What running a result does. Operations carry only host-derived data (catalog ids, paths from provider results, validated URLs, enum values), never UI strings
/// that the host would pass to the shell (docs/IPC.md section 14).
/// </summary>
public abstract record SearchOperation;

public sealed record LaunchAppOperation(string AppId) : SearchOperation;
public sealed record OpenUriOperation(string Uri) : SearchOperation;
public sealed record OpenPathOperation(string Path) : SearchOperation;
public sealed record RevealPathOperation(string Path) : SearchOperation;
public sealed record CopyTextOperation(string Text) : SearchOperation;
public sealed record PowerOperation(PowerAction Action) : SearchOperation;
public sealed record ThemeOperation(ThemePreference Preference) : SearchOperation;
public sealed record TaskbarOperation(TaskbarMode Mode) : SearchOperation;
public sealed record ShowDesktopOperation : SearchOperation;
public sealed record ShowSettingsOperation(string? Page) : SearchOperation;
public sealed record QuitOperation : SearchOperation;
public sealed record ActivateWindowOperation(long Handle, int ProcessId) : SearchOperation;
