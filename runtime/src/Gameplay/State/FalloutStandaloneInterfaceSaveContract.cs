using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutStandaloneInterfaceSaveContract
{
    internal static void ValidateSource(FalloutPluginStack records, FalloutActorProcessRuntimeSnapshot runtime)
    {
        if (runtime.StandaloneMain is not { } main) return;
        var saved = main.Interface;
        FalloutStandaloneInterfaceState.RequirePlayable(saved);
        var source = records.OwnedSource ?? throw new InvalidDataException("Cold interface has no actual selected source.");
        if (saved.Source.Main != main.Source || saved.Stack != source.StackId || saved.CapturedProcess != runtime.CapturedProcess)
            throw new InvalidDataException("Cold interface changed its actual Main/stack/process.");
        var resources = saved.Objects.Select(item => (Resource: item.Native.Resource, ResourceSha256: item.Native.ResourceSha256))
            .Concat(saved.Menus.Dialogs.SelectMany(item => item.Declaration.Resources)
                .Select(item => (Resource: item.Path, ResourceSha256: item.Sha256))).Distinct();
        foreach (var resource in resources)
        {
            if (!source.TryRead(resource.Resource, null, out var bytes, out _) ||
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != resource.ResourceSha256)
                throw new InvalidDataException("Cold interface object no longer belongs to its exact winning XML declaration.");
        }
    }
}
