using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

// A selected engine contract, not a source spell whitelist. Original code and
// addresses stay private; the owned winner still supplies every effect field.
internal sealed record FalloutScriptedEffectClockSource(string EngineSha256,
    string RuntimeSha256, string WorldTimeSha256, string ContractSha256)
{
    private const string Contract = "source-float32-effect-elapsed-and-update-delta;" +
        "condition-floor-bucket-before-elapsed;positive-source-update-only;" +
        "duration-clamp-before-script-update;start-zero-update-delta-finish-zero;" +
        "same-ordered-event-list-until-finish;condition-restart-keeps-elapsed;" +
        "script-prefix-and-closure-before-local-retirement;no-offline-cold-time";
    internal string Identity => Hash(JsonSerializer.Serialize(this));
    internal bool CompareElapsedAsFloat32 => EngineSha256 == "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";

    internal static FalloutScriptedEffectClockSource Read(FalloutSleepWaitSource source)
    {
        source.Validate();
        RequireEngine(source.EngineSha256);
        return new(source.EngineSha256, source.RuntimeSha256,
            FalloutRestWorldTimeSource.Read(source).Identity, Hash(Contract));
    }

    internal void RequireCurrent(FalloutSleepWaitSource source)
    {
        if (this != Read(source))
            throw new InvalidDataException("Scripted-effect clock changed its selected engine/world-time source.");
    }

    internal void Validate()
    {
        RequireEngine(EngineSha256);
        if (!FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            WorldTimeSha256 is not { Length: 64 } || !WorldTimeSha256.All(Uri.IsHexDigit) || ContractSha256 != Hash(Contract))
            throw new InvalidDataException("Scripted-effect clock has no complete selected-source contract.");
    }

    private static void RequireEngine(string hash)
    {
        if (hash is not ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new NotSupportedException("Selected engine has no reviewed scripted-effect clock consumer.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
