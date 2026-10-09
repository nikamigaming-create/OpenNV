using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcessSource
{
    private readonly Dictionary<FalloutFormKey, FalloutCellNativeSource> _nativeCellSources = new(FalloutFormKeyComparer.Instance);
    internal FalloutCellNativeSource ReadNativeCellSource(FalloutFormKey cell)
    {
        if (_nativeCellSources.TryGetValue(cell, out var cached)) return cached;
        var identity = ReadIdentity(cell);
        var definition = _scenes.TryGetValue(cell, out var decoded) ? decoded.Cell :
            FalloutCellSceneReader.ReadDefinition(records, cell);
        var landscapes = records.EffectiveCellChildren(cell, new HashSet<string> { "LAND" }).ToArray();
        var scope = identity.Worldspace is not null && definition.Coordinates is not null &&
            (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0 && (identity.Flags & 0x400) == 0;
        if (!scope)
        {
            if (landscapes.Length != 0)
                throw new NotSupportedException("An interior/persistent CELL declares LAND outside the admitted native exterior terrain owner.");
            var absent = new FalloutCellNativeSource(identity, null, null, null);
            _nativeCellSources.Add(cell, absent); return absent;
        }
        if (landscapes.Length != 1 || landscapes[0].IsDeleted || landscapes[0].Signature != "LAND" ||
            FalloutCellSceneReader.ParentCell(landscapes[0]) != cell ||
            FalloutCellSceneReader.ParentWorldspace(landscapes[0]) != identity.Worldspace)
            throw new NotSupportedException("Actual exterior CELL has no unique winning source LAND consumer; default/ambiguous terrain remains unowned.");
        var source = new FalloutCellNativeSource(identity, landscapes[0].FormKey,
            Convert.ToHexString(SHA256.HashData(landscapes[0].ReadData())).ToLowerInvariant(),
            FalloutCellNativeSource.TransportDigest(FalloutLandscapeTransportResolver.ResolveCell(records, definition)));
        _nativeCellSources.Add(cell, source); return source;
    }
}
