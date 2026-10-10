using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        internal int ShapedPhantomCount { get; private set; }

        private FalloutNifCollisionObject RequireVisualPhantom(FalloutNifBlock owner,
            FalloutNifCollisionObject attachment)
        {
            var source = FalloutNifPhantomContract.Read(_source, attachment);
            if (source.Target != owner.Index)
                throw new InvalidDataException("Visual shaped phantom targets another source node.");
            return attachment;
        }

        private bool AddShapePhantom(Node3D owner, FalloutNifCollisionObject attachment)
        {
            if (_source.ReadObject(attachment.Body) is not FalloutNifSimpleShapePhantom) return false;
            var built = NativeNifCollisionBuilder.BuildPhantom(_source, attachment, _unitsToMetres);
            try { owner.AddChild(built.Body); }
            catch { built.Body.Free(); throw; }
            ShapedPhantomCount++;
            CollisionShapeCount += built.Shapes;
            // Queries do not join _collisionBodies and cannot become hinge
            // pairs, floor collision, rigid-body mass or collision triangles.
            return true;
        }
    }
}
