using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseWorldAxesContracts()
    {
        var parent = new Node3D { Transform = new(new Basis(Vector3.Up, .7f).Scaled(Vector3.One * 2), new(4, 5, 6)) };
        var source = new Node3D { Transform = new(new Basis(Vector3.Right, .4f).Scaled(Vector3.One * 3), new(1, 2, 3)) };
        var second = new Node3D { Transform = new(new Basis(Vector3.Back, -.2f), new(-2, 4, 8)) };
        try
        {
            parent.AddChild(source); parent.AddChild(second);
            var authored = source.Transform;
            var axes = NativeNifWorldAxes.Attach(source, 1);
            var secondAxes = NativeNifWorldAxes.Attach(second, 1);
            var child = new Node3D { Position = new(2, 3, 4) }; axes.AddChild(child);
            var nested = new Node3D { Transform = new(new Basis(Vector3.Back, .6f), new(3, 1, 2)) };
            axes.AddChild(nested);
            var nestedAxes = NativeNifWorldAxes.Attach(nested, 1);
            var leaf = new Node3D { Position = Vector3.Right }; nestedAxes.AddChild(leaf);
            Check();
            var independent = NativeNifWorldAxes.World(second);
            source.Transform = new(new Basis(Vector3.Back, .9f).Scaled(Vector3.One * 3), new(8, 9, 10));
            authored = source.Transform;
            _ = NativeNifWorldAxes.Effective(source);
            Check();
            if (!NativeNifWorldAxes.World(second).IsEqualApprox(independent) || ReferenceEquals(axes, secondAxes))
                throw new InvalidDataException("World-axis source instances shared mutable pose state.");
            parent.Transform = new(new Basis(Vector3.Right, -.6f).Scaled(Vector3.One * -2), new(9, 1, 7));
            Check();
            parent.Position = new(1_000_000, -2_000_000, 3_000_000);
            Check();
            if (NativeNifWorldAxes.Attach(child, 0) != child)
                throw new InvalidDataException("Unflagged source node acquired world-axis behavior.");
            var rejected = false;
            try { _ = NativeNifWorldAxes.Validate(2); } catch (NotSupportedException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Unqualified player-adjust flag was admitted.");
            GD.Print("OPENNV_WORLD_AXES_CONTRACT_PASS authoredLocal=true worldRotation=true translation=true largeWorldExactOrigin=true signedScale=true childPose=true embeddedRelative=true instanceIsolation=true flag2Rejected=true");

            void Check()
            {
                var ordinary = parent.Transform * authored;
                var expected = new Transform3D(Basis.Identity.Scaled(ordinary.Basis.Scale), ordinary.Origin);
                var ordinaryNested = expected * nested.Transform;
                var expectedNested = new Transform3D(Basis.Identity.Scaled(ordinaryNested.Basis.Scale), ordinaryNested.Origin);
                _ = NativeNifWorldAxes.World(leaf);
                if (axes.Transform.Origin != Vector3.Zero || nestedAxes.Transform.Origin != Vector3.Zero ||
                    NativeNifWorldAxes.World(source).Origin != ordinary.Origin)
                    throw new InvalidDataException("Derived world-axis correction changed the source position, including at large world coordinates.");
                if (!NativeNifWorldAxes.World(leaf).IsEqualApprox(expectedNested * leaf.Transform) ||
                    !NativeNifWorldAxes.World(source).IsEqualApprox(expected) || source.Transform != authored ||
                    !NativeNifWorldAxes.World(child).IsEqualApprox(expected * child.Transform) ||
                    !RuntimeNifEmbeddedSkin.Relative(parent, source).IsEqualApprox(new Transform3D(
                        parent.Basis.Inverse() * expected.Basis, authored.Origin)) ||
                    !RuntimeNifEmbeddedSkin.Relative(source, child).IsEqualApprox(child.Transform))
                    throw new InvalidDataException("World-axis publication changed source local pose, position, scale or a child/skin consumer.");
            }
        }
        finally { parent.Free(); }
    }
}
