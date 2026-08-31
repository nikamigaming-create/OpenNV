using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicDoorNumericContracts
{
    internal const int Sha256HexCharacters = 64;
}

internal sealed record ClassicDoorSound(
    string LogicalPath,
    string Sha256,
    string WavPath,
    string WavSha256,
    int Channels,
    int SampleWidthBytes,
    int SampleRate,
    int SampleFrames);

internal sealed record ClassicDoorFrameAsset(
    int Frame,
    string Path,
    string Sha256,
    int Width,
    int Height,
    int OffsetX,
    int OffsetY);

internal sealed record ClassicDoorSource(
    string PrototypeSha256,
    string ArtSha256,
    int StoredFramesPerSecond,
    int ActionFrame,
    int FrameCount,
    int ClosedFrame,
    int OpenFrame,
    ClassicDoorSound OpenSound,
    ClassicDoorSound CloseSound,
    IReadOnlyList<ClassicDoorFrameAsset> Frames)
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
        var runtimeAssets = source.GetProperty("runtimeAssets");
        var runtimeSounds = runtimeAssets.GetProperty("sounds");
        var frames = runtimeAssets.GetProperty("frames").EnumerateArray()
            .Select(row =>
            {
                var offset = row.GetProperty("offset").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray();
                if (offset.Length != 2)
                    throw new InvalidOperationException(
                        "Classic door frame offset is invalid.");
                return new ClassicDoorFrameAsset(
                    row.GetProperty("frame").GetInt32(),
                    Required(row, "path"),
                    Hash(row, "sha256"),
                    row.GetProperty("width").GetInt32(),
                    row.GetProperty("height").GetInt32(),
                    offset[0],
                    offset[1]);
            }).ToArray();
        var result = new ClassicDoorSource(
            Hash(prototype, "sha256"),
            Hash(art, "sha256"),
            animation.GetProperty("storedFramesPerSecond").GetInt32(),
            animation.GetProperty("actionFrame").GetInt32(),
            animation.GetProperty("frameCount").GetInt32(),
            animation.GetProperty("closedFrame").GetInt32(),
            animation.GetProperty("openFrame").GetInt32(),
            Sound(sounds.GetProperty("open"), runtimeSounds.GetProperty("open")),
            Sound(sounds.GetProperty("close"), runtimeSounds.GetProperty("close")),
            frames);
        if (result.PrototypeSha256 != expectedPrototypeSha256 ||
            result.ArtSha256 != expectedArtSha256 ||
            result.StoredFramesPerSecond <= 0 || result.FrameCount <= 1 ||
            result.ClosedFrame != 0 || result.OpenFrame != result.FrameCount - 1 ||
            result.ActionFrame < result.ClosedFrame || result.ActionFrame > result.OpenFrame ||
            result.OpenSound == result.CloseSound ||
            frames.Length != result.FrameCount ||
            !frames.Select(row => row.Frame).SequenceEqual(
                Enumerable.Range(result.ClosedFrame, result.FrameCount)) ||
            frames.Any(row => row.Width <= 0 || row.Height <= 0))
            throw new InvalidOperationException("Classic door source presentation is invalid.");
        return result;
    }

    private static ClassicDoorSound Sound(JsonElement source, JsonElement runtime)
    {
        var result = new ClassicDoorSound(
            Required(source, "logicalPath"),
            Hash(source, "sha256"),
            Required(runtime, "wav"),
            Hash(runtime, "wavSha256"),
            runtime.GetProperty("channels").GetInt32(),
            runtime.GetProperty("sampleWidthBytes").GetInt32(),
            runtime.GetProperty("sampleRate").GetInt32(),
            runtime.GetProperty("sampleFrames").GetInt32());
        if (Required(runtime, "logicalPath") != result.LogicalPath ||
            Hash(runtime, "sha256") != result.Sha256 || result.Channels <= 0 ||
            result.SampleWidthBytes <= 0 || result.SampleRate <= 0 || result.SampleFrames <= 0)
            throw new InvalidOperationException("Classic door runtime sound is invalid.");
        VerifiedGltfLoader.VerifyHash(result.WavPath, result.WavSha256);
        return result;
    }

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
    string? LastSoundLogicalPath,
    string Phase,
    double FrameElapsedSeconds,
    double PhaseElapsedSeconds)
{
    internal object Save() => new
    {
        Open,
        Blocked,
        Frame,
        LastSoundLogicalPath,
        Phase,
        FrameElapsedSeconds,
        PhaseElapsedSeconds,
    };

    internal static ClassicDoorState Restore(JsonElement source) => new(
        source.GetProperty("Open").GetBoolean(),
        source.GetProperty("Blocked").GetBoolean(),
        source.GetProperty("Frame").GetInt32(),
        source.GetProperty("LastSoundLogicalPath").ValueKind == JsonValueKind.Null
            ? null
            : source.GetProperty("LastSoundLogicalPath").GetString(),
        source.GetProperty("Phase").GetString() ?? "",
        source.GetProperty("FrameElapsedSeconds").GetDouble(),
        source.GetProperty("PhaseElapsedSeconds").GetDouble());
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

    internal ClassicDoorState BeginOpening()
    {
        if (State.Open)
            return State;
        if (State.Frame == Source.OpenFrame)
        {
            State = OpenTerminal(Source);
            return State;
        }
        State = new ClassicDoorState(
            true,
            Source.ActionFrame > Source.ClosedFrame,
            Source.ClosedFrame,
            Source.OpenSound.LogicalPath,
            "opening",
            0.0,
            0.0);
        Validate();
        return State;
    }

    internal ClassicDoorState BeginClosing()
    {
        if (!State.Open)
            return State;
        if (State.Frame == Source.ClosedFrame)
        {
            State = ClosedTerminal(Source);
            return State;
        }
        State = new ClassicDoorState(
            false,
            false,
            Source.OpenFrame,
            Source.CloseSound.LogicalPath,
            "closing",
            0.0,
            0.0);
        Validate();
        return State;
    }

    internal bool Advance(double deltaSeconds)
    {
        if (deltaSeconds < 0.0 || !double.IsFinite(deltaSeconds))
            throw new InvalidOperationException("Classic door playback delta is invalid.");
        if (State.Phase is not ("opening" or "closing") || deltaSeconds == 0.0)
            return false;
        var frameDuration = 1.0 / Source.StoredFramesPerSecond;
        var elapsed = State.FrameElapsedSeconds + deltaSeconds;
        var phaseElapsed = State.PhaseElapsedSeconds + deltaSeconds;
        var frame = State.Frame;
        while (elapsed >= frameDuration)
        {
            elapsed -= frameDuration;
            frame += State.Phase == "opening" ? 1 : -1;
            if (frame == (State.Phase == "opening" ? Source.OpenFrame : Source.ClosedFrame))
            {
                State = State with
                {
                    Blocked = State.Phase == "closing",
                    Frame = frame,
                    Phase = State.Phase == "opening" ? "open" : "closed",
                    FrameElapsedSeconds = 0.0,
                    PhaseElapsedSeconds = phaseElapsed,
                };
                Validate();
                return true;
            }
        }
        State = State with
        {
            Blocked = State.Phase == "opening"
                ? frame < Source.ActionFrame
                : frame <= Source.ActionFrame,
            Frame = frame,
            FrameElapsedSeconds = elapsed,
            PhaseElapsedSeconds = phaseElapsed,
        };
        Validate();
        return true;
    }

    internal static ClassicDoorState Closed(ClassicDoorSource source) =>
        new(false, true, source.ClosedFrame, null, "closed", 0.0, 0.0);

    internal static ClassicDoorState OpenTerminal(ClassicDoorSource source) =>
        new(true, false, source.OpenFrame, source.OpenSound.LogicalPath, "open", 0.0, 0.0);

    internal static ClassicDoorState ClosedTerminal(ClassicDoorSource source) =>
        new(false, true, source.ClosedFrame, source.CloseSound.LogicalPath, "closed", 0.0, 0.0);

    private void Validate()
    {
        var initial = State == Closed(Source);
        var openTerminal = State.Phase == "open" && State.Open && !State.Blocked &&
            State.Frame == Source.OpenFrame && State.FrameElapsedSeconds == 0.0 &&
            State.LastSoundLogicalPath == Source.OpenSound.LogicalPath;
        var closedTerminal = State.Phase == "closed" && !State.Open && State.Blocked &&
            State.Frame == Source.ClosedFrame && State.FrameElapsedSeconds == 0.0 &&
            State.LastSoundLogicalPath == Source.CloseSound.LogicalPath;
        var opening = State.Phase == "opening" && State.Open &&
            State.Frame is >= 0 && State.Frame < Source.OpenFrame &&
            State.LastSoundLogicalPath == Source.OpenSound.LogicalPath;
        var closing = State.Phase == "closing" && !State.Open &&
            State.Frame > Source.ClosedFrame && State.Frame <= Source.OpenFrame &&
            State.LastSoundLogicalPath == Source.CloseSound.LogicalPath;
        if ((!initial && !openTerminal && !closedTerminal && !opening && !closing) ||
            State.FrameElapsedSeconds < 0.0 || State.PhaseElapsedSeconds < 0.0 ||
            !double.IsFinite(State.FrameElapsedSeconds) ||
            !double.IsFinite(State.PhaseElapsedSeconds) ||
            State.FrameElapsedSeconds >= 1.0 / Source.StoredFramesPerSecond)
            throw new InvalidOperationException(
                "Classic door playback state is invalid.");
    }
}
