using System.Runtime.InteropServices;

namespace BetterWindows.Native.Interop;

/// <summary>
/// UNDOCUMENTED COM interface used by the Windows audio control panel to change the default playback device (class
/// 870AF99C-171D-4F9E-AF0D-E63DF40C2BC9). Only the vtable slots up to <c>SetDefaultEndpoint</c> matter: the methods before it are placeholders with
/// the right slot count (IUnknown 3 + 10 methods) and are never called. The layout is the Windows 10/11 one used by the open-source audio switchers; a
/// later Windows release may change it, in which case binding or the call fails and <c>CanSetDefaultDevice</c> stays false.
/// </summary>
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStatusPolicyConfig
{
    [PreserveSig] int Slot0GetMixFormat();
    [PreserveSig] int Slot1GetDeviceFormat();
    [PreserveSig] int Slot2ResetDeviceFormat();
    [PreserveSig] int Slot3SetDeviceFormat();
    [PreserveSig] int Slot4GetProcessingPeriod();
    [PreserveSig] int Slot5SetProcessingPeriod();
    [PreserveSig] int Slot6GetShareMode();
    [PreserveSig] int Slot7SetShareMode();
    [PreserveSig] int Slot8GetPropertyValue();
    [PreserveSig] int Slot9SetPropertyValue();
    /// <summary>Sets the default endpoint for one role (ERole: 0 console, 1 multimedia, 2 communications). Returns HRESULT.</summary>
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
}

/// <summary>Constants and the activation helper for <see cref="IStatusPolicyConfig"/>.</summary>
internal static class StatusPolicyConfigClass
{
    /// <summary>CLSID of the PolicyConfig coclass (undocumented).</summary>
    public static readonly Guid Clsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    /// <summary>ERole values for <see cref="IStatusPolicyConfig.SetDefaultEndpoint"/>: console, multimedia and communications.</summary>
    public static readonly int[] Roles = { 0, 1, 2 };

    /// <summary>Creates the coclass. Throws if the class is missing or the activation is refused: callers treat that as "not supported".</summary>
    public static IStatusPolicyConfig Create()
    {
        var type = Type.GetTypeFromCLSID(Clsid, throwOnError: true)!;
        return (IStatusPolicyConfig)Activator.CreateInstance(type)!;
    }
}
