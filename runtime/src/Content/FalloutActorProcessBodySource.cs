using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// The original High initializer searches the actual actor 3D. This pure source
// join is retained only alongside a living native body lease. It selects source
// objects; activation/timing of BoneLOD is a separate presentation owner.
internal static class FalloutActorProcessBodySource
{
    internal static FalloutActorProcessBodyBinding Read(FalloutActorProcessRuntimeDeclaration declaration,
        FalloutFormKey actor, string skeletonPath, FalloutNifFile skeleton, FalloutBodyPartData parts,
        string bodyPartSha256, string owner)
    {
        declaration.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(skeletonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentNullException.ThrowIfNull(skeleton); ArgumentNullException.ThrowIfNull(parts);
        if (parts.Parts is null || parts.Parts.Any(part => part is null || part.Type >= 15) ||
            parts.Parts.Select(part => part.Type).Distinct().Count() != parts.Parts.Count ||
            bodyPartSha256 is not { Length: 64 } || !bodyPartSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("High source initialization lost its exact winning BPTD table.");
        var nodes = new List<FalloutNifObject>(); var seen = new HashSet<int>();
        void Visit(int block)
        {
            if (!seen.Add(block)) throw new InvalidDataException("Actor source 3D hierarchy repeats an owned node.");
            var value = skeleton.ReadObject(block);
            if (value is not (FalloutNifNode or FalloutNifGeometry))
                throw new NotSupportedException("Actor source 3D child has no admitted AVObject lookup owner: " + value.Block.TypeName);
            nodes.Add(value);
            if (value is FalloutNifNode node)
                foreach (var child in node.Children) if (child != -1) Visit(child);
        }
        foreach (var root in skeleton.Roots) Visit(root);
        FalloutNifObject? Find(string? name) => string.IsNullOrEmpty(name) ? null : nodes.FirstOrDefault(value => value switch
        {
            FalloutNifNode node => node.Name == name,
            FalloutNifGeometry geometry => geometry.Name == name,
            _ => false,
        });
        var bindings = parts.Parts.Select(part => new FalloutActorProcessNodeBinding(part.Type, part.Node, Find(part.Node)?.Block.Index)).ToArray();
        var sight = FalloutDetectionSightBindings.Read(parts);
        var rootObject = Find(declaration.HighBoneLodNode);
        int? boneLod = null;
        var controller = rootObject switch
        {
            FalloutNifNode node => node.Controller,
            FalloutNifGeometry geometry => geometry.Controller,
            null => -1,
            _ => throw new InvalidDataException("Actor source root lookup has a foreign object kind."),
        };
        var controllers = new HashSet<int>();
        while (controller != -1)
        {
            if (!controllers.Add(controller)) throw new InvalidDataException("Actor High initializer repeats a source controller.");
            var value = skeleton.ReadObject(controller);
            if (value is FalloutNifBoneLodController)
            {
                boneLod = controller; break;
            }
            controller = FalloutNifNodeControllerChain.Time(value).NextController;
        }
        return new(actor, skeletonPath, skeleton.Sha256, parts.Form, bodyPartSha256, bindings,
            sight.HeadTarget, Find(sight.HeadTarget)?.Block.Index, sight.TorsoTarget, Find(sight.TorsoTarget)?.Block.Index,
            rootObject?.Block.Index, boneLod, owner);
    }
}
