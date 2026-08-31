using System.Text.Json;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2OpeningTailFrame(
    int SourceFrame,
    string Path,
    string PngSha256,
    long PngBytes);

internal sealed record Fo2OpeningTailContract(
    string MovieLogicalPath,
    string MovieSha256,
    long MovieBytes,
    string FadeConfigLogicalPath,
    string FadeConfigSha256,
    long FadeConfigBytes,
    int Width,
    int Height,
    int FrameRateNumerator,
    int FrameRateDenominator,
    int SourceFrameCount,
    int PlaybackStartFrame,
    int TailStartFrame,
    int TerminalFrame,
    int TerminalFrameRepeatedFrom,
    string TerminalFramePngSha256,
    IReadOnlyList<Fo2OpeningTailFrame> Frames,
    string AudioPath,
    string AudioSha256,
    long AudioBytes,
    int AudioSampleRate,
    int AudioChannels,
    int AudioSampleBytes,
    int AudioSampleFrames,
    long AudioStartNumerator,
    long AudioStartDenominator,
    long AudioDurationNumerator,
    long AudioDurationDenominator,
    int FadeStartFrame,
    int FadeEndFrame,
    int FadeSteps,
    bool MovieEndForcesBlack,
    int HandoffMapIndex,
    int HandoffElevation,
    int HandoffArrivalTile,
    int HandoffArrivalRotation,
    string HandoffPresentation,
    string ParityStatus)
{
    internal double FramePeriodSeconds =>
        FrameRateDenominator / (double)FrameRateNumerator;

    internal int TerminalFadeStep => TerminalFrame - FadeStartFrame + 1;

    internal float TerminalFadeFraction => TerminalFadeStep / (float)FadeSteps;

    internal float SourceFadeFraction(int sourceFrame)
    {
        if (sourceFrame < PlaybackStartFrame || sourceFrame > TerminalFrame)
            throw new InvalidOperationException(
                $"Fallout 2 Elder source frame {sourceFrame} is outside its decoded playback.");
        if (sourceFrame < FadeStartFrame)
            return 0.0f;
        return Math.Clamp(
            (sourceFrame - FadeStartFrame + 1) / (float)FadeSteps,
            0.0f,
            1.0f);
    }

    internal static Fo2OpeningTailContract Load(
        JsonElement row,
        JsonElement expected,
        string cacheRoot)
    {
        var source = row.GetProperty("source");
        var sourceMovie = source.GetProperty("movie");
        var sourceFade = source.GetProperty("fadeConfig");
        var expectedMovie = expected.GetProperty("movie");
        var expectedFade = expected.GetProperty("fadeConfig");
        VerifySource(sourceMovie, expectedMovie, "Elder MVE");
        VerifySource(sourceFade, expectedFade, "Elder fade CFG");

        var expectedVideo = expected.GetProperty("video");
        var video = row.GetProperty("video");
        var width = video.GetProperty("width").GetInt32();
        var height = video.GetProperty("height").GetInt32();
        var rateNumerator = video.GetProperty("frameRateNumerator").GetInt32();
        var rateDenominator = video.GetProperty("frameRateDenominator").GetInt32();
        var sourceFrames = video.GetProperty("sourceFrameCount").GetInt32();
        var playbackStart = video.GetProperty("playbackStartFrame").GetInt32();
        var tailStart = video.GetProperty("tailStartFrame").GetInt32();
        var terminalFrame = video.GetProperty("terminalFrame").GetInt32();
        var terminalRepeat = video.GetProperty("terminalFrameRepeatedFrom").GetInt32();
        var terminalHash = Fo2TemplePresentationCatalog.RequiredHash(
            video,
            "terminalFramePngSha256");
        if (Fo2TemplePresentationCatalog.RequiredString(video, "videoCodec") !=
                Fo2TemplePresentationCatalog.RequiredString(expectedVideo, "codec") ||
            width != expectedVideo.GetProperty("width").GetInt32() ||
            height != expectedVideo.GetProperty("height").GetInt32() ||
            rateNumerator != expectedVideo.GetProperty("frameRateNumerator").GetInt32() ||
            rateDenominator != expectedVideo.GetProperty("frameRateDenominator").GetInt32() ||
            sourceFrames != expectedVideo.GetProperty("sourceFrameCount").GetInt32() ||
            playbackStart != expectedVideo.GetProperty("playbackStartFrame").GetInt32() ||
            tailStart != expectedVideo.GetProperty("tailStartFrame").GetInt32() ||
            !video.GetProperty("sourceFrameNumbersOneBased").GetBoolean() ||
            playbackStart != 1 || terminalFrame != sourceFrames ||
            tailStart <= playbackStart ||
            terminalRepeat < tailStart || terminalRepeat > terminalFrame)
            throw new InvalidOperationException(
                "Fallout 2 Elder full-playback video contract drifted.");

        var frameRows = video.GetProperty("frames").EnumerateArray().ToArray();
        var expectedFrameCount = sourceFrames - playbackStart + 1;
        if (video.GetProperty("playbackFrameCount").GetInt32() != expectedFrameCount ||
            frameRows.Length != expectedFrameCount)
            throw new InvalidOperationException(
                "Fallout 2 Elder full-playback frame coverage drifted.");
        var frames = new List<Fo2OpeningTailFrame>(frameRows.Length);
        for (var index = 0; index < frameRows.Length; index++)
        {
            var frame = frameRows[index];
            var sourceFrame = frame.GetProperty("sourceFrame").GetInt32();
            if (sourceFrame != playbackStart + index)
                throw new InvalidOperationException(
                    "Fallout 2 Elder full-playback source-frame order drifted.");
            var path = VerifyCacheFile(
                cacheRoot,
                Fo2TemplePresentationCatalog.RequiredString(frame, "png"),
                Fo2TemplePresentationCatalog.RequiredHash(frame, "pngSha256"),
                frame.GetProperty("pngBytes").GetInt64(),
                $"Fallout 2 Elder source frame {sourceFrame}");
            frames.Add(new Fo2OpeningTailFrame(
                sourceFrame,
                path,
                Fo2TemplePresentationCatalog.RequiredHash(frame, "pngSha256"),
                frame.GetProperty("pngBytes").GetInt64()));
        }
        if (frames[^1].PngSha256 != terminalHash ||
            frames.Where(frame => frame.SourceFrame >= terminalRepeat)
                .Any(frame => frame.PngSha256 != terminalHash) ||
            terminalRepeat > tailStart &&
                frames.Single(frame => frame.SourceFrame == terminalRepeat - 1).PngSha256 ==
                    terminalHash)
            throw new InvalidOperationException(
                "Fallout 2 Elder repeated terminal-frame identity drifted.");

        var expectedAudio = expected.GetProperty("audio");
        var audio = row.GetProperty("audio");
        var audioPath = VerifyCacheFile(
            cacheRoot,
            Fo2TemplePresentationCatalog.RequiredString(audio, "wav"),
            Fo2TemplePresentationCatalog.RequiredHash(audio, "wavSha256"),
            audio.GetProperty("wavBytes").GetInt64(),
            "Fallout 2 Elder tail PCM");
        var audioRate = audio.GetProperty("sampleRate").GetInt32();
        var audioChannels = audio.GetProperty("channels").GetInt32();
        var audioBytesPerSample = audio.GetProperty("sampleBytes").GetInt32();
        if (Fo2TemplePresentationCatalog.RequiredString(video, "audioCodec") !=
                Fo2TemplePresentationCatalog.RequiredString(expectedAudio, "sourceCodec") ||
            audioRate != expectedAudio.GetProperty("sampleRate").GetInt32() ||
            audioChannels != expectedAudio.GetProperty("channels").GetInt32() ||
            audioBytesPerSample != expectedAudio.GetProperty("sampleBytes").GetInt32())
            throw new InvalidOperationException(
                "Fallout 2 Elder tail audio contract drifted.");

        var expectedFadeContract = expected.GetProperty("fade");
        var fade = row.GetProperty("fade");
        var fadeStart = fade.GetProperty("startFrame").GetInt32();
        var fadeSteps = fade.GetProperty("steps").GetInt32();
        var fadeEnd = fade.GetProperty("endFrame").GetInt32();
        if (fadeStart != expectedFadeContract.GetProperty("startFrame").GetInt32() ||
            Fo2TemplePresentationCatalog.RequiredString(fade, "type") != "out" ||
            !fade.GetProperty("color").EnumerateArray().Select(value => value.GetInt32())
                .SequenceEqual([0, 0, 0]) ||
            fadeSteps != expectedFadeContract.GetProperty("steps").GetInt32() ||
            fadeEnd != fadeStart + fadeSteps - 1 ||
            fadeStart != tailStart || terminalFrame < fadeStart ||
            terminalFrame > fadeEnd ||
            !fade.GetProperty("movieEndForcesBlack").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 Elder source fade contract drifted.");

        var handoff = row.GetProperty("handoff");
        var expectedHandoff = expected.GetProperty("handoff");
        if (handoff.GetProperty("mapIndex").GetInt32() !=
                expectedHandoff.GetProperty("mapIndex").GetInt32() ||
            handoff.GetProperty("elevation").GetInt32() !=
                expectedHandoff.GetProperty("elevation").GetInt32() ||
            handoff.GetProperty("arrivalTile").GetInt32() !=
                expectedHandoff.GetProperty("arrivalTile").GetInt32() ||
            handoff.GetProperty("arrivalRotation").GetInt32() !=
                expectedHandoff.GetProperty("arrivalRotation").GetInt32() ||
            Fo2TemplePresentationCatalog.RequiredString(handoff, "presentation") !=
                Fo2TemplePresentationCatalog.RequiredString(
                    expectedHandoff,
                    "presentation") ||
            Fo2TemplePresentationCatalog.RequiredString(handoff, "parityStatus") !=
                Fo2TemplePresentationCatalog.RequiredString(
                    expectedHandoff,
                    "parityStatus"))
            throw new InvalidOperationException(
                "Fallout 2 Elder-to-Arroyo handoff contract drifted.");

        return new Fo2OpeningTailContract(
            Fo2TemplePresentationCatalog.RequiredString(sourceMovie, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(sourceMovie, "sha256"),
            sourceMovie.GetProperty("bytes").GetInt64(),
            Fo2TemplePresentationCatalog.RequiredString(sourceFade, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(sourceFade, "sha256"),
            sourceFade.GetProperty("bytes").GetInt64(),
            width,
            height,
            rateNumerator,
            rateDenominator,
            sourceFrames,
            playbackStart,
            tailStart,
            terminalFrame,
            terminalRepeat,
            terminalHash,
            frames,
            audioPath,
            Fo2TemplePresentationCatalog.RequiredHash(audio, "wavSha256"),
            audio.GetProperty("wavBytes").GetInt64(),
            audioRate,
            audioChannels,
            audioBytesPerSample,
            audio.GetProperty("sampleFrames").GetInt32(),
            audio.GetProperty("sourceStartNumerator").GetInt64(),
            audio.GetProperty("sourceStartDenominator").GetInt64(),
            audio.GetProperty("sourceDurationNumerator").GetInt64(),
            audio.GetProperty("sourceDurationDenominator").GetInt64(),
            fadeStart,
            fadeEnd,
            fadeSteps,
            true,
            handoff.GetProperty("mapIndex").GetInt32(),
            handoff.GetProperty("elevation").GetInt32(),
            handoff.GetProperty("arrivalTile").GetInt32(),
            handoff.GetProperty("arrivalRotation").GetInt32(),
            Fo2TemplePresentationCatalog.RequiredString(handoff, "presentation"),
            Fo2TemplePresentationCatalog.RequiredString(handoff, "parityStatus"));
    }

    private static void VerifySource(
        JsonElement row,
        JsonElement expected,
        string label)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") !=
                Fo2TemplePresentationCatalog.RequiredString(expected, "logicalPath") ||
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256") !=
                Fo2TemplePresentationCatalog.RequiredHash(expected, "sha256") ||
            row.GetProperty("bytes").GetInt64() <= 0)
            throw new InvalidOperationException($"Fallout 2 {label} identity drifted.");
    }

    private static string VerifyCacheFile(
        string cacheRoot,
        string relative,
        string hash,
        long bytes,
        string label)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException($"{label} path must be cache-relative.");
        var path = Path.GetFullPath(Path.Combine(
            cacheRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(
                cacheRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} path escapes its cache.");
        Fo2TemplePresentationCatalog.VerifyFile(path, hash, bytes, label);
        return path;
    }
}
