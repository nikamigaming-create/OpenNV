using System.Diagnostics;
using System.Text.Json;

namespace OpenNV.LiveHarness;

// One actual viewport read through the existing diagnostic owner. It neither
// supplies gameplay input nor joins a retail comparison. The caller inspects
// or exports the returned private files and deletes them in its finally path.
internal static class LiveFrameInspection
{
    private sealed record Configuration(string CommandDirectory, int ProcessId,
        int ReceiptTimeoutMilliseconds = 5000);

    internal static async Task Run(string configurationPath)
    {
        if (!Path.IsPathFullyQualified(configurationPath))
            throw new ArgumentException("Frame inspection requires an absolute private configuration.");
        var configuration = SequentialInputCommandClient.ReadJson(configurationPath).Deserialize<Configuration>(Program.Json)
            ?? throw new InvalidDataException("Frame inspection configuration is absent.");
        var clock = Stopwatch.StartNew();
        long Microseconds() => checked((long)((decimal)clock.ElapsedTicks * 1_000_000 / Stopwatch.Frequency));
        using var client = new SequentialInputCommandClient(configuration.CommandDirectory, "opennv",
            configuration.ProcessId, configuration.ReceiptTimeoutMilliseconds, Microseconds);
        var request = client.ReadState().GetProperty("nextCommandRequest").GetInt64();
        var prefix = Path.Combine(client.Directory, $"frame-{request:D10}");
        var files = new[] { prefix + ".pixels", prefix + ".png", prefix + ".frame.json" };
        if (files.Any(File.Exists))
            throw new IOException("Frame inspection refuses to replace an existing diagnostic frame.");
        var completed = false;
        try
        {
            var receipt = await client.SendAsync("{\"op\":\"capture\"}", CancellationToken.None).ConfigureAwait(false);
            if (receipt.Request != request || !receipt.Delivered)
                throw new InvalidDataException("The actual viewport owner refused the requested frame: " + receipt.Message);
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < configuration.ReceiptTimeoutMilliseconds)
            {
                _ = client.ReadState();
                var actualReceipt = SequentialInputCommandClient.ReadJson(Path.Combine(client.Directory, $"{request:D10}.receipt.json"));
                if (!actualReceipt.GetProperty("delivered").GetBoolean())
                    throw new IOException(actualReceipt.GetProperty("message").GetString());
                if (files.All(File.Exists))
                {
                    var frame = SequentialInputCommandClient.ReadJson(files[2]);
                    if (frame.GetProperty("schema").GetString() != "opennv-live-harness-frame/v1" ||
                        frame.GetProperty("request").GetInt64() != request ||
                        frame.GetProperty("width").GetInt32() <= 0 || frame.GetProperty("height").GetInt32() <= 0 ||
                        frame.GetProperty("drawCount").GetUInt64() == 0 || new FileInfo(files[0]).Length == 0 ||
                        new FileInfo(files[1]).Length == 0)
                        throw new InvalidDataException("The native viewport frame has no complete original readback.");
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        schema = "opennv-private-frame-inspection/v1", process = configuration.ProcessId,
                        request, metadata = frame, pixelsPath = files[0], imagePath = files[1], metadataPath = files[2],
                        temporary = true, cleanup = "caller finally after inspection or requested export",
                        gameplayParity = "unverified", continuousRecording = false
                    }, Program.Json));
                    completed = true;
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            throw new TimeoutException("The actual viewport did not finish the requested readback; the entered capture is not retried.");
        }
        finally
        {
            if (!completed)
                foreach (var file in files) File.Delete(file);
        }
    }
}
