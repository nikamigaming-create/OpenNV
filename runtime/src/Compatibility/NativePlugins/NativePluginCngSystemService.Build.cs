using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginCngSystemBuild
{
    internal static NativePluginCngServiceImage ReadSibling(string companion)
    {
        companion = Path.GetFullPath(companion);
        var directory = Path.GetDirectoryName(companion) ?? throw new InvalidDataException("Native companion has no exact build directory.");
        using var input = new FileStream(Path.Combine(directory, "build-manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var manifest = JsonDocument.Parse(input);
        var root = manifest.RootElement;
        if (!root.GetProperty("authoredOnly").GetBoolean() || root.GetProperty("machine").GetString() != "I386")
            throw new InvalidDataException("CNG service manifest is not a first-party x86 build.");
        string? serviceHash = null; var companionFound = false;
        foreach (var row in root.GetProperty("files").EnumerateArray())
        {
            var relative = row.GetProperty("path").GetString() ?? throw new InvalidDataException("Native build row has no path.");
            if (relative is not ("opennv_plugin_domain.exe" or "opennv_cng_service.exe")) continue;
            var path = Path.GetFullPath(Path.Combine(directory, relative));
            var sha = row.GetProperty("sha256").GetString() ?? throw new InvalidDataException("Native build row has no actual byte identity.");
            using var bytes = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (row.GetProperty("bytes").GetInt64() != bytes.Length ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected first-party native image differs from the exact build manifest.");
            if (relative == "opennv_plugin_domain.exe")
            {
                if (companionFound || !StringComparer.OrdinalIgnoreCase.Equals(path, companion))
                    throw new InvalidDataException("CNG service build does not identify the actual original-module companion.");
                companionFound = true;
            }
            else
            {
                if (serviceHash is not null) throw new InvalidDataException("CNG service build identity repeats.");
                serviceHash = sha;
            }
        }
        if (!companionFound || serviceHash is null)
            throw new NotSupportedException("Selected build lacks its exact first-party CNG service image.");
        return new(Path.Combine(directory, "opennv_cng_service.exe"), serviceHash);
    }
}
