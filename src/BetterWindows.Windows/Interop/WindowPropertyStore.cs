using System.Runtime.InteropServices;

namespace BetterWindows.Native.Interop;

/// <summary>COM PROPERTYKEY.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>
/// Minimal PROPVARIANT: header + 16 bytes of payload (24 bytes on x64, the only supported platform). Only VT_LPWSTR is interpreted;
/// the struct must always be released with PropVariantClear.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Payload0;
    public nint Payload1;
}

/// <summary>IPropertyStore (propsys.h), declared by hand so the layer does not depend on the shape of CsWin32's COM output.</summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

/// <summary>Reads the explicit AppUserModelID a window advertises (System.AppUserModel.ID) through SHGetPropertyStoreForWindow.</summary>
internal static class WindowPropertyStore
{
    private const ushort VtLpwstr = 31;
    private static readonly Guid IidPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
    private static readonly PropertyKey PkeyAppUserModelId = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };

    [DllImport("shell32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? ppv);

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>
    /// The window's explicit AUMID, or null (no such property, window gone, access denied, COM failure). Never throws.
    /// The property store of a window is read-only here and released immediately.
    /// </summary>
    public static string? TryGetAppUserModelId(nint hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            int hr = SHGetPropertyStoreForWindow(hwnd, IidPropertyStore, out store);
            if (hr < 0 || store == null)
            {
                return null;
            }

            var key = PkeyAppUserModelId;
            hr = store.GetValue(ref key, out var value);
            if (hr < 0)
            {
                return null;
            }

            try
            {
                if (value.Vt == VtLpwstr && value.Payload0 != 0)
                {
                    string? s = Marshal.PtrToStringUni(value.Payload0);
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }

                return null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or AccessViolationException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (store != null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(store);
                }
                catch (ArgumentException)
                {
                    // Not a COM object any more.
                }
            }
        }
    }
}
