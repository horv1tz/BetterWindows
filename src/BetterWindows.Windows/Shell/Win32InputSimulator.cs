using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;

namespace BetterWindows.Native;

/// <summary>
/// Synthesizes the system shortcuts BetterWindows needs (Win+D, Win+Ctrl+Left/Right, Win+Tab) with SendInput. Each chord is ONE SendInput call
/// (not interleaved with real input), with proper key-up ordering, extended-key flags and release of foreign held modifiers first. If the
/// foreground window is higher-integrity (UIPI would swallow the input) nothing is sent; if SendInput injects fewer events than requested every
/// key of the chord is released so no modifier stays stuck.
/// </summary>
public sealed class Win32InputSimulator : IInputSimulator
{
    private readonly ILogger _log;

    /// <summary>Creates the simulator.</summary>
    public Win32InputSimulator(ILogger<Win32InputSimulator>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public void ShowDesktop() => Send("Win+D", InputSequences.ShowDesktop);

    /// <inheritdoc />
    public void SwitchDesktopLeft() => Send("Win+Ctrl+Left", held => InputSequences.SwitchDesktop(held, left: true));

    /// <inheritdoc />
    public void SwitchDesktopRight() => Send("Win+Ctrl+Right", held => InputSequences.SwitchDesktop(held, left: false));

    /// <inheritdoc />
    public void SendTaskView() => Send("Win+Tab", InputSequences.TaskView);

    private void Send(string what, Func<IReadOnlySet<ushort>, IReadOnlyList<KeyStep>> build)
    {
        try
        {
            // UIPI: input injected while a higher-integrity window is in the foreground is dropped silently (SendInput does not report it).
            int fgPid = WindowNative.GetProcessId(PInvoke.GetForegroundWindow(), out _);
            if (fgPid != 0 && ProcessSecurity.IsLikelyBlockedByUipi(fgPid))
            {
                _log.LogInformation("{Chord} skipped: the foreground window belongs to an elevated process (UIPI would block the input)", what);
                return;
            }

            var steps = build(InputSequences.HeldModifiers(KeyboardInput.IsDown));
            uint sent = KeyboardInput.Send(steps.ToArray());
            if (sent != steps.Count)
            {
                _log.LogWarning("{Chord}: SendInput injected {Sent} of {Total} events (error {Error}); releasing keys", what, sent, steps.Count,
                    System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
                KeyboardInput.Send(InputSequences.ReleaseAll(steps).ToArray());
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Chord} failed", what);
        }
    }
}
