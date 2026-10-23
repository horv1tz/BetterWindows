namespace BetterWindows.Host.Bridge;

/// <summary>What an outgoing message is, as far as queueing policy is concerned.</summary>
public enum OutboundKind
{
    /// <summary>Reply to a call: always delivered (the page is waiting for it).</summary>
    Result,
    /// <summary>A state event (full snapshot): only the newest per type is worth sending.</summary>
    StateEvent,
    /// <summary>A transient event: delivered in order, oldest dropped under pressure.</summary>
    TransientEvent,
}

/// <summary>Head of an outgoing envelope.</summary>
public readonly record struct OutboundHead(OutboundKind Kind, string? Type);

/// <summary>
/// Cheap classification of outgoing JSON without parsing the whole message. Core builds every envelope with <c>Envelope.ToJson()</c>, which serialises
/// <c>v, kind, id, type, payload</c> in that order, so the head sits in the first few dozen characters. Anything unrecognised is treated as a transient event.
/// </summary>
public static class EnvelopeSniffer
{
    private const int HeadChars = 320;

    public static OutboundHead Sniff(string json)
    {
        var head = json.AsSpan(0, Math.Min(json.Length, HeadChars));
        var kind = ReadString(head, "\"kind\":\"");
        if (kind.SequenceEqual("result")) return new OutboundHead(OutboundKind.Result, null);
        if (!kind.SequenceEqual("event")) return new OutboundHead(OutboundKind.TransientEvent, null);
        var type = ReadString(head, "\"type\":\"");
        if (type.IsEmpty) return new OutboundHead(OutboundKind.TransientEvent, null);
        var t = type.ToString();
        return new OutboundHead(BetterWindows.Core.Protocol.InitialState.IsState(t) ? OutboundKind.StateEvent : OutboundKind.TransientEvent, t);
    }

    private static ReadOnlySpan<char> ReadString(ReadOnlySpan<char> head, string key)
    {
        var at = head.IndexOf(key.AsSpan(), StringComparison.Ordinal);
        if (at < 0) return default;
        var rest = head[(at + key.Length)..];
        var end = rest.IndexOf('"');
        return end < 0 ? default : rest[..end];
    }
}
