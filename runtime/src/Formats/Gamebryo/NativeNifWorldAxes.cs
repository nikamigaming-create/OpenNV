using Godot;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Formats.Gamebryo;

// BSValueNode flag 1 changes derived world rotation, not authored local pose.
// Source children consume this node after ordinary parent/local composition.
internal sealed partial class NativeNifWorldAxes : Node3D
{
    private static readonly ConditionalWeakTable<Node3D, NativeNifWorldAxes> Owners = new();
    private bool _publishing;
    private Transform3D? _ordinary;

    internal static bool Validate(byte flags)
    {
        if ((flags & ~1) != 0)
            throw new NotSupportedException($"BSValueNode flags {flags} have unqualified player-adjust ownership.");
        return (flags & 1) != 0;
    }

    internal static Node3D Attach(Node3D source, byte flags)
    {
        if (!Validate(flags)) return source;
        var owner = new NativeNifWorldAxes { Name = "SourceWorldAxes", ProcessPriority = 1 };
        Owners.Add(source, owner);
        source.AddChild(owner);
        owner.Publish();
        return owner;
    }

    internal static Node3D Effective(Node3D source)
    {
        RefreshAncestors(source);
        if (!Owners.TryGetValue(source, out var owner)) return source;
        owner.Publish();
        return owner;
    }

    internal static void RefreshAncestors(Node source)
    {
        for (var parent = source.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is NativeNifWorldAxes owner) { owner.Publish(); break; }
    }

    // Works before scene admission as well as while live. Parent owners publish
    // first, so nested source world-axis nodes retain native update order.
    private static Transform3D Composed(Node3D source)
    {
        var result = Transform3D.Identity;
        for (Node? cursor = source; cursor is not null; cursor = cursor.GetParent())
            if (cursor is Node3D spatial)
            {
                result = spatial.Transform * result;
                if (spatial.TopLevel) break;
            }
        return result;
    }

    internal static Transform3D World(Node3D source) => Composed(Effective(source));

    internal void Publish()
    {
        if (_publishing) throw new InvalidOperationException("Source world-axis publication reentered its hierarchy.");
        var source = GetParent() as Node3D ?? throw new InvalidOperationException("World-axis node has no source pose owner.");
        _publishing = true;
        try
        {
            RefreshAncestors(source);
            var ordinary = Composed(source);
            if (_ordinary == ordinary) return;
            var lengths = new Vector3(ordinary.Basis.X.Length(), ordinary.Basis.Y.Length(), ordinary.Basis.Z.Length());
            if (!lengths.IsEqualApprox(Vector3.One * lengths.X) ||
                ordinary.Basis.Determinant() == 0 && ordinary.Basis != new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero))
                throw new NotSupportedException("Source world-axis basis is nonuniform or singular without zero source scale.");
            var scale = ordinary.Basis.Scale;
            if (!ordinary.IsFinite() || !scale.IsEqualApprox(Vector3.One * scale.X))
                throw new NotSupportedException("Source world-axis publication requires a finite uniform NiTransform scale.");
            // Zero source scale collapses the complete basis irrespective of
            // orientation; it needs no inverse of a singular transform.
            // Only rotation changes. Keep the derived local origin exactly zero
            // rather than cancelling large world translations through an inverse.
            var correction = scale.X == 0 ? Transform3D.Identity : new Transform3D(
                ordinary.Basis.Inverse() * Basis.Identity.Scaled(scale), Vector3.Zero);
            Transform = correction;
            _ordinary = ordinary;
        }
        finally { _publishing = false; }
    }

    public override void _EnterTree() => Publish();
    public override void _Process(double delta) => Publish();
}
