using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.AnimationCompare;

internal static class ObservationCommand
{
    internal const string Usage = "observe <absolute-command-dir> <pid> <plugin:reference> <seconds .1..8> <fresh-output.json> [actor|object]";

    internal static async Task<int> Run(string[] arguments)
    {
        if (arguments.Length is < 6 or > 7 || arguments[0] != "observe" ||
            !int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0 ||
            !double.TryParse(arguments[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || seconds is < .1 or > 8)
            throw new ArgumentException(Usage);
        var kind = arguments.Length == 7 ? arguments[6] : "actor";
        if (kind is not ("actor" or "object")) throw new ArgumentException(Usage);
        var reference = arguments[3];
        var separator = reference.LastIndexOf(':');
        if (separator <= 0 || reference.Any(char.IsControl) ||
            !uint.TryParse(reference.AsSpan(separator + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var objectId) ||
            objectId > 0xffffff)
            throw new ArgumentException("Observation requires an exact plugin:24-bit-reference identity.");
        reference = reference[..separator] + $":{objectId:x6}";
        if (!Path.IsPathFullyQualified(arguments[5])) throw new ArgumentException("Observation output must be absolute.");
        var output = Path.GetFullPath(arguments[5]);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Use a fresh observation output path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 20));
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; timeout.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var channel = new RigFileCommandChannel(arguments[1], pid);
            var started = Stopwatch.GetTimestamp();
            var (request, receipt) = await channel.SendAsync(new { op = kind + ".pose", reference, seconds }, timeout.Token);
            var source = Path.Combine(channel.DirectoryPath, $"{request:D10}" + (kind == "actor" ? ".pose.json" : ".object-pose.json"));
            while (!File.Exists(source))
            {
                timeout.Token.ThrowIfCancellationRequested();
                _ = channel.ReadState();
                await Task.Delay(25, timeout.Token);
            }
            _ = channel.ReadState();
            var pose = RigFileCommandChannel.ReadJson(source, 64 * 1024 * 1024);
            // Preserve the actual artifact, including incomplete observations;
            // delivery acceptance alone never becomes a capture success.
            File.Move(source, output, overwrite: false);
            using var stream = File.OpenRead(output);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, timeout.Token)).ToLowerInvariant();
            var expectedSchema = kind == "actor" ? "opennv-native-actor-pose/v2" : "opennv-native-object-pose/v1";
            var reason = pose.GetProperty("reason").GetString();
            var physics = pose.GetProperty("physicsSamples").GetInt32();
            var draw = pose.GetProperty("drawSamples").GetInt32();
            var complete = pose.GetProperty("schema").GetString() == expectedSchema &&
                string.Equals(pose.GetProperty("reference").GetString(), reference, StringComparison.OrdinalIgnoreCase) &&
                pose.GetProperty("monotonicFrequency").GetInt64() == Stopwatch.Frequency &&
                pose.GetProperty("startedTicks").GetInt64() >= started &&
                reason == (kind == "actor" ? "completed" : "duration-complete") &&
                (!pose.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null) && physics > 0 && draw >= 2;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = complete ? "observed" : "incomplete",
                output,
                sha256 = hash,
                process = pid,
                request,
                receipt,
                reference,
                kind,
                requestedSeconds = seconds,
                reason,
                physicsSamples = physics,
                drawSamples = draw,
                samples = pose.GetProperty("samples").GetArrayLength(),
                gameplayMutations = false,
                parityCertified = false,
                boundary = "Actual bounded pose observation only; no checkpoint, preparation, input, camera or action changes."
            }, Program.Json));
            return complete ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
