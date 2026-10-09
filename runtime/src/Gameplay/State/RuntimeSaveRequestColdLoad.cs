using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Read creates this from the actual selected file and already-decoded current
// campaign owners. Neither historical offsets nor a caller-supplied phase can
// create a cold execution epoch.
internal sealed class RuntimeSaveRequestColdLoad
{
    internal string Path { get; }
    internal string Sha256 { get; }
    internal RuntimeSaveRequestOrderSnapshot Snapshot { get; }
    internal IReadOnlyList<FalloutCompiledSliceReceipt> Suspended { get; }

    private RuntimeSaveRequestColdLoad(string path, string digest, RuntimeSaveRequestOrderSnapshot snapshot,
        IReadOnlyList<FalloutCompiledSliceReceipt> suspended)
    { Path = path; Sha256 = digest; Snapshot = snapshot; Suspended = suspended; }

    internal static RuntimeSaveRequestColdLoad Read(string path, string source, FalloutPluginStack records,
        RuntimeSaveRequestOrderSnapshot snapshot, FalloutQuestScriptsSnapshot scripts)
    {
        var full = System.IO.Path.GetFullPath(path);
        var suspended = SuspendedSlices(scripts);
        RuntimeSaveRequestOrder.ValidateSnapshot(snapshot, source, records, suspended);
        RuntimeSaveRequestOrder.RequirePublishedCapture(snapshot);
        return new(full, Digest(full), snapshot, suspended);
    }

    internal static IReadOnlyList<FalloutCompiledSliceReceipt> SuspendedSlices(FalloutQuestScriptsSnapshot scripts) =>
        scripts.Instances.Select(instance => instance.Compiled?.Pending?.LastSlice)
            .OfType<FalloutCompiledSliceReceipt>().Where(slice => slice.Disposition == "suspended").ToArray();

    internal void RequireUnchanged()
    {
        if (Digest(Path) != Sha256) throw new InvalidDataException("Selected save bytes changed before their cold queue handoff.");
    }
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
