using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLandscapeOpacity(
    ushort VertexIndex,
    ushort Unknown,
    float Opacity);

internal sealed record FalloutLandscapeLayer(
    FalloutFormKey Texture,
    byte Quadrant,
    ushort LayerIndex,
    byte Unknown,
    bool UsesQuadrantDefault,
    IReadOnlyList<FalloutLandscapeOpacity> Opacities);

internal sealed record FalloutLandscapeTexture(
    FalloutFormKey LandscapeTexture,
    string LandscapeTextureEditorId,
    FalloutFormKey TextureSet,
    string TextureSetEditorId,
    string DiffusePath,
    string? NormalPath);

internal sealed record FalloutLandscapeTransport(
    FalloutFormKey PersistentDestinationCell,
    FalloutFormKey ActiveCell,
    (int X, int Y) ActiveCoordinates,
    FalloutFormKey Worldspace,
    FalloutFormKey Landscape,
    uint Flags,
    float[] Heights,
    float[] Normals,
    byte[] Colors,
    IReadOnlyList<FalloutLandscapeLayer> BaseLayers,
    IReadOnlyList<FalloutLandscapeLayer> AlphaLayers,
    IReadOnlyDictionary<FalloutFormKey, FalloutLandscapeTexture> Textures);

internal static class FalloutLandscapeTransportResolver
{
    internal const int VertexSide = 33;
    internal const int QuadrantVertexSide = 17;
    internal const float VertexSpacingGameUnits = 128.0f;
    internal const float ExteriorCellSideGameUnits = 4096.0f;

    private const int VertexCount = VertexSide * VertexSide;
    private const int NormalComponentCount = 3;
    private const int HeightHeaderBytes = sizeof(float);
    private const int HeightTrailerBytes = 3;
    private const int HeightBytes = HeightHeaderBytes + VertexCount + HeightTrailerBytes;
    private const int NormalBytes = VertexCount * NormalComponentCount;
    private const int ColorBytes = VertexCount * NormalComponentCount;
    private const int LayerHeaderBytes = 8;
    private const int OpacityRowBytes = 8;
    private const int WorldChildrenGroupType = 1;
    private const uint VertexDataFlag = 0x0000_0001;
    private const float HeightScale = 8.0f;
    private const float NormalLengthEpsilon = 0.000001f;

    internal static FalloutLandscapeTransport Resolve(
        FalloutPluginStack stack,
        FalloutDoorTransition transition)
    {
        var entry = transition.SourceDoor.Teleport ??
            throw new InvalidDataException("Native LAND entry door has no XTEL transform.");
        var coordinates = (
            X: (int)MathF.Floor(entry.Position[0] / ExteriorCellSideGameUnits),
            Y: (int)MathF.Floor(entry.Position[1] / ExteriorCellSideGameUnits));
        var cells = stack.EffectiveRecords("CELL").Where(record =>
            HasWorldspace(record, transition.DestinationWorldspace) &&
            ReadCoordinates(record) == coordinates).ToArray();
        if (cells.Length != 1)
            throw new InvalidDataException(
                $"Native LAND active grid {coordinates} in {transition.DestinationWorldspace} has " +
                $"{cells.Length} winning CELL records.");
        var cell = cells[0];
        var landscapes = stack.EffectiveRecords("LAND").Where(record =>
            FalloutCellSceneReader.ParentCell(record) == cell.FormKey &&
            HasWorldspace(record, transition.DestinationWorldspace)).ToArray();
        if (landscapes.Length != 1)
            throw new InvalidDataException(
                $"Native LAND active CELL {cell.FormKey} has {landscapes.Length} winning LAND records.");
        return ReadLandscape(
            stack,
            landscapes[0],
            transition.DestinationScene.Cell.FormKey,
            cell.FormKey,
            coordinates,
            transition.DestinationWorldspace);
    }

    private static FalloutLandscapeTransport ReadLandscape(
        FalloutPluginStack stack,
        FalloutPluginRecord record,
        FalloutFormKey persistentDestinationCell,
        FalloutFormKey activeCell,
        (int X, int Y) coordinates,
        FalloutFormKey worldspace)
    {
        var source = record.ReadSubrecords().ToArray();
        var data = RequiredSingle(source, "DATA", record);
        if (data.Length != sizeof(uint))
            throw Error(record, "DATA must contain one uint32 flag field");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        if ((flags & VertexDataFlag) == 0)
            throw new NotSupportedException(
                $"Native LAND {record.FormKey} has no authored vertex geometry.");
        var heights = ReadHeights(RequiredSingle(source, "VHGT", record), record);
        var normals = ReadNormals(RequiredSingle(source, "VNML", record), record);
        var colorRows = source.Where(value => value.Signature == "VCLR").ToArray();
        if (colorRows.Length > 1)
            throw Error(record, $"contains {colorRows.Length} VCLR subrecords");
        var colors = colorRows.Length == 0
            ? Enumerable.Repeat(byte.MaxValue, ColorBytes).ToArray()
            : ReadColors(colorRows[0].Data, record);
        var baseLayers = source.Where(value => value.Signature == "BTXT")
            .Select(value => ReadLayerHeader(record, value.Data, "BTXT", baseLayer: true))
            .ToArray();
        var quadrants = baseLayers.Select(value => value.Quadrant).ToArray();
        if (baseLayers.Length != 4 || quadrants.Distinct().Count() != 4 ||
            quadrants.Any(value => value > 3))
            throw new NotSupportedException(
                $"Native LAND {record.FormKey} must author one BTXT for each quadrant; found " +
                $"[{string.Join(',', quadrants)}]. No prepared/default texture is substituted.");
        var baseByQuadrant = baseLayers.ToDictionary(value => value.Quadrant);
        var alphaLayers = new List<FalloutLandscapeLayer>();
        FalloutLandscapeLayer? pending = null;
        foreach (var subrecord in source)
        {
            if (subrecord.Signature == "ATXT")
            {
                if (pending is not null)
                    throw Error(record, "ATXT is missing its VTXT rows");
                pending = ReadLayerHeader(record, subrecord.Data, "ATXT", baseLayer: false);
            }
            else if (subrecord.Signature == "VTXT")
            {
                if (pending is null)
                    throw Error(record, "VTXT has no preceding ATXT");
                var effectiveTexture = pending.Texture.ObjectId == 0
                    ? baseByQuadrant[pending.Quadrant].Texture
                    : pending.Texture;
                alphaLayers.Add(pending with
                {
                    Texture = effectiveTexture,
                    UsesQuadrantDefault = pending.Texture.ObjectId == 0,
                    Opacities = ReadOpacities(record, subrecord.Data),
                });
                pending = null;
            }
        }
        if (pending is not null)
            throw Error(record, "ATXT is missing its VTXT rows");
        if (alphaLayers.Select(value => (value.Quadrant, value.LayerIndex)).Distinct().Count() !=
            alphaLayers.Count)
            throw Error(record, "duplicates an ATXT quadrant/layer index");
        var textures = baseLayers.Concat(alphaLayers).Select(value => value.Texture)
            .Distinct()
            .ToDictionary(key => key, key => ReadTexture(stack, key));
        return new FalloutLandscapeTransport(
            persistentDestinationCell,
            activeCell,
            coordinates,
            worldspace,
            record.FormKey,
            flags,
            heights,
            normals,
            colors,
            baseLayers.OrderBy(value => value.Quadrant).ToArray(),
            alphaLayers.OrderBy(value => value.Quadrant).ThenBy(value => value.LayerIndex).ToArray(),
            textures);
    }

    private static FalloutLandscapeLayer ReadLayerHeader(
        FalloutPluginRecord record,
        ReadOnlyMemory<byte> data,
        string signature,
        bool baseLayer)
    {
        if (data.Length != LayerHeaderBytes)
            throw Error(record, $"{signature} must contain {LayerHeaderBytes} bytes");
        var rawTexture = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        if (baseLayer && rawTexture == 0)
            throw Error(record, $"{signature} has a null base texture");
        var quadrant = data.Span[sizeof(uint)];
        if (quadrant > 3)
            throw Error(record, $"{signature} has invalid quadrant {quadrant}");
        return new FalloutLandscapeLayer(
            rawTexture == 0
                ? new FalloutFormKey(record.Plugin.Name, 0)
                : record.Plugin.AdjustFormId(rawTexture),
            quadrant,
            BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(sizeof(uint) + 2)..]),
            data.Span[sizeof(uint) + 1],
            UsesQuadrantDefault: false,
            []);
    }

    private static IReadOnlyList<FalloutLandscapeOpacity> ReadOpacities(
        FalloutPluginRecord record,
        ReadOnlyMemory<byte> data)
    {
        if (data.Length % OpacityRowBytes != 0)
            throw Error(record, $"VTXT must contain {OpacityRowBytes}-byte rows");
        var result = new List<FalloutLandscapeOpacity>();
        var indices = new HashSet<ushort>();
        for (var offset = 0; offset < data.Length; offset += OpacityRowBytes)
        {
            var vertex = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[offset..]);
            var unknown = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + sizeof(ushort))..]);
            var opacity = BinaryPrimitives.ReadSingleLittleEndian(data.Span[(offset + sizeof(uint))..]);
            if (vertex >= QuadrantVertexSide * QuadrantVertexSide ||
                !indices.Add(vertex) || !float.IsFinite(opacity) || opacity < 0.0f || opacity > 1.0f)
                throw Error(record, "VTXT contains an invalid vertex opacity row");
            result.Add(new FalloutLandscapeOpacity(vertex, unknown, opacity));
        }
        return result;
    }

    private static float[] ReadHeights(ReadOnlyMemory<byte> data, FalloutPluginRecord record)
    {
        if (data.Length != HeightBytes)
            throw Error(record, $"VHGT must contain {HeightBytes} bytes");
        var result = new float[VertexCount];
        var initial = BinaryPrimitives.ReadSingleLittleEndian(data.Span) * HeightScale;
        if (!float.IsFinite(initial))
            throw Error(record, "VHGT has a non-finite initial height");
        for (var y = 0; y < VertexSide; ++y)
        {
            for (var x = 0; x < VertexSide; ++x)
            {
                var index = y * VertexSide + x;
                var delta = unchecked((sbyte)data.Span[HeightHeaderBytes + index]) * HeightScale;
                result[index] = x > 0
                    ? result[index - 1] + delta
                    : y > 0
                        ? result[index - VertexSide] + delta
                        : initial + delta;
            }
        }
        return result;
    }

    private static float[] ReadNormals(ReadOnlyMemory<byte> data, FalloutPluginRecord record)
    {
        if (data.Length != NormalBytes)
            throw Error(record, $"VNML must contain {NormalBytes} bytes");
        var result = new float[NormalBytes];
        for (var index = 0; index < VertexCount; ++index)
        {
            var offset = index * NormalComponentCount;
            var x = unchecked((sbyte)data.Span[offset]);
            var y = unchecked((sbyte)data.Span[offset + 1]);
            var z = unchecked((sbyte)data.Span[offset + 2]);
            var length = MathF.Sqrt(x * x + y * y + z * z);
            if (length <= NormalLengthEpsilon)
                throw Error(record, $"VNML vertex {index} has a zero normal");
            result[offset] = x / length;
            result[offset + 1] = y / length;
            result[offset + 2] = z / length;
        }
        return result;
    }

    private static byte[] ReadColors(ReadOnlyMemory<byte> data, FalloutPluginRecord record)
    {
        if (data.Length != ColorBytes)
            throw Error(record, $"VCLR must contain {ColorBytes} bytes");
        return data.ToArray();
    }

    private static FalloutLandscapeTexture ReadTexture(FalloutPluginStack stack, FalloutFormKey key)
    {
        var ltex = stack.GetEffective(key);
        if (ltex.Signature != "LTEX")
            throw Error(ltex, $"resolved LAND texture {key} is not LTEX");
        var ltexRows = ltex.ReadSubrecords().ToArray();
        var textureSetBytes = RequiredSingle(ltexRows, "TNAM", ltex);
        if (textureSetBytes.Length != sizeof(uint))
            throw Error(ltex, "TNAM must contain one FormID");
        var textureSetKey = ltex.Plugin.AdjustOptionalFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(textureSetBytes.Span)) ??
            throw Error(ltex, "TNAM has a null texture set");
        var txst = stack.GetEffective(textureSetKey);
        if (txst.Signature != "TXST")
            throw Error(txst, $"resolved LAND texture set {textureSetKey} is not TXST");
        var txstRows = txst.ReadSubrecords().ToArray();
        return new FalloutLandscapeTexture(
            key,
            ReadOptionalText(ltexRows, "EDID", ltex),
            textureSetKey,
            ReadOptionalText(txstRows, "EDID", txst),
            NormalizeTexturePath(ReadRequiredText(txstRows, "TX00", txst)),
            ReadOptionalPath(txstRows, "TX01", txst));
    }

    private static bool HasWorldspace(FalloutPluginRecord record, FalloutFormKey worldspace)
    {
        var group = record.Groups.LastOrDefault(value => value.Type == WorldChildrenGroupType);
        return group.Type == WorldChildrenGroupType &&
            record.Plugin.AdjustFormId(group.LabelAsUInt32) == worldspace;
    }

    private static (int X, int Y)? ReadCoordinates(FalloutPluginRecord record)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == "XCLC").ToArray();
        if (rows.Length == 0)
            return null;
        if (rows.Length != 1 || rows[0].Data.Length < sizeof(int) * 2)
            throw Error(record, "XCLC must contain at least two int32 coordinates");
        return (
            BinaryPrimitives.ReadInt32LittleEndian(rows[0].Data.Span),
            BinaryPrimitives.ReadInt32LittleEndian(rows[0].Data.Span[sizeof(int)..]));
    }

    private static ReadOnlyMemory<byte> RequiredSingle(
        IReadOnlyList<FalloutPluginSubrecord> source,
        string signature,
        FalloutPluginRecord record)
    {
        var rows = source.Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1)
            throw Error(record, $"must contain exactly one {signature}; found {rows.Length}");
        return rows[0].Data;
    }

    private static string ReadRequiredText(
        IReadOnlyList<FalloutPluginSubrecord> source,
        string signature,
        FalloutPluginRecord record)
    {
        var value = ReadText(RequiredSingle(source, signature, record), record, signature);
        return value.Length == 0 ? throw Error(record, $"{signature} is empty") : value;
    }

    private static string ReadOptionalText(
        IReadOnlyList<FalloutPluginSubrecord> source,
        string signature,
        FalloutPluginRecord record)
    {
        var rows = source.Where(value => value.Signature == signature).ToArray();
        if (rows.Length > 1)
            throw Error(record, $"contains {rows.Length} {signature} subrecords");
        return rows.Length == 0 ? string.Empty : ReadText(rows[0].Data, record, signature);
    }

    private static string? ReadOptionalPath(
        IReadOnlyList<FalloutPluginSubrecord> source,
        string signature,
        FalloutPluginRecord record)
    {
        var value = ReadOptionalText(source, signature, record);
        return value.Length == 0 ? null : NormalizeTexturePath(value);
    }

    private static string ReadText(
        ReadOnlyMemory<byte> data,
        FalloutPluginRecord record,
        string signature)
    {
        var terminator = data.Span.IndexOf((byte)0);
        if (terminator != data.Length - 1 ||
            data.Span[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, $"{signature} must be null-terminated ASCII");
        return Encoding.ASCII.GetString(data.Span[..terminator]);
    }

    private static string NormalizeTexturePath(string source)
    {
        var path = source.Replace('/', '\\').TrimStart('\\');
        return path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)
            ? path.ToLowerInvariant()
            : $"textures\\{path.ToLowerInvariant()}";
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"Native {record.Signature} {record.FormKey} {detail}.");
}
