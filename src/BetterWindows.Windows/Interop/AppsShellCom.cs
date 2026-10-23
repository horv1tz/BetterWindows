using System.Runtime.InteropServices;
using System.Text;

namespace BetterWindows.Native.Interop;

// Hand-written COM interop for the shell interfaces of the apps stream. Source for every vtable below: the Windows SDK headers
// (shobjidl_core.h, shlobj_core.h, objidl.h, propkey.h). The order of the methods IS the vtable layout, so never reorder or drop a
// method, even an unused one. Every pointer-typed out parameter is declared as IntPtr and converted by the helpers in AppsCom, because
// with [PreserveSig] the CLR would otherwise marshal a (possibly uninitialised) out pointer even when the HRESULT is a failure.

/// <summary>PROPERTYKEY (propkey.h / wtypes.h): a property schema GUID plus a property id.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AppsPropertyKey
{
    public Guid Fmtid;
    public uint Pid;

    public AppsPropertyKey(string fmtid, uint pid)
    {
        Fmtid = new Guid(fmtid);
        Pid = pid;
    }
}

/// <summary>SIZE (windef.h), passed by value to IShellItemImageFactory::GetImage.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AppsSize
{
    public int Cx;
    public int Cy;
}

/// <summary>IShellItem + IShellItem2 (shobjidl_core.h), flattened into one interface so a child of an enumeration can be queried for properties directly.</summary>
[ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsShellItem2
{
    // IShellItem
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IntPtr ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IntPtr psi, uint hint, out int piOrder);
    // IShellItem2
    [PreserveSig] int GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyStoreWithCreateObject(uint flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, uint flags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyDescriptionList(ref AppsPropertyKey keyType, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int Update(IntPtr pbc);
    [PreserveSig] int GetProperty(ref AppsPropertyKey key, IntPtr ppropvar);
    [PreserveSig] int GetCLSID(ref AppsPropertyKey key, out Guid pclsid);
    [PreserveSig] int GetFileTime(ref AppsPropertyKey key, out long pft);
    [PreserveSig] int GetInt32(ref AppsPropertyKey key, out int pi);
    [PreserveSig] int GetString(ref AppsPropertyKey key, out IntPtr ppsz);
    [PreserveSig] int GetUInt32(ref AppsPropertyKey key, out uint pui);
    [PreserveSig] int GetUInt64(ref AppsPropertyKey key, out ulong pull);
    [PreserveSig] int GetBool(ref AppsPropertyKey key, out int pf);
}

/// <summary>IEnumShellItems (shobjidl_core.h): what BHID_EnumItems returns for a shell folder item.</summary>
[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsEnumShellItems
{
    // Next(celt = 1): S_OK with one item, S_FALSE with 0 fetched at the end.
    [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IntPtr ppenum);
}

/// <summary>IShellItemImageFactory (shobjidl_core.h): icon/thumbnail as HBITMAP.</summary>
[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsShellItemImageFactory
{
    [PreserveSig] int GetImage(AppsSize size, uint flags, out IntPtr phbm);
}

/// <summary>IShellLinkW (shobjidl_core.h).</summary>
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsShellLink
{
    [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    [PreserveSig] int GetIDList(out IntPtr ppidl);
    [PreserveSig] int SetIDList(IntPtr pidl);
    [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    [PreserveSig] int GetHotkey(out ushort pwHotkey);
    [PreserveSig] int SetHotkey(ushort wHotkey);
    [PreserveSig] int GetShowCmd(out int piShowCmd);
    [PreserveSig] int SetShowCmd(int iShowCmd);
    [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

/// <summary>IPersistFile (objidl.h): loads a .lnk into an IShellLink.</summary>
[ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsPersistFile
{
    // IPersist
    [PreserveSig] int GetClassID(out Guid pClassID);
    // IPersistFile
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    [PreserveSig] int GetCurFile(out IntPtr ppszFileName);
}

/// <summary>
/// IDesktopWallpaper (shobjidl_core.h, Windows 8+). Method order copied from the header. IShellItemArray parameters are IntPtr because we never use slideshows.
/// </summary>
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppsDesktopWallpaper
{
    [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, out IntPtr wallpaper);
    [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorID);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out AppsRect displayRect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(int position);
    [PreserveSig] int GetPosition(out int position);
    [PreserveSig] int SetSlideshow(IntPtr items);
    [PreserveSig] int GetSlideshow(out IntPtr items);
    [PreserveSig] int SetSlideshowOptions(int options, uint slideshowTick);
    [PreserveSig] int GetSlideshowOptions(out int options, out uint slideshowTick);
    [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
    [PreserveSig] int GetStatus(out int state);
    [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

/// <summary>RECT (windef.h).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AppsRect
{
    public int Left, Top, Right, Bottom;
}

/// <summary>GUIDs, property keys and constants used with the interfaces above.</summary>
internal static class AppsShellIds
{
    public static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    public static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid IidShellItem2 = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
    public static readonly Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    public static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

    // propkey.h
    public static readonly AppsPropertyKey AppUserModelId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);            // System.AppUserModel.ID
    public static readonly AppsPropertyKey PackageFullName = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 17);          // System.AppUserModel.PackageFullName
    public static readonly AppsPropertyKey PackageFamilyName = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 18);        // System.AppUserModel.PackageFamilyName
    public static readonly AppsPropertyKey ItemNameDisplay = new("B725F130-47EF-101A-A5F1-02608C9EEBAC", 10);          // System.ItemNameDisplay
    public static readonly AppsPropertyKey LinkTargetParsingPath = new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25", 2);     // System.Link.TargetParsingPath

    // SIGDN (shobjidl_core.h)
    public const uint SigdnNormalDisplay = 0x00000000;
    public const uint SigdnDesktopAbsoluteParsing = 0x80028000;
    public const uint SigdnFileSysPath = 0x80058000;

    // SIIGBF (shobjidl_core.h)
    public const uint SiigbfResizeToFit = 0x00;
    public const uint SiigbfBiggerSizeOk = 0x01;
    public const uint SiigbfIconOnly = 0x04;
    public const uint SiigbfThumbnailOnly = 0x08;

    // STGM_READ, SLGP_RAWPATH
    public const uint StgmRead = 0x00000000;
    public const uint SlgpRawPath = 0x4;

    // DESKTOP_WALLPAPER_POSITION
    public const int DwposFill = 4;
}

/// <summary>P/Invokes for shell32 entry points that return COM objects/PIDLs (hand-declared: CsWin32 without marshalling would give raw pointers anyway).</summary>
internal static class AppsShellNative
{
    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int SHGetKnownFolderItem(ref Guid rfid, uint flags, IntPtr hToken, ref Guid riid, out IntPtr ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
}

/// <summary>Small helpers around the raw COM plumbing: HRESULT checks, string/unknown conversion and deterministic release.</summary>
internal static class AppsCom
{
    public const uint ClsctxInprocServer = 0x1;
    public const uint ClsctxLocalServer = 0x4;

    public static bool Succeeded(int hr) => hr >= 0;

    /// <summary>Wraps (and consumes the reference of) a raw interface pointer in an RCW; zero stays null.</summary>
    public static T? Wrap<T>(IntPtr unknown) where T : class
    {
        if (unknown == IntPtr.Zero) return null;
        try
        {
            // GetObjectForIUnknown AddRefs for the RCW; drop the reference we were handed.
            return (T)Marshal.GetObjectForIUnknown(unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Converts a CoTaskMem-allocated wide string returned by a COM method into a managed string and frees it.</summary>
    public static string? TakeString(IntPtr coTaskMemString)
    {
        if (coTaskMemString == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(coTaskMemString); }
        finally { Marshal.FreeCoTaskMem(coTaskMemString); }
    }

    /// <summary>Releases the RCW completely (all references); null-safe, never throws.</summary>
    public static void Release(object? com)
    {
        if (com == null) return;
        try
        {
            if (Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
        catch (Exception) { /* already released / apartment gone */ }
    }

    /// <summary>SHCreateItemFromParsingName(parsingName) as IShellItem2; null when the item does not exist or cannot be bound.</summary>
    public static IAppsShellItem2? CreateShellItem(string parsingName, out int hresult)
    {
        var iid = AppsShellIds.IidShellItem2;
        hresult = AppsShellNative.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var pv);
        return hresult >= 0 ? Wrap<IAppsShellItem2>(pv) : null;
    }

    /// <summary>SHCreateItemFromParsingName(parsingName) as IShellItemImageFactory.</summary>
    public static IAppsShellItemImageFactory? CreateImageFactory(string parsingName, out int hresult)
    {
        var iid = AppsShellIds.IidShellItemImageFactory;
        hresult = AppsShellNative.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var pv);
        return hresult >= 0 ? Wrap<IAppsShellItemImageFactory>(pv) : null;
    }

    /// <summary>IShellItem2::GetString; null if the property is missing, empty or the call fails.</summary>
    public static string? GetString(IAppsShellItem2 item, AppsPropertyKey key)
    {
        int hr = item.GetString(ref key, out var p);
        var s = hr >= 0 ? TakeString(p) : null;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>IShellItem::GetDisplayName(sigdn); null on failure.</summary>
    public static string? GetDisplayName(IAppsShellItem2 item, uint sigdn)
    {
        int hr = item.GetDisplayName(sigdn, out var p);
        var s = hr >= 0 ? TakeString(p) : null;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>CoCreateInstance(clsid) for interface <typeparamref name="T"/> (identified by <paramref name="iid"/>) in-proc, falling back to a local server.</summary>
    public static T? CreateInstance<T>(Guid clsid, Guid iid) where T : class
    {
        int hr = AppsShellNative.CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer | ClsctxLocalServer, ref iid, out var pv);
        return hr >= 0 ? Wrap<T>(pv) : null;
    }
}
