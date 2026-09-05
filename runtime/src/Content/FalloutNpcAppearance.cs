using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNpcTextureOverride(string ShapeName, int ShapeIndex, FalloutFormKey TextureSet,
    IReadOnlyDictionary<string, string> Textures, byte[] SourceBytes);

internal sealed record FalloutNpcAppearancePart(string Role, FalloutFormKey Source, string? ModelPath,
    string? TexturePath, uint BipedSlots, byte FaceGenModelFlags,
    IReadOnlyList<FalloutNpcTextureOverride> AlternateTextures, FalloutFormKey? TextureSource = null);

internal sealed record FalloutNpcFaceGen(FalloutFormKey Source, byte[] SymmetricGeometry,
    byte[] AsymmetricGeometry, byte[] SymmetricTexture);

internal sealed record FalloutActorAppearanceState(bool Female, FalloutFormKey Race,
    FalloutFormKey Hair, FalloutFormKey Eyes, FalloutNpcFaceGen? FaceGen = null,
    byte[]? HairColor = null, byte[]? HairLength = null, IReadOnlyList<FalloutFormKey>? HeadParts = null);

internal sealed record FalloutNpcInventoryItem(FalloutFormKey Source, FalloutFormKey Item, string Signature,
    int Count, byte[]? ExtraData, IReadOnlyList<FalloutFormKey> PossibleArmor);

internal sealed record FalloutNpcArmor(FalloutFormKey Source, uint BipedSlots, byte GeneralFlags,
    FalloutNpcAppearancePart Model, IReadOnlyList<FalloutNpcAppearancePart> Addons);

internal sealed record FalloutNpcAppearance(FalloutFormKey Npc, FalloutFormKey? Reference,
    FalloutFormKey TraitsOwner, FalloutFormKey ModelOwner, FalloutFormKey InventoryOwner,
    bool Female, FalloutFormKey Race, string SkeletonPath, float RaceHeight, float RaceWeight,
    byte[] NpcHeightBytes, byte[] NpcWeightBytes, byte[] HairColorBytes, byte[] HairLengthBytes,
    FalloutNpcFaceGen FaceGen, FalloutNpcFaceGen RaceFaceGen,
    IReadOnlyList<FalloutNpcAppearancePart> RaceParts, IReadOnlyList<FalloutNpcAppearancePart> Models,
    IReadOnlyList<FalloutNpcInventoryItem> Inventory, IReadOnlyList<FalloutNpcArmor> Armor,
    IReadOnlyList<FalloutFormKey> EquippedArmor, IReadOnlyList<string> Blockers,
    FalloutFormKey? Hair, FalloutFormKey? Eyes, bool RuntimeFace = false)
{
    internal bool CanConstruct => Blockers.Count == 0;
}

/// <summary>
/// Resolves the winning NPC/RACE/HAIR/EYES/HDPT and ARMO/BIPL/ARMA graph.
/// Binary layouts follow xEdit FNV definitions; template groups are independent.
/// Inventory candidates are not a replacement for runtime inventory state.
/// </summary>
internal static class FalloutNpcAppearanceResolver
{
    private static readonly string[] HeadRoles = ["head", "ears", "mouth", "teeth-lower", "teeth-upper", "tongue", "eye-left", "eye-right"];
    private static readonly string[] BodyRoles = ["body", "hand-left", "hand-right", "body-texture"];

    internal static FalloutNpcAppearance Resolve(FalloutPluginStack stack, FalloutFormKey npcKey,
        FalloutFormKey? reference = null, IReadOnlyList<FalloutFormKey>? equippedArmor = null,
        FalloutActorAppearanceState? appearanceState = null)
    {
        var npc = Require(stack, npcKey, "NPC_");
        if (reference is { } referenceKey)
        {
            var placed = Require(stack, referenceKey, "ACHR");
            if (!Same(RequiredForm(placed, "NAME"), npc.FormKey))
                throw Error(placed, "NAME does not match the requested NPC");
        }
        var traits = TemplateOwner(stack, npc, 1);
        var model = TemplateOwner(stack, npc, 64);
        var inventoryOwner = TemplateOwner(stack, npc, 256);
        var female = appearanceState?.Female ?? ((BinaryPrimitives.ReadUInt32LittleEndian(Bytes(traits, "ACBS", 24)) & 1) != 0);
        var race = Require(stack, appearanceState?.Race ?? RequiredForm(traits, "RNAM"), "RACE");
        var raceData = Bytes(race, "DATA", 36);
        var raceParts = ReadRaceParts(stack, race, female, out var raceFace);
        var parts = new List<FalloutNpcAppearancePart>(raceParts.Where(part => part.ModelPath?.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) == true));
        var eye = appearanceState?.Eyes ?? OptionalForm(model, "ENAM");
        if (eye is { } eyeKey)
        {
            var eyes = Require(stack, eyeKey, "EYES");
            var texture = PathField(eyes, "ICON", "textures", required: true);
            for (var index = 0; index < parts.Count; ++index)
                if (parts[index].Role is "eye-left" or "eye-right")
                    parts[index] = parts[index] with { TexturePath = texture, TextureSource = eyes.FormKey };
        }
        var hair = appearanceState?.Hair ?? OptionalForm(model, "HNAM");
        if (hair is { } hairKey)
        {
            var hairRecord = Require(stack, hairKey, "HAIR");
            parts.Add(ReadModel(stack, hairRecord, "hair", "MODL", "MODS", "MODD", 2,
                PathField(hairRecord, "ICON", "textures", required: true)));
        }
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headParts = appearanceState?.HeadParts ?? model.ReadSubrecords().Where(row => row.Signature == "PNAM")
            .Select(row => model.Plugin.AdjustFormId(UInt32(row.Data.Span, model, "PNAM"))).ToArray();
        foreach (var headPart in headParts) AddHeadParts(stack, headPart, parts, added, []);

        var inventory = ReadInventory(stack, inventoryOwner);
        var armorKeys = inventory.Where(item => item.Signature == "ARMO" && item.Count > 0).Select(item => item.Item)
            .Concat(equippedArmor ?? []).DistinctBy(key => key.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();
        var armors = armorKeys.Select(key => ReadArmor(stack, key, female)).ToArray();
        var blockers = new List<string>();
        if (eye is null && parts.Any(part => part.Role is "eye-left" or "eye-right"))
            blockers.Add("default-eye-selection-required");
        foreach (var item in inventory.Where(item => item.Count < 0))
            blockers.Add($"negative-inventory-count-semantics-required:{item.Item}");
        var selected = equippedArmor?.ToArray() ?? armorKeys;
        if (equippedArmor is null)
        {
            foreach (var item in inventory.Where(item => item.Signature == "LVLI" && item.PossibleArmor.Count != 0 && item.Count > 0))
                blockers.Add($"inventory-leveled-equipment-selection-required:{item.Item}");
        }
        uint occupied = 0;
        foreach (var key in selected)
        {
            var armor = armors.Single(item => Same(item.Source, key));
            var armorParts = new[] { armor.Model }.Concat(armor.Addons).ToArray();
            foreach (var armorPart in armorParts.Where(part => part.ModelPath is null && part.BipedSlots != 0))
                blockers.Add($"sex-specific-equipped-model-absent:{armorPart.Source}");
            var slots = armorParts.Aggregate(0u, (mask, part) => mask | part.BipedSlots);
            if ((occupied & slots) != 0)
                blockers.Add($"equipment-slot-conflict:{key}:0x{occupied & slots:x8}");
            occupied |= slots;
            parts.AddRange(armorParts.Where(part => part.ModelPath is not null));
        }
        // Body slots are explicit in both RACE INDX and ARMO/ARMA BMDT.
        // Head-addon hiding needs the runtime's head-part policy; do not guess.
        if ((occupied & 3) != 0)
            blockers.Add("head-equipment-visibility-policy-required");
        parts.RemoveAll(part => Same(part.Source, race.FormKey) &&
            part.Role is "body" or "hand-left" or "hand-right" && (part.BipedSlots & occupied) != 0);
        return new FalloutNpcAppearance(npc.FormKey, reference, traits.FormKey, model.FormKey, inventoryOwner.FormKey,
            female, race.FormKey, PathField(model, "MODL", "meshes", required: true)!,
            Float(raceData, female ? 20 : 16, race), Float(raceData, female ? 28 : 24, race),
            OptionalBytes(traits, "NAM6", 4), OptionalBytes(traits, "NAM7", 4),
            appearanceState?.HairColor ?? Bytes(model, "HCLR", 4), appearanceState?.HairLength ?? OptionalBytes(model, "LNAM", 4),
            appearanceState?.FaceGen ?? ReadFaceGen(model, model.ReadSubrecords().ToArray()),
            raceFace, raceParts, parts, inventory, armors, selected, blockers, hair, eye, appearanceState is not null);
    }

    private static FalloutPluginRecord TemplateOwner(FalloutPluginStack stack, FalloutPluginRecord record, ushort group)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            if (!seen.Add(record.FormKey.ToString())) throw Error(record, "actor template cycle");
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(Bytes(record, "ACBS", 24).AsSpan(22));
            if ((flags & group) == 0) return record;
            var template = stack.GetEffective(RequiredForm(record, "TPLT"));
            if (template.Signature == "LVLN")
                throw new NotSupportedException($"NPC template {template.FormKey} requires authoritative leveled-actor selection.");
            if (template.Signature != "NPC_") throw Error(template, "NPC template must resolve to NPC_ or LVLN");
            record = template;
        }
    }

    private static IReadOnlyList<FalloutNpcAppearancePart> ReadRaceParts(FalloutPluginStack stack,
        FalloutPluginRecord race, bool female, out FalloutNpcFaceGen face)
    {
        var parts = new List<FalloutNpcAppearancePart>();
        var rows = race.ReadSubrecords().ToArray();
        string? section = null;
        bool? sectionFemale = null;
        var faceRows = new List<FalloutPluginSubrecord>();
        for (var index = 0; index < rows.Length; ++index)
        {
            var row = rows[index];
            if (row.Signature is "NAM0" or "NAM1")
            {
                section = row.Signature;
                sectionFemale = null;
                continue;
            }
            if (row.Signature is "MNAM" or "FNAM")
            {
                sectionFemale = row.Signature == "FNAM";
                continue;
            }
            if (row.Signature is "HNAM" or "ENAM") { section = "face"; sectionFemale = null; continue; }
            if (sectionFemale != female) continue;
            if (section == "face" && row.Signature is "FGGS" or "FGGA" or "FGTS") { faceRows.Add(row); continue; }
            if (row.Signature != "INDX" || section is not ("NAM0" or "NAM1")) continue;
            var partIndex = UInt32(row.Data.Span, race, "INDX");
            var roles = section == "NAM0" ? HeadRoles : BodyRoles;
            if (partIndex >= roles.Length) throw Error(race, $"unknown {section} part index {partIndex}");
            var fields = new List<FalloutPluginSubrecord>();
            while (index + 1 < rows.Length && rows[index + 1].Signature is "MODL" or "MODT" or "MODS" or "MODD" or "ICON")
                fields.Add(rows[++index]);
            var role = roles[partIndex];
            if (parts.Any(part => part.Role == role)) throw Error(race, $"duplicate {role} part");
            var mask = section == "NAM1" ? partIndex switch { 0 => 4u, 1 => 8u, 2 => 16u, _ => 0u } : 1u;
            parts.Add(ReadModel(stack, race, role, "MODL", "MODS", "MODD", mask,
                PathField(race, "ICON", "textures", false, fields), fields));
        }
        face = ReadFaceGen(race, faceRows);
        if (parts.Count == 0) throw Error(race, "sex-specific race parts are absent");
        return parts;
    }

    private static void AddHeadParts(FalloutPluginStack stack, FalloutFormKey key,
        List<FalloutNpcAppearancePart> result, HashSet<string> added, HashSet<string> ancestors)
    {
        if (!ancestors.Add(key.ToString())) throw new InvalidDataException($"HDPT cycle at {key}.");
        if (added.Add(key.ToString()))
        {
            var record = Require(stack, key, "HDPT");
            result.Add(ReadModel(stack, record, "head-addon", "MODL", "MODS", "MODD", 0, null));
            foreach (var row in record.ReadSubrecords().Where(row => row.Signature == "HNAM"))
                AddHeadParts(stack, record.Plugin.AdjustFormId(UInt32(row.Data.Span, record, "HNAM")), result, added, ancestors);
        }
        ancestors.Remove(key.ToString());
    }

    private static IReadOnlyList<FalloutNpcInventoryItem> ReadInventory(FalloutPluginStack stack, FalloutPluginRecord owner)
    {
        var result = new List<FalloutNpcInventoryItem>();
        var rows = owner.ReadSubrecords().ToArray();
        for (var index = 0; index < rows.Length; ++index)
        {
            if (rows[index].Signature != "CNTO") continue;
            var data = rows[index].Data.Span;
            if (data.Length != 8) throw Error(owner, "CNTO must contain FormID and signed count");
            var key = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
            var item = stack.GetEffective(key);
            var count = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            byte[]? extra = null;
            if (index + 1 < rows.Length && rows[index + 1].Signature == "COED")
            {
                extra = rows[++index].Data.ToArray();
                if (extra.Length != 12) throw Error(owner, "COED must contain owner, rank/global and condition");
            }
            var possible = new List<FalloutFormKey>();
            CollectPossibleArmor(stack, item, possible, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            result.Add(new FalloutNpcInventoryItem(owner.FormKey, key, item.Signature, count, extra, possible));
        }
        return result;
    }

    private static void CollectPossibleArmor(FalloutPluginStack stack, FalloutPluginRecord record,
        List<FalloutFormKey> result, HashSet<string> ancestors)
    {
        if (record.Signature == "ARMO") { result.Add(record.FormKey); return; }
        if (record.Signature != "LVLI") return;
        if (!ancestors.Add(record.FormKey.ToString())) throw Error(record, "leveled inventory cycle");
        foreach (var row in record.ReadSubrecords().Where(row => row.Signature == "LVLO"))
        {
            if (row.Data.Length != 12) throw Error(record, "LVLO layout must be 12 bytes");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span[4..]);
            if (raw != 0) CollectPossibleArmor(stack, stack.GetEffective(record.Plugin.AdjustFormId(raw)), result, ancestors);
        }
        ancestors.Remove(record.FormKey.ToString());
    }

    private static FalloutNpcArmor ReadArmor(FalloutPluginStack stack, FalloutFormKey key, bool female)
    {
        var record = Require(stack, key, "ARMO");
        var data = Bytes(record, "BMDT", 8);
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var part = ReadModel(stack, record, "armor", female ? "MOD3" : "MODL", female ? "MO3S" : "MODS",
            female ? "MOSD" : "MODD", mask, null);
        var addons = new List<FalloutNpcAppearancePart>();
        if (OptionalForm(record, "BIPL") is { } listKey)
        {
            var list = Require(stack, listKey, "FLST");
            foreach (var row in list.ReadSubrecords().Where(row => row.Signature == "LNAM"))
            {
                var addon = Require(stack, list.Plugin.AdjustFormId(UInt32(row.Data.Span, list, "LNAM")), "ARMA");
                var addonMask = BinaryPrimitives.ReadUInt32LittleEndian(Bytes(addon, "BMDT", 8));
                addons.Add(ReadModel(stack, addon, "armor-addon", female ? "MOD3" : "MODL", female ? "MO3S" : "MODS",
                    female ? "MOSD" : "MODD", addonMask, null));
            }
        }
        return new FalloutNpcArmor(record.FormKey, mask, data[4], part, addons);
    }

    private static FalloutNpcAppearancePart ReadModel(FalloutPluginStack stack, FalloutPluginRecord record, string role,
        string modelSignature, string alternateSignature, string flagsSignature, uint mask, string? texture,
        IReadOnlyList<FalloutPluginSubrecord>? fields = null)
    {
        fields ??= record.ReadSubrecords().ToArray();
        var flags = Optional(record, flagsSignature, fields);
        if (flags is not null && flags.Value.Length != 1) throw Error(record, $"{flagsSignature} must contain one byte");
        var alternates = Optional(record, alternateSignature, fields);
        return new FalloutNpcAppearancePart(role, record.FormKey, PathField(record, modelSignature, "meshes", false, fields),
            texture, mask, flags?.Span[0] ?? 0,
            alternates is null ? [] : ReadAlternates(stack, record, alternates.Value), texture is null ? null : record.FormKey);
    }

    private static IReadOnlyList<FalloutNpcTextureOverride> ReadAlternates(FalloutPluginStack stack,
        FalloutPluginRecord owner, ReadOnlyMemory<byte> source)
    {
        var data = source.Span;
        if (data.Length < 4) throw Error(owner, "alternate texture count is absent");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var offset = 4;
        var result = new List<FalloutNpcTextureOverride>();
        for (uint index = 0; index < count; ++index)
        {
            var start = offset;
            if (data.Length - offset < 4) throw Error(owner, "alternate texture name length is absent");
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); offset += 4;
            if (length > data.Length - offset - 8) throw Error(owner, "alternate texture entry is truncated");
            var name = Encoding.UTF8.GetString(data.Slice(offset, checked((int)length))).TrimEnd('\0'); offset += (int)length;
            var textureKey = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])); offset += 4;
            var shapeIndex = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]); offset += 4;
            var texture = Require(stack, textureKey, "TXST");
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in texture.ReadSubrecords().Where(row => row.Signature.StartsWith("TX0", StringComparison.Ordinal)))
                paths.Add(row.Signature, NormalizePath(Text(row.Data.Span, texture), "textures"));
            result.Add(new FalloutNpcTextureOverride(name, shapeIndex, textureKey, paths, data[start..offset].ToArray()));
        }
        if (offset != data.Length) throw Error(owner, "alternate textures contain trailing bytes");
        return result;
    }

    private static FalloutNpcFaceGen ReadFaceGen(FalloutPluginRecord record, IReadOnlyList<FalloutPluginSubrecord> fields) =>
        new(record.FormKey, Bytes(record, "FGGS", 200, fields), Bytes(record, "FGGA", 120, fields), Bytes(record, "FGTS", 200, fields));

    private static FalloutPluginRecord Require(FalloutPluginStack stack, FalloutFormKey key, string signature)
    {
        var record = stack.GetEffective(key);
        if (record.Signature != signature) throw Error(record, $"expected {signature}");
        return record;
    }

    private static ReadOnlyMemory<byte>? Optional(FalloutPluginRecord record, string signature,
        IReadOnlyList<FalloutPluginSubrecord>? fields = null)
    {
        var rows = (fields ?? record.ReadSubrecords().ToArray()).Where(row => row.Signature == signature).ToArray();
        if (rows.Length > 1) throw Error(record, $"duplicate {signature}");
        if (rows.Length == 0) return null;
        return rows[0].Data;
    }

    private static byte[] Bytes(FalloutPluginRecord record, string signature, int length,
        IReadOnlyList<FalloutPluginSubrecord>? fields = null)
    {
        var data = Optional(record, signature, fields);
        if (data is null || data.Value.Length != length) throw Error(record, $"{signature} must contain {length} bytes");
        return data.Value.ToArray();
    }

    private static byte[] OptionalBytes(FalloutPluginRecord record, string signature, int length) =>
        Optional(record, signature) is null ? [] : Bytes(record, signature, length);

    private static FalloutFormKey? OptionalForm(FalloutPluginRecord record, string signature)
    {
        var data = Optional(record, signature);
        if (data is null) return null;
        var raw = UInt32(data.Value.Span, record, signature);
        return raw == 0 ? null : record.Plugin.AdjustFormId(raw);
    }

    private static FalloutFormKey RequiredForm(FalloutPluginRecord record, string signature) =>
        OptionalForm(record, signature) ?? throw Error(record, $"{signature} must identify a form");

    private static uint UInt32(ReadOnlySpan<byte> data, FalloutPluginRecord record, string signature) =>
        data.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(data) : throw Error(record, $"{signature} must contain four bytes");

    private static float Float(byte[] data, int offset, FalloutPluginRecord owner)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
        return float.IsFinite(value) ? value : throw Error(owner, "non-finite race dimension");
    }

    private static string? PathField(FalloutPluginRecord record, string signature, string prefix, bool required,
        IReadOnlyList<FalloutPluginSubrecord>? fields = null)
    {
        var data = Optional(record, signature, fields);
        if (data is null || data.Value.Span.SequenceEqual(new byte[] { 0 }))
            return required ? throw Error(record, $"{signature} path is absent") : null;
        return NormalizePath(Text(data.Value.Span, record), prefix);
    }

    private static string Text(ReadOnlySpan<byte> data, FalloutPluginRecord owner)
    {
        var end = data.IndexOf((byte)0);
        if (end != data.Length - 1) throw Error(owner, "path must have one trailing null");
        return new UTF8Encoding(false, true).GetString(data[..end]);
    }

    private static string NormalizePath(string path, string prefix)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) path = path[5..];
        if (!path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) path = prefix + "/" + path;
        if (path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Invalid owned resource path: {path}");
        return path;
    }

    private static bool Same(FalloutFormKey left, FalloutFormKey right) =>
        left.ObjectId == right.ObjectId && left.OwnerPlugin.Equals(right.OwnerPlugin, StringComparison.OrdinalIgnoreCase);
    private static InvalidDataException Error(FalloutPluginRecord record, string message) =>
        new($"{record.Signature} {record.FormKey}: {message}.");
}
