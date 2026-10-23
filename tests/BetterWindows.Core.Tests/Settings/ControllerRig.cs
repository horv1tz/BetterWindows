using System.Diagnostics;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Config;

/// <summary>
/// Runs ONE real controller on the real Core graph with fake ports. CoreHarness registers controllers but never starts them, so the rig registers the controller under
/// test, starts it, and returns the harness (the controller instance is <c>h.Get&lt;TController&gt;()</c>).
/// </summary>
public static class ControllerRig
{
    public static CoreHarness Start<TController>(Action<IServiceCollection>? configure = null) where TController : class, IShellController
    {
        var h = new CoreHarness(sc =>
        {
            sc.AddSingleton<TController>();
            sc.AddSingleton<IShellController>(sp => sp.GetRequiredService<TController>());
            configure?.Invoke(sc);
        });
        h.Get<TController>().Start();
        return h;
    }

    /// <summary>The error code of a failed call result ("badRequest", "forbidden", ...).</summary>
    public static string ErrorCode(System.Text.Json.Nodes.JsonObject result) => (string?)result["error"]?["code"] ?? "";

    /// <summary>Polls a condition on the real clock (test plumbing only: background Task.Run work in controllers is observed, never waited for by logic).</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Xunit.Sdk.XunitException("condition not reached in time");
            await Task.Delay(5);
        }
    }
}
