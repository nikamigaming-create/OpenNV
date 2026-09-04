using OpenNV.Runtime.Diagnostics.Parity;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Retail parity publishing requires Windows named memory.");
if (args.Length != 2 || args[0] != "--channel")
    throw new ArgumentException("Usage: OpenNV.ParityRetailPublisher --channel <name>");

using var ring = ParitySharedMemoryRing.CreateOrOpen(args[1]);
ulong producerSequence = 0;
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;
    var frame = ParityRetailIngress.Parse(line, checked(++producerSequence));
    var ringSequence = ring.Publish(ParityTelemetryCodec.Encode(frame));
    Console.WriteLine($"OPENNV_RETAIL_PARITY_PUBLISHED producer={producerSequence} ring={ringSequence}");
    Console.Out.Flush();
}
