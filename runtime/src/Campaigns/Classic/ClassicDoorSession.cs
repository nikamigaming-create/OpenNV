using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicDoorNumericContracts
{
    internal const int Sha256HexCharacters = 64;
}

internal sealed record ClassicDoorSound(string LogicalPath, string Sha256);

internal sealed record ClassicDoorSource(
    string PrototypeSha256,
    string ArtSha256,
    int StoredFramesPerSecond,
    int ActionFrame,
    int FrameCount,
    int ClosedFrame,
    int OpenFrame,
    ClassicDoorSound OpenSound,
    ClassicDoorSound CloseSound)
{
    internal static ClassicDoorSource Load(
        JsonElement source,
        string expectedPrototypeSha256,
        string expectedArtSha256)
    {
        var prototype = source.GetProperty("prototype");
        var art = source.GetProperty("art");
        var animation = source.GetProperty("animation");
        var sounds = source.GetProperty("sounds");
        var result = new ClassicDoorSource(
            Hash(prototype, "sha256"),
            Hash(art, "sha256"),
            animation.GetProperty("storedFramesPerSecond").GetInt32(),
            animation.GetProperty("actionFrame").GetInt32(),
            animation.GetProperty("frameCount").GetInt32(),
            animation.GetProperty("closedFrame").GetInt32(),
            animation.GetProperty("openFrame").GetInt32(),
            Sound(sounds.GetProperty("open")),
            Sound(sounds.GetProperty("close")));
        if (result.PrototypeSha256 != expectedPrototypeSha256 ||
            result.ArtSha256 != expectedArtSha256 ||
            result.StoredFramesPerSecond <= 0 || result.FrameCount <= 1 ||
            result.ClosedFrame != 0 || result.OpenFrame != result.FrameCount - 1 ||
            result.ActionFrame < result.ClosedFrame || result.ActionFrame > result.OpenFrame ||
            result.OpenSound == result.CloseSound)
            throw new InvalidOperationException("Classic door source presentation is invalid.");
        return result;
    }

    private static ClassicDoorSound Sound(JsonElement source) => new(
        Required(source, "logicalPath"),
        Hash(source, "sha256"));

    private static string Hash(JsonElement source, string name)
    {
        var value = Required(source, name);
        return value.Length == ClassicDoorNumericContracts.Sha256HexCharacters &&
            value.All(Uri.IsHexDigit) ? value :
            throw new InvalidOperationException("Classic door source hash is invalid.");
    }

    private static string Required(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Classic door source is missing {name}.");
}

internal sealed record ClassicDoorState(
    bool Open,
    bool Blocked,
    int Frame,
    string? LastSoundLogicalPath)
{
    internal object Save() => new { Open, Blocked, Frame, LastSoundLogicalPath };
}

internal sealed class ClassicDoorSession
{
    internal ClassicDoorSession(ClassicDoorSource source, ClassicDoorState? restored = null)
    {
        Source = source;
        State = restored ?? Closed(source);
        Validate();
    }

    internal ClassicDoorSource Source { get; }
    internal ClassicDoorState State { get; private set; }
    internal IEnumerable<int> OpeningFrames =>
        Enumerable.Range(Source.ClosedFrame, Source.FrameCount);
    internal IEnumerable<int> ClosingFrames => OpeningFrames.Reverse();

    internal ClassicDoorState OpenToSourceTerminal()
    {
        if (State.Open)
            return State;
        State = new ClassicDoorState(
            true,
            false,
            Source.OpenFrame,
            Source.OpenSound.LogicalPath);
        Validate();
        return State;
    }

    internal ClassicDoorState CloseToSourceTerminal()
    {
        if (!State.Open)
            return State;
        State = new ClassicDoorState(
            false,
            true,
            Source.ClosedFrame,
            Source.CloseSound.LogicalPath);
        Validate();
        return State;
    }

    internal static ClassicDoorState Closed(ClassicDoorSource source) =>
        new(false, true, source.ClosedFrame, null);

    internal static ClassicDoorState OpenTerminal(ClassicDoorSource source) =>
        new(true, false, source.OpenFrame, source.OpenSound.LogicalPath);

    internal static ClassicDoorState ClosedTerminal(ClassicDoorSource source) =>
        new(false, true, source.ClosedFrame, source.CloseSound.LogicalPath);

    private void Validate()
    {
        var closed = State == Closed(Source) || State == ClosedTerminal(Source);
        var open = State == OpenTerminal(Source);
        if (!closed && !open)
            throw new InvalidOperationException(
                "Classic door state is not a source terminal presentation state.");
    }
}
