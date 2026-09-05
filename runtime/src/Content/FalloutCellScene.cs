using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCellDefinition(
    FalloutFormKey FormKey,
    string EditorId,
    byte Flags,
    (int X, int Y)? Coordinates,
    FalloutFormKey? Worldspace,
    FalloutCellLighting? Lighting);

internal sealed record FalloutCellLighting(
    byte[] AmbientRgb,
    byte[] DirectionalRgb,
    byte[] FogRgb,
    float FogNear,
    float FogFar,
    int DirectionalXDegrees,
    int DirectionalZDegrees,
    float DirectionalFade,
    float FogClipDistance,
    float FogPower);

internal sealed record FalloutBaseObjectDefinition(
    FalloutFormKey FormKey,
    string Signature,
    string EditorId,
    string? ModelPath,
    FalloutLightDefinition? Light);

internal sealed record FalloutLightDefinition(
    int Duration,
    uint RadiusGameUnits,
    byte[] ColorRgb,
    byte ColorAlpha,
    uint Flags,
    float Falloff,
    float FieldOfViewDegrees,
    uint NearClip,
    float Period,
    float Intensity);

internal sealed record FalloutPlacedReference(
    FalloutFormKey FormKey,
    string EditorId,
    FalloutFormKey Cell,
    FalloutFormKey Base,
    uint Flags,
    float[] Position,
    float[] RotationRadians,
    float Scale,
    float? RadiusAdjustmentGameUnits,
    FalloutTeleportDestination? Teleport,
    FalloutFormKey? EnableParent,
    bool EnableParentOpposite,
    FalloutFormKey? Emittance = null);

internal sealed record FalloutTeleportDestination(
    FalloutFormKey Door,
    float[] Position,
    float[] RotationRadians,
    uint Flags);

internal sealed record FalloutCellScene(
    FalloutCellDefinition Cell,
    IReadOnlyList<FalloutPlacedReference> References,
    IReadOnlyDictionary<FalloutFormKey, FalloutBaseObjectDefinition> BaseObjects);

internal static class FalloutCellSceneReader
{
    private const int CellChildrenGroupType = 6;
    private const int WorldChildrenGroupType = 1;
    private const int ReferenceTransformBytes = sizeof(float) * 6;
    private const int TeleportDestinationBytes = sizeof(uint) * 2 + sizeof(float) * 6;
    private const int EnableParentBytes = sizeof(uint) * 2;
    private const int LightDataBytes = 32;
    private const int LightDurationOffset = 0;
    private const int LightRadiusOffset = 4;
    private const int LightColorOffset = 8;
    private const int LightFlagsOffset = 12;
    private const int LightFalloffOffset = 16;
    private const int LightFieldOfViewOffset = 20;
    private const int LightNearClipOffset = 24;
    private const int LightPeriodOffset = 28;
    private const int LightingDataBytes = 40;
    private const int AmbientColorStart = 0;
    private const int AmbientColorEnd = 3;
    private const int DirectionalColorStart = 4;
    private const int DirectionalColorEnd = 7;
    private const int FogColorStart = 8;
    private const int FogColorEnd = 11;
    private const int FogNearOffset = 12;
    private const int FogFarOffset = 16;
    private const int DirectionalXOffset = 20;
    private const int DirectionalZOffset = 24;
    private const int DirectionalFadeOffset = 28;
    private const int FogClipDistanceOffset = 32;
    private const int FogPowerOffset = 36;
    private const string DataMeshesPrefix = "data\\meshes\\";
    private const string MeshesPrefix = "meshes\\";
    private const uint InitiallyDisabledFlag = 0x0000_0800;
    private const uint KnownLightingInheritanceFlags = 0x0000_01ff;
    private static readonly HashSet<string> ReferenceTypes =
        ["REFR", "ACHR", "ACRE", "PGRE", "PMIS"];

    internal static FalloutCellScene Read(FalloutPluginStack stack, FalloutFormKey cellKey)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var cellRecord = stack.GetEffective(cellKey);
        if (cellRecord.Signature != "CELL")
            throw new InvalidDataException($"Requested native scene {cellKey} is {cellRecord.Signature}, not CELL.");
        var cellValues = Values(cellRecord);
        var data = OptionalSingle(cellValues, "DATA", cellRecord);
        var coordinates = OptionalSingle(cellValues, "XCLC", cellRecord);
        if (coordinates is not null && coordinates.Length < sizeof(int) * 2)
            throw Error(cellRecord, "XCLC is shorter than two coordinates");
        var authoredLighting = OptionalSingle(cellValues, "XCLL", cellRecord);
        var templateId = OptionalSingle(cellValues, "LTMP", cellRecord);
        var inheritance = OptionalSingle(cellValues, "LNAM", cellRecord);
        if (inheritance is not null && templateId is null)
            throw Error(cellRecord, "has LNAM lighting inheritance without LTMP");
        var lighting = authoredLighting is null ? null : ReadLighting(authoredLighting, cellRecord, "XCLL");
        if (templateId is not null)
        {
            if (templateId.Length != sizeof(uint))
                throw Error(cellRecord, "LTMP must contain one FormID");
            var flags = inheritance is null ? 0u : ReadUInt32(inheritance, cellRecord, "LNAM");
            if ((flags & ~KnownLightingInheritanceFlags) != 0)
                throw Error(cellRecord, $"has unsupported lighting inheritance flags 0x{flags:x8}");
            var rawTemplateId = BinaryPrimitives.ReadUInt32LittleEndian(templateId);
            if (rawTemplateId != 0 && flags != 0)
            {
                var templateKey = cellRecord.Plugin.AdjustFormId(rawTemplateId);
                var template = stack.GetEffective(templateKey);
                if (template.Signature != "LGTM")
                    throw Error(template, "LTMP target is not LGTM");
                var templateData = RequiredSingle(Values(template), "DATA", template);
                lighting = InheritLighting(
                    lighting,
                    ReadLighting(templateData, template, "DATA"),
                    flags,
                    cellRecord);
            }
        }
        var cell = new FalloutCellDefinition(
            cellKey,
            Text(OptionalSingle(cellValues, "EDID", cellRecord)),
            data is { Length: > 0 } ? data[0] : (byte)0,
            coordinates is null ? null : (
                BinaryPrimitives.ReadInt32LittleEndian(coordinates),
                BinaryPrimitives.ReadInt32LittleEndian(coordinates.AsSpan(sizeof(int)))),
            ParentWorldspace(cellRecord),
            lighting);

        var references = new List<FalloutPlacedReference>();
        foreach (var record in stack.EffectiveCellChildren(cellKey, ReferenceTypes))
        {
            var parent = ParentCell(record);
            if (parent is null || parent.Value != cellKey)
                throw new InvalidDataException(
                    $"Native CELL child index returned {record.FormKey} outside {cellKey}.");
            var values = Values(record);
            var name = RequiredSingle(values, "NAME", record);
            var transform = RequiredSingle(values, "DATA", record);
            if (name.Length != sizeof(uint))
                throw Error(record, "NAME must contain exactly one FormID");
            if (transform.Length != ReferenceTransformBytes)
                throw Error(record, $"DATA must contain exactly {ReferenceTransformBytes} transform bytes");
            var position = new float[3];
            var rotation = new float[3];
            for (var index = 0; index < 3; ++index)
            {
                position[index] = ReadFiniteSingle(transform, index * sizeof(float), record, "position");
                rotation[index] = ReadFiniteSingle(transform, (index + 3) * sizeof(float), record, "rotation");
            }
            var scaleBytes = OptionalSingle(values, "XSCL", record);
            var scale = scaleBytes is null ? 1.0f :
                scaleBytes.Length == sizeof(float)
                    ? ReadFiniteSingle(scaleBytes, 0, record, "scale")
                    : throw Error(record, "XSCL must contain one float");
            if (scale <= 0.0f)
                throw Error(record, "XSCL must be positive");
            var enableBytes = OptionalSingle(values, "XESP", record);
            if (enableBytes is not null && enableBytes.Length != EnableParentBytes)
                throw Error(record, "XESP must contain a FormID and flags");
            var enableFlags = enableBytes is null ? 0u :
                BinaryPrimitives.ReadUInt32LittleEndian(enableBytes.AsSpan(sizeof(uint)));
            var radiusBytes = OptionalSingle(values, "XRDS", record);
            float? radiusAdjustment = radiusBytes is null ? null :
                radiusBytes.Length == sizeof(float)
                    ? ReadFiniteSingle(radiusBytes, 0, record, "reference light radius")
                    : throw Error(record, "XRDS must contain one float");
            var emittanceBytes = OptionalSingle(values, "XEMI", record);
            if (emittanceBytes is not null && emittanceBytes.Length != sizeof(uint))
                throw Error(record, "XEMI must contain one FormID");
            var teleportBytes = OptionalSingle(values, "XTEL", record);
            FalloutTeleportDestination? teleport = null;
            if (teleportBytes is not null)
            {
                if (teleportBytes.Length != TeleportDestinationBytes)
                    throw Error(
                        record,
                        $"XTEL must contain exactly {TeleportDestinationBytes} destination bytes");
                var destination = record.Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(teleportBytes));
                if (destination is null)
                    throw Error(record, "XTEL has a null destination door");
                var destinationPosition = new float[3];
                var destinationRotation = new float[3];
                for (var index = 0; index < 3; ++index)
                {
                    destinationPosition[index] = ReadFiniteSingle(
                        teleportBytes, sizeof(uint) + index * sizeof(float), record, "XTEL position");
                    destinationRotation[index] = ReadFiniteSingle(
                        teleportBytes, sizeof(uint) + (index + 3) * sizeof(float), record, "XTEL rotation");
                }
                teleport = new FalloutTeleportDestination(
                    destination.Value,
                    destinationPosition,
                    destinationRotation,
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        teleportBytes.AsSpan(TeleportDestinationBytes - sizeof(uint))));
            }
            references.Add(new FalloutPlacedReference(
                record.FormKey,
                Text(OptionalSingle(values, "EDID", record)),
                parent.Value,
                record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(name)),
                record.Flags,
                position,
                rotation,
                scale,
                radiusAdjustment,
                teleport,
                enableBytes is null ? null : record.Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(enableBytes)),
                (enableFlags & 1u) != 0,
                emittanceBytes is null ? null : record.Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(emittanceBytes))));
        }

        var bases = new Dictionary<FalloutFormKey, FalloutBaseObjectDefinition>();
        foreach (var baseKey in references.Select(reference => reference.Base).Distinct())
        {
            if (!stack.TryGetEffective(baseKey, out var record))
            {
                var expectedPrimitiveCount = references.Count(reference => reference.Base == baseKey);
                var primitiveReferences = stack.EffectiveCellChildren(cellKey, ReferenceTypes)
                    .Where(candidate =>
                    {
                        var candidateValues = Values(candidate);
                        var candidateName = RequiredSingle(candidateValues, "NAME", candidate);
                        return candidateName.Length == sizeof(uint) &&
                            candidate.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(candidateName)) == baseKey &&
                            candidateValues.TryGetValue("XPRM", out var primitiveRows) &&
                            primitiveRows.Count == 1;
                    })
                    .ToArray();
                if (expectedPrimitiveCount > 0 &&
                    primitiveReferences.Length == expectedPrimitiveCount)
                {
                    bases.Add(baseKey, new FalloutBaseObjectDefinition(
                        baseKey,
                        "XPRM",
                        string.Empty,
                        null,
                        null));
                    continue;
                }
                throw new InvalidDataException(
                    $"Native CELL {cellKey} references missing base record {baseKey} " +
                    $"(references={expectedPrimitiveCount}, exactXprm={primitiveReferences.Length}).");
            }
            var values = Values(record);
            var model = OptionalSingle(values, "MODL", record);
            var modelPath = model is null ? null : NormalizeModelPath(Text(model));
            if (model is not null && modelPath!.Length == 0)
                throw Error(record, "MODL has an empty model path");
            var light = record.Signature == "LIGH" ? ReadLight(values, record) : null;
            bases.Add(baseKey, new FalloutBaseObjectDefinition(
                baseKey,
                record.Signature,
                Text(OptionalSingle(values, "EDID", record)),
                modelPath,
                light));
        }
        return new FalloutCellScene(cell, references, bases);
    }

    internal static bool IsInitiallyDisabled(FalloutPlacedReference reference) =>
        (reference.Flags & InitiallyDisabledFlag) != 0;

    internal static FalloutLightDefinition ReadLight(FalloutPluginRecord record)
    {
        if (record.Signature != "LIGH") throw Error(record, "is not a light definition");
        return ReadLight(Values(record), record);
    }

    private static FalloutLightDefinition ReadLight(
        IReadOnlyDictionary<string, List<byte[]>> values,
        FalloutPluginRecord record)
    {
        var data = RequiredSingle(values, "DATA", record);
        if (data.Length != LightDataBytes)
            throw Error(record, $"LIGH DATA must contain exactly {LightDataBytes} bytes");
        var intensityBytes = OptionalSingle(values, "FNAM", record);
        var intensity = intensityBytes is null ? 1.0f :
            intensityBytes.Length == sizeof(float)
                ? ReadFiniteSingle(intensityBytes, 0, record, "light intensity")
                : throw Error(record, "LIGH FNAM must contain one float");
        return new FalloutLightDefinition(
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(LightDurationOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LightRadiusOffset)),
            data[LightColorOffset..(LightColorOffset + 3)],
            data[LightColorOffset + 3],
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LightFlagsOffset)),
            ReadFiniteSingle(data, LightFalloffOffset, record, "light falloff"),
            ReadFiniteSingle(data, LightFieldOfViewOffset, record, "light field of view"),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LightNearClipOffset)),
            ReadFiniteSingle(data, LightPeriodOffset, record, "light period"),
            intensity);
    }

    private static FalloutCellLighting ReadLighting(
        byte[] data,
        FalloutPluginRecord record,
        string signature)
    {
        if (data.Length != LightingDataBytes)
            throw Error(record, $"{signature} lighting must contain exactly {LightingDataBytes} bytes");
        return new FalloutCellLighting(
            data[AmbientColorStart..AmbientColorEnd],
            data[DirectionalColorStart..DirectionalColorEnd],
            data[FogColorStart..FogColorEnd],
            ReadFiniteSingle(data, FogNearOffset, record, "fog near"),
            ReadFiniteSingle(data, FogFarOffset, record, "fog far"),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(DirectionalXOffset)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(DirectionalZOffset)),
            ReadFiniteSingle(data, DirectionalFadeOffset, record, "directional fade"),
            ReadFiniteSingle(data, FogClipDistanceOffset, record, "fog clip distance"),
            ReadFiniteSingle(data, FogPowerOffset, record, "fog power"));
    }

    private static FalloutCellLighting InheritLighting(
        FalloutCellLighting? authored,
        FalloutCellLighting template,
        uint flags,
        FalloutPluginRecord record)
    {
        if (authored is null && flags != KnownLightingInheritanceFlags)
            throw Error(record, "partial lighting inheritance has no authored XCLL");
        var source = authored ?? template;
        return new FalloutCellLighting(
            (flags & 0x001) != 0 ? template.AmbientRgb : source.AmbientRgb,
            (flags & 0x002) != 0 ? template.DirectionalRgb : source.DirectionalRgb,
            (flags & 0x004) != 0 ? template.FogRgb : source.FogRgb,
            (flags & 0x008) != 0 ? template.FogNear : source.FogNear,
            (flags & 0x010) != 0 ? template.FogFar : source.FogFar,
            (flags & 0x020) != 0 ? template.DirectionalXDegrees : source.DirectionalXDegrees,
            (flags & 0x020) != 0 ? template.DirectionalZDegrees : source.DirectionalZDegrees,
            (flags & 0x040) != 0 ? template.DirectionalFade : source.DirectionalFade,
            (flags & 0x080) != 0 ? template.FogClipDistance : source.FogClipDistance,
            (flags & 0x100) != 0 ? template.FogPower : source.FogPower);
    }

    private static uint ReadUInt32(byte[] data, FalloutPluginRecord record, string label)
    {
        if (data.Length != sizeof(uint))
            throw Error(record, $"{label} must contain one uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }

    internal static FalloutFormKey? ParentCell(FalloutPluginRecord record)
    {
        for (var index = record.Groups.Count - 1; index >= 0; --index)
            if (record.Groups[index].Type == CellChildrenGroupType)
                return record.Plugin.AdjustFormId(record.Groups[index].LabelAsUInt32);
        return null;
    }

    private static FalloutFormKey? ParentWorldspace(FalloutPluginRecord record)
    {
        for (var index = record.Groups.Count - 1; index >= 0; --index)
            if (record.Groups[index].Type == WorldChildrenGroupType)
                return record.Plugin.AdjustFormId(record.Groups[index].LabelAsUInt32);
        return null;
    }

    private static Dictionary<string, List<byte[]>> Values(FalloutPluginRecord record)
    {
        var result = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);
        foreach (var subrecord in record.ReadSubrecords())
        {
            if (!result.TryGetValue(subrecord.Signature, out var values))
                result.Add(subrecord.Signature, values = []);
            values.Add(subrecord.Data.ToArray());
        }
        return result;
    }

    private static byte[] RequiredSingle(
        IReadOnlyDictionary<string, List<byte[]>> values,
        string signature,
        FalloutPluginRecord record) =>
        OptionalSingle(values, signature, record) ??
        throw Error(record, $"is missing required {signature}");

    private static byte[]? OptionalSingle(
        IReadOnlyDictionary<string, List<byte[]>> values,
        string signature,
        FalloutPluginRecord record)
    {
        if (!values.TryGetValue(signature, out var matches))
            return null;
        if (matches.Count != 1)
            throw Error(record, $"contains {matches.Count} {signature} subrecords");
        return matches[0];
    }

    private static string Text(byte[]? data)
    {
        if (data is null)
            return string.Empty;
        var end = Array.IndexOf(data, (byte)0);
        return Encoding.UTF8.GetString(data, 0, end < 0 ? data.Length : end);
    }

    private static string NormalizeModelPath(string value)
    {
        var path = value.Replace('/', '\\').TrimStart('\\');
        if (path.StartsWith(DataMeshesPrefix, StringComparison.OrdinalIgnoreCase))
            path = path[DataMeshesPrefix.Length..];
        else if (path.StartsWith(MeshesPrefix, StringComparison.OrdinalIgnoreCase))
            path = path[MeshesPrefix.Length..];
        return $"meshes\\{path}".ToLowerInvariant();
    }

    private static float ReadFiniteSingle(
        ReadOnlySpan<byte> data,
        int offset,
        FalloutPluginRecord record,
        string label)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        return float.IsFinite(value) ? value : throw Error(record, $"has non-finite {label}");
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"{record.Plugin.Name} {record.Signature} {record.FormKey} {detail}.");
}
