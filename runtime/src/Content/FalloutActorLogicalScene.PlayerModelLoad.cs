using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerLoadedRootFlag(FalloutReferenceModelDataInvocation Load, Guid Factory,
    Guid Lifetime, long Changed, uint Before, uint After);
internal sealed record FalloutPlayerLoadedShaderSelection(Guid ModelFactory, Guid LogicalLifetime,
    FalloutNifSceneKey? Named, FalloutNifSceneKey SelectedRoot, IReadOnlyList<FalloutNifSceneKey> Visited,
    FalloutSourceSharedShaderIdentity? Shader, FalloutPlayerModelNamedRead NamedRead);
internal sealed partial class FalloutActorLogicalScene
{
    internal FalloutPlayerLoadedRootFlag StorePlayerFirstModelFlag(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load); var before = Root.Flags;
        Root.Flags |= FalloutPlayerModelLoadContinuationSource.FirstRootFlag;
        var result = new FalloutPlayerLoadedRootFlag(load, Factory, Lifetime, Next(), before, Root.Flags);
        requireActual(load); return result;
    }
    internal void StoreInitialPlayerIdentityRotation(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load);
        // The selected source child copies the supplied original identity
        // matrix; it does not read reference Euler or native transforms.
        PublishLocalPose(Root.Key, Root.Local with { RotationRowMajor = [1, 0, 0, 0, 1, 0, 0, 0, 1] });
        requireActual(load);
    }
    internal FalloutPlayerLoadedRootFlag StorePlayerModelActiveFlag(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load); var before = Root.Flags;
        Root.Flags |= FalloutPlayerModelLoadContinuationSource.ActiveRootFlag;
        var result = new FalloutPlayerLoadedRootFlag(load, Factory, Lifetime, Next(), before, Root.Flags);
        requireActual(load); return result;
    }
    internal FalloutNifSceneObject? ReadPlayerModelNamedObject(string name,
        FalloutReferenceModelDataInvocation load, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // This is the same current source graph as the returned factory. The
        // query preserves source child-slot order and includes the receiver.
        var stack = new Stack<FalloutNifSceneObject>(); stack.Push(Root);
        while (stack.TryPop(out var node))
        {
            if (node.Name.Equals(name, StringComparison.Ordinal)) { requireActual(load); return node; }
            for (var index = node.Children.Count - 1; index >= 0; --index)
                if (node.Children[index] is { } child) stack.Push(child);
        }
        requireActual(load); return null;
    }
    internal (FalloutPlayerLoadedShaderSelection Read, FalloutSourceSharedShaderProperty? Property)
        SelectPlayerFirstShader(FalloutPlayerModelLoadContinuationSource source,
            FalloutSourceAnimationContextState context, FalloutReferenceModelDataInvocation load,
            Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); RequireIdle(); requireActual(load);
        var query = context.ReadModelLoadNamedObject(source.SkinAttachment, this, load, requireActual);
        return SelectPlayerFirstShaderProperty(source, query.Read, query.Object, load, requireActual);
    }
    internal (FalloutPlayerLoadedShaderSelection Read, FalloutSourceSharedShaderProperty? Property)
        SelectPlayerFirstShaderProperty(FalloutPlayerModelLoadContinuationSource source, FalloutPlayerModelNamedRead query,
            FalloutNifSceneObject? namedObject, FalloutReferenceModelDataInvocation load,
            Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); RequireIdle(); requireActual(load);
        if (query.Name != source.SkinAttachment || query.ModelFactory != Factory || query.LogicalLifetime != Lifetime ||
            query.Returned != namedObject?.Key || namedObject is not null && !_objects.Values.Any(value => ReferenceEquals(value, namedObject)))
            throw new InvalidDataException("Shared-shader helper replaced its actual palette/named-root query.");
        var visited = new List<FalloutNifSceneKey>();
        var selected = namedObject;
        var named = selected?.Key; FalloutNifSceneProperty? shader = null;
        if (selected is null)
        {
            foreach (var child in Root.Children)
            {
                if (child is not { IsNode: true } || string.IsNullOrEmpty(child.Name) ||
                    child.Name.StartsWith(source.BipedPrefix, StringComparison.OrdinalIgnoreCase) ||
                    child.Children.Count == 0 || child.Bound is not { } bound || !(bound.Sphere.Radius > 0)) continue;
                shader = Select(child);
                if (shader is not null) { selected = child; break; }
            }
            selected ??= Root;
        }
        shader ??= Select(selected); requireActual(load);
        var property = shader is null ? null : FalloutSourceSharedShaderProperty.From(this, shader);
        return (new(Factory, Lifetime, named, selected.Key, visited.ToArray(), property?.Source, query), property);
        FalloutNifSceneProperty? Select(FalloutNifSceneObject node)
        {
            // Source first probes the first child of each nonempty node from
            // the tail, then performs a forward depth-first fallback.
            for (var index = node.Children.Count - 1; index >= 0; --index)
                if (node.Children[index] is { IsNode: true } child && child.Children.Count > 0 && child.Children[0] is { } first)
                { visited.Add(first.Key); if (first.Shader is { ShaderKind: >= 7 and <= 11 } found) return found; }
            return Recurse(node);
        }
        FalloutNifSceneProperty? Recurse(FalloutNifSceneObject node)
        {
            foreach (var child in node.Children)
            {
                if (child is null) continue; visited.Add(child.Key);
                if (child.IsNode)
                { if (Recurse(child) is { } found) return found; }
                else if (child.IsGeometry && child.Shader is { ShaderKind: >= 7 and <= 11 } shader) return shader;
            }
            return null;
        }
    }
}
