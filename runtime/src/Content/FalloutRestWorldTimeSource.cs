using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRestWorldTimeSource(string RestSourceSha256, uint InitialProcessBits,
    double ResetAbove, string ContractSha256)
{
    private const string Contract = "selected-zero-filled-process-static;float32-read-add-store;" +
        "reset-zero-if-greater-than100000-or-not-finite;ordinary-source-frame-and-rest-hour-consumers;" +
        "independent-of-calendar;current-cold-value-no-offline-time";
    internal string Identity => Hash(JsonSerializer.Serialize(this));

    internal static FalloutRestWorldTimeSource Read(FalloutSleepWaitSource source)
    {
        source.Validate();
        if (source.EngineSha256 is not ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new NotSupportedException("Selected source has no reviewed cumulative world-time consumer.");
        return new(source.Identity, 0, 100000, Hash(Contract));
    }

    internal void RequireSource(FalloutSleepWaitSource source)
    {
        if (this != Read(source))
            throw new InvalidDataException("Cumulative world time changed its selected source declaration.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
