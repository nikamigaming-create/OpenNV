namespace OpenNV.Runtime.Content;

internal sealed class FalloutFormKeyComparer : IEqualityComparer<FalloutFormKey>
{
    internal static FalloutFormKeyComparer Instance { get; } = new();

    public bool Equals(FalloutFormKey left, FalloutFormKey right) =>
        left.ObjectId == right.ObjectId &&
        left.OwnerPlugin.Equals(right.OwnerPlugin, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(FalloutFormKey value) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.OwnerPlugin), value.ObjectId);
}
