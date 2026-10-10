using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceSceneNode(Guid Process, Guid Factory, ulong NativeRoot);
internal sealed record FalloutSourceSceneReference(FalloutFormKey Reference, byte? BaseKind);

// Every node/reference read belongs to a living source factory. An arbitrary
// Godot parent or collision hit is not this source's graph/reference mapping.
internal interface IFalloutSourceSceneReferenceGraph
{
    FalloutStandalonePlayerSceneSource Source { get; }
    Guid Process { get; }
    void RequireCurrent();
    FalloutSourceSceneNode? ReadForcedFirstPerson();
    FalloutFormKey ReadPlayerReference();
    FalloutSourceSceneReference? ReadNodeReference(FalloutSourceSceneNode node);
    FalloutSourceSceneNode? ReadParent(FalloutSourceSceneNode node);
}

internal static class FalloutSourceSceneReferenceQuery
{
    internal const byte ParentPreferredBaseKind = 0x29;

    internal static FalloutFormKey? Read(IFalloutSourceSceneReferenceGraph graph, FalloutSourceSceneNode? input)
    {
        ArgumentNullException.ThrowIfNull(graph); graph.Source.Validate();
        if (graph.Process == Guid.Empty) throw new InvalidDataException("Source scene query has no actual Player process.");
        var source = graph.Source; var process = graph.Process;
        var visiting = new HashSet<FalloutSourceSceneNode>();
        return ReadBranch(input);

        FalloutFormKey? ReadBranch(FalloutSourceSceneNode? cursor)
        {
            Require();
            // The source calls the explicit true-argument getter on EVERY
            // invocation, including null input and recursive parent fallback.
            var first = graph.ReadForcedFirstPerson(); Require(); ValidateNode(first);
            var entered = new List<FalloutSourceSceneNode>();
            try
            {
                while (cursor is not null)
                {
                    ValidateNode(cursor);
                    if (!visiting.Add(cursor)) throw new InvalidDataException("Source scene parent graph contains a nonreturning ownership cycle.");
                    entered.Add(cursor);
                    if (cursor == first)
                    {
                        var player = graph.ReadPlayerReference(); Require(); ValidateReference(player); return player;
                    }
                    var owner = graph.ReadNodeReference(cursor); Require();
                    if (owner is not null)
                    {
                        ValidateReference(owner.Reference);
                        if (owner.BaseKind != ParentPreferredBaseKind) return owner.Reference;
                        var parent = graph.ReadParent(cursor); Require(); ValidateNode(parent);
                        return ReadBranch(parent) ?? owner.Reference;
                    }
                    cursor = graph.ReadParent(cursor); Require();
                }
                return null;
            }
            finally { foreach (var node in entered) visiting.Remove(node); }
        }
        void Require()
        {
            graph.RequireCurrent();
            if (graph.Source != source || graph.Process != process)
                throw new InvalidDataException("Scene query changed its selected source or current process during a source child.");
        }
        void ValidateNode(FalloutSourceSceneNode? node)
        {
            if (node is not null && (node.Process != process || node.Factory == Guid.Empty || node.NativeRoot == 0))
                throw new InvalidDataException("Source scene graph omitted its actual factory/root/current process.");
        }
        static void ValidateReference(FalloutFormKey reference)
        {
            if (reference.ObjectId == 0 || string.IsNullOrWhiteSpace(reference.OwnerPlugin))
                throw new InvalidDataException("Source scene query returned an unowned reference identity.");
        }
    }
}
