using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginSteamSource
{
    internal string? ProviderHashCandidate { get; private set; }
    internal FileStream RetainedProviderInput(ulong generation)
    { Check(generation); return _provider ?? throw new ObjectDisposedException(nameof(NativePluginSteamSource)); }
    internal string HashRetainedProvider(ulong generation, NativePluginSteamFileIdentity nativeIdentity)
    {
        ProviderHashCandidate = null;
        var input = RetainedProviderInput(generation); nativeIdentity.Validate();
        if (NativePluginSteamFileIdentity.Read(input) != nativeIdentity)
            throw new InvalidDataException("Native platform loader opened another physical file than its retained C# original source.");
        var position = input.Position;
        Exception? primary = null;
        try
        {
            input.Position = 0; var sha = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            ProviderHashCandidate = sha; // Retain the actual computed value even if later admission refuses.
            if ((ulong)input.Position != nativeIdentity.Bytes || NativePluginSteamFileIdentity.Read(input) != nativeIdentity ||
                !sha.Equals(Declaration.ProviderSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Retained original platform bytes or kernel identity drifted during actual C# hashing.");
            return sha;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            try { input.Position = position; }
            catch (Exception cleanup)
            {
                if (primary is not null) throw new AggregateException("Original source hashing and retained input-position restoration failed.", primary, cleanup);
                throw;
            }
        }
    }
}
