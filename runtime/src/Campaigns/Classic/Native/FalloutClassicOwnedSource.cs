namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal interface IFalloutClassicOwnedSource : IDisposable
{
    string ProfileId { get; }
    byte[] Read(string logicalPath, out int sourceIndex);
    IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension);
}
