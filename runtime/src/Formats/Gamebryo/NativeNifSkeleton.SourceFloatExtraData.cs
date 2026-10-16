namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNativeNifSkeleton
{
    internal Action<float> BindSourceFloatExtraData(FalloutNifFile source, FalloutNifControllerLink link) =>
        _logicalScene is { } scene ? scene.BindSkeletonFloatExtraDataAnimation(source, link) : FloatExtraData.Bind(source, link);
    internal float ReadSourceFloatExtraData(string node, string name) =>
        _logicalScene is { } scene ? scene.ReadSkeletonFloatExtraData(node, name) : FloatExtraData.Get(node, name);
    internal IEnumerable<(string Node, string Name, float Value)> SourceFloatExtraDataValues =>
        _logicalScene is { } scene ? scene.ReadSkeletonFloatExtraDataValues() : FloatExtraData.Values;
    internal void BorrowRestoredSourceFloatExtraData(string node, string name, float value)
    {
        if (_logicalScene is { } scene) scene.BorrowRestoredSkeletonFloatExtraData(node, name, value);
        else FloatExtraData.Restore(node, name, value);
    }
}
