using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private FalloutNpcAppearancePart? ReadModifiedModel(FalloutPluginRecord weapon, FalloutNpcAppearancePart? baseline,
            byte mask, bool firstPerson)
        {
            if (mask == 0) return baseline;
            var fields = weapon.ReadSubrecords().ToArray();
            var model = firstPerson ? fields.SingleOrDefault(field => field.Signature == "WNM" + mask).Data : default;
            if (!model.IsEmpty)
            {
                if (model.Length != 4) throw new InvalidDataException("Modified first-person model FormID extent is invalid.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(model.Span);
                if (raw != 0)
                    return FalloutNpcAppearanceResolver.ReadModel(records, records.GetEffective(weapon.Plugin.AdjustFormId(raw)),
                        "weapon", "MODL", "MODS", "MODD", 0, null);
            }
            else if (!firstPerson && fields.SingleOrDefault(field => field.Signature == "MWD" + mask).Data is { IsEmpty: false } path)
            {
                var value = FalloutDialogueTopic.Text(path.Span).Replace('\\', '/');
                if (!value.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) value = "meshes/" + value;
                if (baseline is not null) return baseline with { ModelPath = value, AlternateTextures = [] };
            }
            return baseline;
        }
    }
}
