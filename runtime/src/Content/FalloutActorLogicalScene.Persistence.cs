using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorLogicalScene
{
    internal FalloutActorLogicalSceneSnapshot Capture()
    {
        RequireIdle(allowFailure: true);
        return new(Schema, Factory, _lifetime, _sequence, Array.AsReadOnly(_assets.ToArray()), Root.Key,
            Array.AsReadOnly(_order.Select(node => node.Capture()).ToArray()),
            Array.AsReadOnly(_properties.Values.Select(property => property.Capture()).ToArray()),
            _alpha, _failure, _retired, _restoredLifetime, BoundLane, AttachmentLane, CaptureFloatExtraData(), CaptureVisibility(), CaptureMorphs(), _initialModelUpdate);
    }
    private void Restore(FalloutActorLogicalSceneSnapshot saved)
    {
        Validate(saved);
        if (saved.Factory == Factory || saved.Lifetime == _lifetime || saved.Retired || saved.Root != Root.Key ||
            saved.Assets.Count != _assets.Count || saved.Objects.Count != _objects.Count || saved.Properties.Count != _properties.Count)
            throw new InvalidDataException("Cold logical scene lost a fresh model lifetime or its complete source denominator.");
        for (var index = 0; index < _assets.Count; index++)
        {
            var actual = _assets[index]; var old = saved.Assets[index];
            if (old.Slot != actual.Slot || old.Path != actual.Path || old.Sha256 != actual.Sha256 || !old.Roots.SequenceEqual(actual.Roots))
                throw new InvalidDataException("Cold logical scene changed a selected original resource/root order.");
        }
        for (var index = 0; index < _order.Count; index++)
        {
            var node = _order[index]; var old = saved.Objects[index];
            if (node.Key != old.Key || node.Source.Block.TypeName != old.Type || node.Name != old.Name ||
                node.Parent?.Key != old.Parent || !node.Children.Select(child => (FalloutNifSceneKey?)child?.Key).SequenceEqual(old.Children) ||
                node.Shader?.Key != old.Shader || !node.FloatExtraData.SequenceEqual(old.FloatExtraData))
                throw new InvalidDataException("Cold logical scene changed source class, child slots, property reference or assembly publication.");
            node.Flags = old.Flags; node.Local = old.Local.Read(); node.World = old.World.Read();
            node.Bound = old.Bound is { } bound ? bound with { Identity = Guid.NewGuid() } : null;
            node.PoseStores = old.PoseStores; node.BoundStores = old.BoundStores;
        }
        foreach (var property in saved.Properties) _properties[property.Key].Restore(property);
        RestoreFloatExtraData(saved);
        RestoreVisualFields(saved);
        RestoreInitialModelUpdate(saved);
        _sequence = saved.Sequence; _alpha = saved.AlphaCall; _failure = saved.Failure; _restoredLifetime = saved.Lifetime;
        // Fresh actual field/resource identities, retained state: do not update
        // transforms, bounds, shader alpha, clocks or source constructors here.
        Next();
    }
    internal static void Validate(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Factory == Guid.Empty || saved.Lifetime == Guid.Empty || saved.Sequence < 1 ||
            saved.Assets is null || saved.Assets.Count == 0 || saved.Objects is null || saved.Objects.Count == 0 || saved.Properties is null ||
            saved.BoundLane != BoundLane || saved.AttachmentLane != AttachmentLane ||
            saved.Assets.Any(asset => asset is null || string.IsNullOrWhiteSpace(asset.Path) || !Digest(asset.Sha256) || asset.Roots is null) ||
            !saved.Assets.Select(asset => asset.Slot).SequenceEqual(Enumerable.Range(0, saved.Assets.Count)) ||
            saved.Objects.Any(node => node is null || node.Children is null || node.Local is null || node.World is null ||
                string.IsNullOrWhiteSpace(node.Type) || node.PoseStores < 0 || node.BoundStores < 0 ||
                node.Key.Asset < 0 || node.Key.Asset >= saved.Assets.Count || node.Key.Block < 0 ||
                node.Bound is { Identity: var boundId } && boundId == Guid.Empty ||
                node.BoundStores == 0 && node.Bound is not null) ||
            saved.Properties.Any(property => property is null || string.IsNullOrWhiteSpace(property.Type) ||
                property.Key.Asset < 0 || property.Key.Asset >= saved.Assets.Count || property.Key.Block < 0 ||
                property.ShaderStores < 0 || property.MaterialStores < 0 ||
                (property.ShaderKind is null) != (property.ShaderAlphaBits is null) ||
                property.ShaderKind is null && property.ShaderStores != 0 ||
                property.MaterialAlphaBits is null && property.MaterialStores != 0) ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure) ||
            saved.RestoredLifetime is { } oldLifetime && (oldLifetime == Guid.Empty || oldLifetime == saved.Lifetime))
            throw new InvalidDataException("Cold logical scene lost its source resources, fields or current lifetime.");
        ValidateFloatExtraData(saved);
        ValidateVisualFields(saved);
        ValidateInitialModelUpdate(saved);
        var objects = saved.Objects.ToDictionary(node => node.Key);
        var properties = saved.Properties.ToDictionary(property => property.Key);
        if (objects.Count != saved.Objects.Count || properties.Count != saved.Properties.Count || !objects.ContainsKey(saved.Root) ||
            saved.Objects.Where(node => node.Bound is not null).Select(node => node.Bound!.Identity).Distinct().Count() != saved.Objects.Count(node => node.Bound is not null))
            throw new InvalidDataException("Cold logical scene duplicated an actual object, property or bound owner.");
        var seen = new HashSet<FalloutNifSceneKey>(); var pending = new Stack<FalloutNifSceneKey>(); pending.Push(saved.Root);
        while (pending.TryPop(out var key))
        {
            if (!seen.Add(key) || !objects.TryGetValue(key, out var node)) throw new InvalidDataException("Cold source scene repeats, cycles or references a missing child.");
            _ = node.Local.Read(); _ = node.World.Read();
            if (key == saved.Root && node.Parent is not null || node.Shader is { } shader && !properties.ContainsKey(shader))
                throw new InvalidDataException("Cold source scene lost its root/property-state relationship.");
            for (var index = node.Children.Count - 1; index >= 0; index--)
                if (node.Children[index] is { } child)
                {
                    if (!objects.TryGetValue(child, out var value) || value.Parent != key)
                        throw new InvalidDataException("Cold source scene changed current parent/child reciprocity.");
                    pending.Push(child);
                }
        }
        if (seen.Count != objects.Count) throw new InvalidDataException("Cold logical scene omitted living source objects.");
        if (saved.AlphaCall is { } call)
        {
            if (call.Invocation == Guid.Empty || call.Entered < 1 || call.Changed < call.Entered || call.Changed > saved.Sequence ||
                !Enum.IsDefined(call.Step) || call.Stores is null || call.Current is { } current && !objects.ContainsKey(current) ||
                call.Stores.Any(key => !properties.TryGetValue(key, out var property) || property.ShaderKind is null or -1 ||
                    property.ShaderStores < 1 || property.ShaderAlphaBits != call.AlphaBits) ||
                (call.Step == FalloutNifSceneAlphaStep.Failed) != (call.Failure is not null) ||
                call.Failure is not null && (call.FailedAt is null || call.Failure != saved.Failure) ||
                call.FailedAt is { } failed && (!Enum.IsDefined(failed) || failed == FalloutNifSceneAlphaStep.Failed) ||
                call.Failure is null && call.FailedAt is not null)
                throw new InvalidDataException("Cold scene alpha lost its ordered stores or a retained child failure.");
        }
        static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
    internal static void RequireComplete(FalloutActorLogicalSceneSnapshot saved)
    {
        Validate(saved);
        if (saved.Retired || saved.Failure is not null || saved.AlphaCall is { Step: not FalloutNifSceneAlphaStep.Returned })
            throw new InvalidDataException("A complete save cannot publish an unfinished logical Actor scene.");
    }
}
