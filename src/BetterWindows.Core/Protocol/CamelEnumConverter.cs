using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterWindows.Core.Protocol;

/// <summary>Serialises enums as lowerCamelCase strings ("missionControl"). .NET 8 has no per-member rename attribute.</summary>
public sealed class CamelEnumConverter : JsonStringEnumConverter
{
    public CamelEnumConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}
