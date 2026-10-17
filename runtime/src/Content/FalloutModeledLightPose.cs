using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutModeledLightPose
{
    private readonly FalloutModeledLightSource _source;
    private readonly Dictionary<int, FalloutNifTransform> _locals = [];
    private readonly List<int> _path = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal FalloutModeledLightPose(FalloutModeledLightSource source)
    {
        _source = source;
        if (!Visit(source.Model.Roots[0], new HashSet<int>()))
            throw new InvalidDataException("Modeled light lost its selected point's actual source parent chain.");
        bool Visit(int block, HashSet<int> entered)
        {
            if (!entered.Add(block)) throw new InvalidDataException("Modeled light source parent chain is cyclic.");
            var value = source.Model.ReadObject(block);
            var transform = value switch
            {
                FalloutNifNode node => node.Transform,
                FalloutNifPointLight point => point.Light.Transform,
                _ => null
            };
            if (transform is null) { entered.Remove(block); return false; }
            _path.Add(block); _locals.Add(block, FalloutNifSceneMath.Copy(transform));
            if (block == source.Point.Block.Index) return true;
            if (value is FalloutNifNode parent)
                foreach (var child in parent.Children.Where(child => child >= 0)) if (Visit(child, entered)) return true;
            _path.RemoveAt(_path.Count - 1); _locals.Remove(block); entered.Remove(block); return false;
        }
    }
    internal void StoreLocal(int block, FalloutNifTransform value)
    {
        RequireThread(); FalloutNifSceneMath.Validate(value);
        if (!_locals.ContainsKey(block)) return;
        _locals[block] = FalloutNifSceneMath.Copy(value);
    }
    internal FalloutNifTransform ReadPoint()
    {
        RequireThread(); var result = FalloutNifSceneMath.Identity;
        foreach (var block in _path)
            if (!_source.ReplaceNodeRootTransform || block != _path[0]) result = FalloutNifSceneMath.Compose(result, _locals[block]);
        return result;
    }
    private void RequireThread()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Modeled source light pose crossed its calling thread.");
    }
    internal static float[] ReferenceRotation(IReadOnlyList<float> angles)
    {
        if (angles.Count != 3 || angles.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Source reference rotation needs three finite angles.");
        // The placed-reference convention is clockwise Rx * Ry * Rz.
        var x = angles[0]; var y = angles[1]; var z = angles[2];
        var cx = MathF.Cos(x); var sx = MathF.Sin(x); var cy = MathF.Cos(y); var sy = MathF.Sin(y);
        var cz = MathF.Cos(z); var sz = MathF.Sin(z);
        return [cy * cz, cy * sz, -sy, sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy,
            cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy];
    }
}
