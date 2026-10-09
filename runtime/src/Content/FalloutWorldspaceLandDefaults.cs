using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWorldspaceLandSelection(
    FalloutFormKey Form, string Winner, uint RawFormId, uint RecordFlags,
    string[] Masters, string Sha256, FalloutFormKey? Parent, ushort ParentFlags);

/// <summary>
/// The winning WRLD DNAM selected by the declared Use Land Data inheritance.
/// A map/LOD/water parent does not select another world's landscape defaults.
/// </summary>
internal sealed record FalloutWorldspaceLandDefaults(
    FalloutFormKey RequestedWorldspace, FalloutFormKey OwnerWorldspace,
    float LandHeight, float WaterHeight,
    IReadOnlyList<FalloutWorldspaceLandSelection> Selection)
{
    internal static FalloutWorldspaceLandDefaults Read(FalloutPluginStack records, FalloutFormKey world)
    {
        var selected = new List<FalloutWorldspaceLandSelection>();
        var visited = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var current = world;
        while (visited.Add(current))
        {
            var record = records.GetEffective(current);
            if (record.Signature != "WRLD")
                throw new InvalidDataException($"Landscape defaults owner {current} is not WRLD.");
            var fields = record.ReadSubrecords().ToArray();
            var parents = fields.Where(field => field.Signature == "WNAM").ToArray();
            var flags = fields.Where(field => field.Signature == "PNAM").ToArray();
            if (parents.Length > 1 || parents.Any(field => field.Data.Length != sizeof(uint)))
                throw new InvalidDataException($"WRLD {current} WNAM requires at most one exact FormID.");
            if (flags.Length > 1 || flags.Any(field => field.Data.Length != sizeof(ushort)))
                throw new InvalidDataException($"WRLD {current} PNAM requires at most one uint16 field.");
            var parent = parents.Length == 0 ? null : record.Plugin.AdjustOptionalFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(parents[0].Data.Span));
            var parentFlags = flags.Length == 0 ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(flags[0].Data.Span);
            selected.Add(new(current, record.Plugin.Name, record.RawFormId, record.Flags,
                record.Plugin.Masters.ToArray(), Convert.ToHexString(SHA256.HashData(record.ReadData())), parent, parentFlags));
            if (parent is { } declared)
            {
                if (!records.TryGetEffective(declared, out var owner) || owner.Signature != "WRLD")
                    throw new InvalidDataException($"WRLD {current} parent is absent, deleted or not WRLD: {declared}.");
            }
            var defaults = fields.Where(field => field.Signature == "DNAM").ToArray();
            if (defaults.Length > 1 || defaults.Any(field => field.Data.Length != 2 * sizeof(float)))
                throw new InvalidDataException($"WRLD {current} DNAM requires at most one exact land/water height pair.");
            float? land = defaults.Length == 0 ? null : BinaryPrimitives.ReadSingleLittleEndian(defaults[0].Data.Span);
            float? water = defaults.Length == 0 ? null : BinaryPrimitives.ReadSingleLittleEndian(defaults[0].Data.Span[sizeof(float)..]);
            if (land is { } authoredLand && !float.IsFinite(authoredLand) || water is { } authoredWater && !float.IsFinite(authoredWater))
                throw new InvalidDataException($"WRLD {current} DNAM contains a non-finite height.");
            if ((parentFlags & 1) != 0)
            {
                current = parent ?? throw new InvalidDataException($"WRLD {current} selects parent land data without a parent world.");
                continue;
            }
            if (land is not { } selectedLand || water is not { } selectedWater)
                throw new InvalidDataException($"WRLD {current} DNAM requires one exact land/water height pair.");
            return new(world, current, selectedLand, selectedWater, selected.ToArray());
        }
        throw new InvalidDataException($"WRLD {world} land-data inheritance contains a cycle.");
    }
}
