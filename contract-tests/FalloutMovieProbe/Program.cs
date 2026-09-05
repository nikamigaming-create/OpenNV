using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpenNV.Runtime.Content;

if (args is ["--codec-fixture", var fixtureScenario, var fixtureRole])
{
    await RunCodecFixture(fixtureScenario, fixtureRole);
    return;
}

var defaults = FalloutMovieCommand.FromScript("; PlayBink \"comment.bik\"\nPlayBink \"nested/movie with spaces.bik\" ; trailing comment").Single();
Require(defaults == new FalloutMovieCommand("nested/movie with spaces.bik", false, true, true, true), "default flags/quoted path");
var flags = FalloutMovieCommand.FromScript("playbink \"owned.bik\" 1 1 0 1\nPlayBink \"second.bik\" -1 0");
Require(flags.Count == 2 && flags[0].Interruptible && flags[0].MuteWorldAudio && !flags[0].PauseMusic && flags[0].Letterboxed, "independent flags");
Require(flags[1].Interruptible && !flags[1].MuteWorldAudio && flags[1].PauseMusic, "partial flags");
foreach (var invalid in new[] { "PlayBink unquoted.bik", "PlayBink \"x.bik\" 1 1 1 1 1", "PlayBink \"x.bik\" variable" })
{
    try { FalloutMovieCommand.FromScript(invalid); throw new InvalidOperationException("Accepted unsupported command: " + invalid); }
    catch (NotSupportedException) { }
}
Console.WriteLine("OPENNV_MOVIE_COMMAND_OK defaults=true quotedPaths=true sourceOrder=true unsupportedArgumentsRejected=true");
var fixtureSource = Path.GetTempFileName();
try
{
    foreach (var scenario in new[] { "complete", "no-audio", "truncated", "extra-video", "partial-pcm", "empty-pcm", "nonfinite-pcm", "decoder-error" })
        await VerifyDecoder(scenario, fixtureSource);
    for (var repeat = 0; repeat < 4; ++repeat)
        await VerifyCancellation("cancel-decode", fixtureSource);
    await VerifyCancellation("cancel-probe", fixtureSource);
}
finally { File.Delete(fixtureSource); }
Console.WriteLine("OPENNV_MOVIE_LIFECYCLE_OK noAudio=true videoExtent=true pcmIntegrity=true failedExit=true cancellation=true repeatedCleanup=true");
if (args.Length != 0)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var processIds = new List<int>();
    await using var decoder = new OwnedMovieDecoder(start => TrackProcess(start, processIds));
    await decoder.OpenAsync(Path.GetFullPath(args[0])).WaitAsync(timeout.Token);
    var info = decoder.Info;
    var audio = info.AudioChannels == 0 ? null : decoder.Audio.ReadAsync(timeout.Token).AsTask();
    var decodedFrames = Math.Min(30, info.FrameCount);
    for (var index = 0; index < decodedFrames; ++index)
    {
        var frame = await decoder.Video.ReadAsync(timeout.Token);
        Require(frame.Index == index && frame.Rgba.Length == checked(info.Width * info.Height * 4), "consecutive native-size frames");
    }
    if (audio is not null)
    {
        var samples = await audio;
        Require(samples.Length != 0 && samples.Length % info.AudioChannels == 0 && samples.All(float.IsFinite), "PCM frames");
    }
    Require(decoder.Failure is null, "decoder error");
    await decoder.DisposeAsync().AsTask().WaitAsync(timeout.Token);
    RequireProcessesExited(processIds);
    Console.WriteLine($"OPENNV_OWNED_MOVIE_DECODE_OK width={info.Width} height={info.Height} rate={info.RateNumerator}/{info.RateDenominator} frames={info.FrameCount} decodedFrames={decodedFrames} audioRate={info.AudioRate} audioChannels={info.AudioChannels} workersReaped=true cacheWrites=0 parity=unmeasured");
}

static async Task VerifyDecoder(string scenario, string source)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var processes = new List<int>();
    await using var decoder = FixtureDecoder(scenario, processes);
    await decoder.OpenAsync(source).WaitAsync(timeout.Token);
    var video = Drain(decoder.Video, timeout.Token);
    var audio = Drain(decoder.Audio, timeout.Token);
    try { await Task.WhenAll(video, audio).WaitAsync(timeout.Token); }
    catch (Exception) when (decoder.Failure is not null) { }
    if (scenario is "complete" or "no-audio")
    {
        Require(decoder.Failure is null && video.IsCompletedSuccessfully && audio.IsCompletedSuccessfully, scenario + " EOF");
        Require(video.Result.Count == 4 && video.Result.Select(frame => frame.Index).SequenceEqual(new long[] { 0, 1, 2, 3 }), "frame order");
        Require(video.Result.SelectMany(frame => frame.Rgba).SequenceEqual(Enumerable.Range(0, 64).Select(index => (byte)index)), "unmodified pixels");
        Require(audio.Result.Count == (scenario == "no-audio" ? 0 : 1), "audio presence");
    }
    else
        Require(decoder.Failure is not null, scenario + " fails visibly");
    await decoder.DisposeAsync().AsTask().WaitAsync(timeout.Token);
    RequireProcessesExited(processes);
}

static async Task VerifyCancellation(string scenario, string source)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var processes = new List<int>();
    await using var decoder = FixtureDecoder(scenario, processes);
    var opening = decoder.OpenAsync(source);
    if (scenario != "cancel-probe")
        await opening.WaitAsync(timeout.Token);
    await decoder.DisposeAsync().AsTask().WaitAsync(timeout.Token);
    try { await opening; }
    catch (OperationCanceledException) { }
    Require(decoder.Failure is null, "intentional interruption is not decode failure");
    RequireProcessesExited(processes);
}

static OwnedMovieDecoder FixtureDecoder(string scenario, List<int> processes) => new(start =>
{
    var role = start.FileName == "ffprobe" ? "probe" : start.ArgumentList.Contains("0:a:0") ? "audio" : "video";
    start.FileName = Environment.ProcessPath!;
    start.ArgumentList.Clear();
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--codec-fixture");
    start.ArgumentList.Add(scenario);
    start.ArgumentList.Add(role);
    return TrackProcess(start, processes);
});

static Process? TrackProcess(ProcessStartInfo start, List<int> processes)
{
    var process = Process.Start(start);
    if (process is not null)
        processes.Add(process.Id);
    return process;
}

static void RequireProcessesExited(List<int> processes)
{
    foreach (var id in processes)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            Require(process.HasExited, "codec process survived disposal");
        }
        catch (ArgumentException) { }
    }
}

static async Task<List<T>> Drain<T>(System.Threading.Channels.ChannelReader<T> reader, CancellationToken token)
{
    var result = new List<T>();
    await foreach (var item in reader.ReadAllAsync(token))
        result.Add(item);
    return result;
}

static async Task RunCodecFixture(string scenario, string role)
{
    if ((scenario == "cancel-probe" && role == "probe") || (scenario == "cancel-decode" && role != "probe"))
        await Task.Delay(Timeout.InfiniteTimeSpan);
    if (role == "probe")
    {
        var streams = new List<object> { new { codec_type = "video", codec_name = "binkvideo", width = 2, height = 2,
            r_frame_rate = "24/1", time_base = "1/24", start_pts = 0, duration_ts = 4 } };
        if (scenario != "no-audio")
            streams.Add(new { codec_type = "audio", sample_rate = "8000", channels = 2 });
        Console.WriteLine(JsonSerializer.Serialize(new { streams }));
        return;
    }
    var output = Console.OpenStandardOutput();
    if (role == "video")
    {
        var count = scenario == "truncated" ? 44 : scenario == "extra-video" ? 65 : 64;
        await output.WriteAsync(Enumerable.Range(0, count).Select(index => (byte)index).ToArray());
        if (scenario == "decoder-error")
        {
            Console.Error.WriteLine("synthetic codec failure");
            Environment.ExitCode = 9;
        }
    }
    else
    {
        var samples = new float[] { 0.25f, -0.25f, 0.5f, scenario == "nonfinite-pcm" ? float.NaN : -0.5f };
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var count = scenario == "partial-pcm" ? 5 : scenario == "empty-pcm" ? 0 : bytes.Length;
        await output.WriteAsync(bytes.AsMemory(0, count));
    }
}

static void Require(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException("Movie contract failed: " + name);
}
