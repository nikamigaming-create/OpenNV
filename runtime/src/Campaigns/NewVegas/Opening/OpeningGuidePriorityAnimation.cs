using System.Security.Cryptography;
using System.Text;
using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal static class OpeningGuidePriorityAnimation
{
    private const string RuntimeLibraryName = "opennv_priority";
    private const string FurnitureRuntimeAnimationName = "guide_furniture_seated";
    private const string PackageRuntimeLibraryName = "opennv_priority_package";
    private const string PackageRuntimeAnimationName = "guide_package_seated";
    private const string PackagePlayerName = "OpenNvGuidePackagePriorityPlayer";
    private const string ActorAccumulationRootNode = "Bip01";
    private const string SourceNonAccumulationRootNode = "Bip01 NonAccum";
    private const int OwnedLoopCycleType = 0;
    private const int OwnedClampCycleType = 2;

    internal static LayeredPlayback Compose(
        ActorModelSlice.LoadedAnimation furniture,
        ActorModelSlice.LoadedAnimation packageIdle,
        string requiredPackageAttachmentNode)
    {
        if (furniture.Player != packageIdle.Player ||
            furniture.StartSeconds != 0.0f || packageIdle.StartSeconds != 0.0f ||
            furniture.StopSeconds <= packageIdle.StartSeconds ||
            packageIdle.StopSeconds <= furniture.StartSeconds ||
            furniture.TransformPrioritiesByNode.Count == 0 ||
            packageIdle.TransformPrioritiesByNode.Count == 0)
            throw new InvalidOperationException(
                "Owned guide priority animation sources are incomplete.");
        if (!packageIdle.TransformPrioritiesByNode.TryGetValue(
                requiredPackageAttachmentNode,
                out var attachmentIdlePriority) ||
            !furniture.TransformPrioritiesByNode.TryGetValue(
                requiredPackageAttachmentNode,
                out var attachmentFurniturePriority) ||
            attachmentIdlePriority <= attachmentFurniturePriority)
            throw new InvalidOperationException(
                "Owned package idle does not win its animation-object attachment node.");
        var player = furniture.Player;
        var furnitureResource = player.GetAnimation(furniture.RuntimeName)
            ?? throw new InvalidOperationException(
                "Owned furniture animation resource is absent.");
        var packageResource = player.GetAnimation(packageIdle.RuntimeName)
            ?? throw new InvalidOperationException(
                "Owned package-idle animation resource is absent.");
        if (!Mathf.IsEqualApprox(
                (float)furnitureResource.Length,
                furniture.StopSeconds) ||
            !Mathf.IsEqualApprox(
                (float)packageResource.Length,
                packageIdle.StopSeconds))
            throw new InvalidOperationException(
                "Owned guide animation runtime/source durations differ.");

        var sources = new[]
        {
            new TrackSource(furniture, furnitureResource, false),
            new TrackSource(packageIdle, packageResource, true),
        };
        var tracks = sources.SelectMany(source => Enumerable.Range(
                0,
                source.Resource.GetTrackCount()).Select(index =>
                    Track(source, index)))
            .GroupBy(value => new TrackIdentity(
                value.Path.ToString(),
                value.Type),
                TrackIdentityComparer.Instance)
            .Select(group => group.OrderByDescending(value => value.Priority)
                .ThenBy(value => value.Source.PackageIdle ? 0 : 1)
                .First())
            .OrderBy(value => value.Path.ToString(), StringComparer.Ordinal)
            .ThenBy(value => value.Type)
            .ToArray();
        var packageTracks = tracks.Count(value => value.Source.PackageIdle);
        var furnitureTracks = tracks.Length - packageTracks;
        if (packageTracks == 0 || furnitureTracks == 0 ||
            !tracks.Any(value =>
                value.Source.PackageIdle &&
                value.NodeName.Equals(
                    requiredPackageAttachmentNode,
                    StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Owned guide priority animation did not retain both source layers.");
        var furnitureComposite = FilteredAnimation(
            furniture,
            furnitureResource,
            tracks.Where(value => !value.Source.PackageIdle));
        var packageComposite = FilteredAnimation(
            packageIdle,
            packageResource,
            tracks.Where(value => value.Source.PackageIdle));

        var library = new AnimationLibrary();
        library.AddAnimation(FurnitureRuntimeAnimationName, furnitureComposite);
        if (player.HasAnimationLibrary(RuntimeLibraryName))
            player.RemoveAnimationLibrary(RuntimeLibraryName);
        player.AddAnimationLibrary(RuntimeLibraryName, library);
        var furnitureRuntimeName = new StringName(
            $"{RuntimeLibraryName}/{FurnitureRuntimeAnimationName}");
        var packagePlayer = new AnimationPlayer
        {
            Name = PackagePlayerName,
            RootNode = player.RootNode,
        };
        player.GetParent().AddChild(packagePlayer);
        var packageLibrary = new AnimationLibrary();
        packageLibrary.AddAnimation(PackageRuntimeAnimationName, packageComposite);
        packagePlayer.AddAnimationLibrary(PackageRuntimeLibraryName, packageLibrary);
        var packageRuntimeName = new StringName(
            $"{PackageRuntimeLibraryName}/{PackageRuntimeAnimationName}");
        var priorities = tracks
            .GroupBy(value => value.NodeName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(value => value.Priority),
                StringComparer.Ordinal);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                furniture.SourceSha256 + ":" + packageIdle.SourceSha256)))
            .ToLowerInvariant();
        GD.Print(
            "OPENNV_NEW_GAME_GUIDE_PRIORITY_COMPOSITE " +
            $"furniture={furniture.SequenceName} furnitureCycle={furniture.CycleType} " +
            $"furnitureSeconds={furniture.StopSeconds:R} " +
            $"packageIdle={packageIdle.SequenceName} packageCycle={packageIdle.CycleType} " +
            $"packageSeconds={packageIdle.StopSeconds:R} " +
            $"furnitureTracks={furnitureTracks} packageTracks={packageTracks} " +
            $"attachment={requiredPackageAttachmentNode} " +
            $"attachmentPriorities={attachmentFurniturePriority}/{attachmentIdlePriority} " +
            $"furnitureLoop={furnitureComposite.LoopMode} " +
            $"packageLoop={packageComposite.LoopMode} " +
            "clocking=independent-source-cycle-duration-players");
        var activeAnimation = new ActorModelSlice.LoadedAnimation(
            furniture.LogicalPath + "+" + packageIdle.LogicalPath,
            identity,
            furniture.AccumulationRootTranslationDisposition,
            tracks.Length,
            furniture.SequenceName + "+" + packageIdle.SequenceName,
            furniture.StartSeconds,
            furniture.StopSeconds,
            furniture.CycleType,
            priorities,
            furnitureRuntimeName,
            player);
        return new LayeredPlayback(
            activeAnimation,
            furniture,
            player,
            furnitureRuntimeName,
            packagePlayer,
            packageRuntimeName);
    }

    private static Animation FilteredAnimation(
        ActorModelSlice.LoadedAnimation animation,
        Animation source,
        IEnumerable<SelectedTrack> tracks)
    {
        var filtered = new Animation
        {
            Length = source.Length,
            LoopMode = animation.CycleType switch
            {
                OwnedLoopCycleType => Animation.LoopModeEnum.Linear,
                OwnedClampCycleType => Animation.LoopModeEnum.None,
                _ => throw new InvalidOperationException(
                    $"Owned guide animation cycle type is unsupported: " +
                    $"{animation.SequenceName}/{animation.CycleType}"),
            },
        };
        foreach (var track in tracks)
            CopyTrack(track.Source.Resource, track.Index, filtered);
        return filtered;
    }

    private static SelectedTrack Track(TrackSource source, int index)
    {
        var path = source.Resource.TrackGetPath(index);
        var subNames = path.GetSubNameCount();
        var names = path.GetNameCount();
        if (subNames > 1 || subNames == 0 && names == 0)
            throw new InvalidOperationException(
                $"Owned guide animation track has no unique skeleton node: {path}");
        var nodeName = subNames == 1
            ? path.GetSubName(0).ToString()
            : path.GetName(names - 1).ToString();
        if (!source.Animation.TransformPrioritiesByNode.TryGetValue(
                nodeName,
                out var priority) &&
            !(nodeName == ActorAccumulationRootNode &&
              source.Animation.TransformPrioritiesByNode.TryGetValue(
                  SourceNonAccumulationRootNode,
                  out priority)))
            throw new InvalidOperationException(
                $"Owned guide animation track has no source priority: {nodeName}");
        return new SelectedTrack(
            source,
            index,
            path,
            source.Resource.TrackGetType(index),
            nodeName,
            priority);
    }

    private static void CopyTrack(Animation source, int sourceIndex, Animation target)
    {
        var targetIndex = target.AddTrack(source.TrackGetType(sourceIndex));
        target.TrackSetPath(targetIndex, source.TrackGetPath(sourceIndex));
        target.TrackSetEnabled(targetIndex, source.TrackIsEnabled(sourceIndex));
        target.TrackSetInterpolationType(
            targetIndex,
            source.TrackGetInterpolationType(sourceIndex));
        target.TrackSetInterpolationLoopWrap(
            targetIndex,
            source.TrackGetInterpolationLoopWrap(sourceIndex));
        for (var key = 0; key < source.TrackGetKeyCount(sourceIndex); key++)
        {
            target.TrackInsertKey(
                targetIndex,
                source.TrackGetKeyTime(sourceIndex, key),
                source.TrackGetKeyValue(sourceIndex, key),
                source.TrackGetKeyTransition(sourceIndex, key));
        }
    }

    private readonly record struct TrackSource(
        ActorModelSlice.LoadedAnimation Animation,
        Animation Resource,
        bool PackageIdle);

    private readonly record struct SelectedTrack(
        TrackSource Source,
        int Index,
        NodePath Path,
        Animation.TrackType Type,
        string NodeName,
        int Priority);

    private readonly record struct TrackIdentity(string Path, Animation.TrackType Type);

    private sealed class TrackIdentityComparer : IEqualityComparer<TrackIdentity>
    {
        internal static readonly TrackIdentityComparer Instance = new();

        public bool Equals(TrackIdentity x, TrackIdentity y) =>
            x.Type == y.Type && x.Path.Equals(y.Path, StringComparison.Ordinal);

        public int GetHashCode(TrackIdentity obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Path), obj.Type);
    }

    internal sealed class LayeredPlayback(
        ActorModelSlice.LoadedAnimation activeAnimation,
        ActorModelSlice.LoadedAnimation furnitureSource,
        AnimationPlayer furniturePlayer,
        StringName furnitureRuntimeName,
        AnimationPlayer packagePlayer,
        StringName packageRuntimeName)
    {
        internal ActorModelSlice.LoadedAnimation ActiveAnimation { get; } =
            activeAnimation;

        internal double FurniturePositionSeconds =>
            furniturePlayer.CurrentAnimationPosition;

        internal double PackagePositionSeconds =>
            packagePlayer.CurrentAnimationPosition;

        internal void Play()
        {
            furniturePlayer.Play(furnitureRuntimeName);
            packagePlayer.Play(packageRuntimeName);
            furniturePlayer.Advance(0.0);
            packagePlayer.Advance(0.0);
        }

        internal void Stop()
        {
            packagePlayer.Stop();
            furniturePlayer.Stop();
        }

        internal void PoseFurnitureOnlyAtCurrentPhase()
        {
            var position = furniturePlayer.CurrentAnimationPosition;
            packagePlayer.Stop();
            furniturePlayer.Play(furnitureSource.RuntimeName);
            furniturePlayer.Seek(position, update: true);
            furniturePlayer.Advance(0.0);
        }
    }
}
