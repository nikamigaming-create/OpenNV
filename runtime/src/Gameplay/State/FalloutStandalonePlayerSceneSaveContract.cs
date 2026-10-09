using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutStandalonePlayerSceneSaveContract
{
    internal static void ValidateSource(FalloutPluginStack records, FalloutActorProcessRuntimeSnapshot runtime)
    {
        if (runtime.StandaloneMain is not { } main) return;
        var saved = main.Scene;
        FalloutStandalonePlayerSceneState.RequirePlayable(saved);
        var source = records.OwnedSource ?? throw new InvalidDataException("Cold Player scene has no actual selected owned source.");
        if (saved.Source.Main != main.Source || saved.Stack != source.StackId || saved.CapturedProcess != runtime.CapturedProcess)
            throw new InvalidDataException("Cold Player scene changed its exact selected Main/stack/process declaration.");
        foreach (var resource in saved.FirstPersonHistory.Select(owner => (owner.Publication.Resource, owner.Publication.ResourceSha256)).Distinct())
        {
            if (!source.TryRead(resource.Resource, null, out var bytes, out _) ||
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != resource.ResourceSha256)
                throw new InvalidDataException("Cold Player first-person scene no longer belongs to its actual winning source resource.");
        }
    }
}
