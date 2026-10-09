using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal enum FalloutRestLocationOwner { InteriorCell, ExteriorWorldspace, ExteriorWithoutWorldspace }

// This is a declaration of the actual attached location. It does not select a
// starting CELL, infer an exterior parent, or turn an unread flag into zero.
internal sealed record FalloutRestLocation(FalloutFormKey Cell, string CellSourceSha256,
    FalloutRestLocationOwner Owner, FalloutFormKey? Worldspace, string? WorldspaceSourceSha256,
    bool ProhibitsRest)
{
    internal static FalloutRestLocation Read(FalloutPluginStack records, FalloutFormKey currentCell)
    {
        var cell = records.GetEffective(currentCell);
        if (cell.Signature != "CELL" || cell.IsDeleted)
            throw new InvalidDataException("Rest location is not the actual winning living CELL.");
        var fields = cell.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (fields.Length != 1 || fields[0].Data.IsEmpty)
            throw new InvalidDataException("Rest CELL has no unambiguous encoded DATA flag owner.");
        var cellHash = SourceIdentity(cell);
        if ((fields[0].Data.Span[0] & FalloutSleepWaitSource.InteriorCellDataFlag) != 0)
            return new(currentCell, cellHash, FalloutRestLocationOwner.InteriorCell, null, null,
                (cell.Flags & FalloutSleepWaitSource.NoRestRecordFlag) != 0);
        if (FalloutCellSceneReader.ParentWorldspace(cell) is not { } worldKey)
            return new(currentCell, cellHash, FalloutRestLocationOwner.ExteriorWithoutWorldspace, null, null, false);
        var world = records.GetEffective(worldKey);
        if (world.Signature != "WRLD" || world.IsDeleted)
            throw new InvalidDataException("Rest CELL's attached parent has no winning living WRLD owner.");
        return new(currentCell, cellHash, FalloutRestLocationOwner.ExteriorWorldspace, worldKey,
            SourceIdentity(world), (world.Flags & FalloutSleepWaitSource.NoRestRecordFlag) != 0);
    }

    internal FalloutRestObservation Observe() => new(ProhibitsRest ? FalloutRestFactState.Denied :
        FalloutRestFactState.Satisfied, $"winning-rest-location:{Cell}:{CellSourceSha256}:{Owner}:{Worldspace}:{WorldspaceSourceSha256}",
        ProhibitsRest ? "The attached source CELL/WRLD prohibits rest." : null);

    internal void RequireCurrent(FalloutPluginStack records, FalloutFormKey currentCell)
    {
        if (this != Read(records, currentCell))
            throw new InvalidDataException("Rest location changed since its actual admission observation.");
    }

    // Record payload alone omits the header flag consumed above and the master
    // adjustment context used by parent/owner links. Retain both independently.
    internal static string SourceIdentity(FalloutPluginRecord record)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("opennv-rest-record-source/v1"));
        hash.AppendData(Encoding.UTF8.GetBytes(record.Signature));
        Span<byte> header = stackalloc byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(header, record.RawFormId);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], record.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], record.FormVersion);
        hash.AppendData(header);
        Span<byte> type = stackalloc byte[4];
        foreach (var group in record.Groups)
        {
            BinaryPrimitives.WriteInt32LittleEndian(type, group.Type);
            hash.AppendData(type); hash.AppendData(group.Label);
        }
        foreach (var name in record.Plugin.Masters.Append(record.Plugin.Name))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
            hash.AppendData([0]);
        }
        hash.AppendData(record.ReadData());
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
