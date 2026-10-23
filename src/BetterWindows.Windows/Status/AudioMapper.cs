using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>Pure audio state assembly and comparison (unit-tested on any OS).</summary>
internal static class AudioMapper
{
    /// <summary>Clamps a master volume to 0..1; NaN and infinities (a COM property read that went wrong) become 0.</summary>
    public static double ClampVolume(double volume) =>
        double.IsNaN(volume) || double.IsInfinity(volume) ? 0.0 : Math.Clamp(volume, 0.0, 1.0);

    /// <summary>
    /// Builds the protocol state from the active render endpoints and the default one. A default id that is not among the active endpoints
    /// (removed between the two reads) is treated as "no default device".
    /// </summary>
    public static AudioState Build(IReadOnlyList<(string Id, string Name)> activeEndpoints, string? defaultId, double? volume, bool? muted)
    {
        var devices = new List<AudioDevice>(activeEndpoints.Count);
        string? defaultName = null;
        foreach (var (id, name) in activeEndpoints)
        {
            var isDefault = defaultId is not null && string.Equals(id, defaultId, StringComparison.Ordinal);
            if (isDefault) defaultName = name;
            devices.Add(new AudioDevice { Id = id, Name = name, IsDefault = isDefault });
        }
        var hasDefault = defaultName is not null;
        return new AudioState
        {
            Volume = hasDefault && volume is not null ? ClampVolume(volume.Value) : 0.0,
            Muted = hasDefault && muted == true,
            DeviceId = hasDefault ? defaultId : null,
            DeviceName = defaultName,
            Devices = devices,
        };
    }

    public static bool Same(AudioState a, AudioState b)
    {
        if (a.Volume != b.Volume || a.Muted != b.Muted || a.DeviceId != b.DeviceId || a.DeviceName != b.DeviceName || a.Devices.Count != b.Devices.Count)
            return false;
        for (var i = 0; i < a.Devices.Count; i++)
        {
            var x = a.Devices[i];
            var y = b.Devices[i];
            if (x.Id != y.Id || x.Name != y.Name || x.IsDefault != y.IsDefault) return false;
        }
        return true;
    }
}
