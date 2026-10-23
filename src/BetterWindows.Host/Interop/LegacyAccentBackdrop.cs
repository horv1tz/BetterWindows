using System.Runtime.InteropServices;

namespace BetterWindows.Host.Interop;

/// <summary>
/// The UNDOCUMENTED user32!SetWindowCompositionAttribute accent (ACCENT_ENABLE_ACRYLICBLURBEHIND), used ONLY on Windows builds older than 22621 (no
/// DWMWA_SYSTEMBACKDROP_TYPE) and ONLY when the environment variable BETTERWINDOWS_LEGACY_ACCENT=1 is set (the project rule: undocumented APIs live behind a feature
/// flag with a safe fallback; without the flag those builds simply get no backdrop and the UI's translucent CSS fill remains).
/// The accent blurs the part of the window that is not clipped by its window region, i.e. exactly the glass shape. Failure is reported by the return value; there is
/// nothing to restore because the accent dies with the window.
/// </summary>
internal static partial class LegacyAccentBackdrop
{
    public const string EnvVar = "BETTERWINDOWS_LEGACY_ACCENT";

    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable(EnvVar) == "1";

    /// <summary>True when this OS build should use the accent path (feature flag on AND no system backdrop attribute).</summary>
    public static bool ShouldUse => IsEnabled && !DwmHelper.IsSystemBackdropSupported;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;   // AABBGGRR; the alpha must be non-zero or the window becomes fully transparent/black on some builds
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowCompositionAttribute")]
    private static partial int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    /// <summary>Enables (tinted acrylic) or disables the accent. <paramref name="tintAbgr"/> is the gradient colour; use a low-alpha tint.</summary>
    public static bool TrySet(nint hwnd, bool enable, uint tintAbgr = 0x66202020)
    {
        if (hwnd == 0) return false;
        var policy = new AccentPolicy
        {
            AccentState = enable ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED,
            AccentFlags = 0,
            GradientColor = tintAbgr,
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
