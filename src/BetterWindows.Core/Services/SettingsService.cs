using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: settings stream. In-memory only.</summary>
public sealed class SettingsService : ISettingsService
{
    public Settings Current { get; private set; } = new();
    public event Action<Settings>? Changed;
    public Task<Settings> PatchAsync(JsonNode patch, CancellationToken ct = default) => Task.FromResult(Current);
    public Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default)
    {
        Current = mutate(Current);
        Changed?.Invoke(Current);
        return Task.FromResult(Current);
    }
    public void Reload() { }
}
