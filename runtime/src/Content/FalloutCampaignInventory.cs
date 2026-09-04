using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutCampaignInventoryRequest(
    uint RuntimeFormId,
    string EditorId,
    string RecordType,
    int Count);

internal readonly record struct FalloutCampaignWeaponRequest(
    uint WeaponRuntimeFormId,
    uint? AmmoRuntimeFormId,
    int Damage,
    int ClipSize,
    int AmmoInMagazine,
    int? AnimationType);

internal sealed record FalloutCampaignItem(
    FalloutFormKey FormKey,
    uint RuntimeFormId,
    string EditorId,
    string RecordType,
    int Count,
    int? Value,
    float? Weight);

internal sealed record FalloutCampaignWeapon(
    FalloutCampaignItem Item,
    FalloutFormKey? Ammo,
    uint? AmmoRuntimeFormId,
    int Damage,
    int ClipSize,
    int AmmoInMagazine,
    int? AnimationType);

internal sealed record FalloutCampaignInventory(
    IReadOnlyList<FalloutCampaignItem> Items,
    FalloutCampaignWeapon? EquippedWeapon);

internal sealed record FalloutOpeningInventoryGrant(
    FalloutCampaignInventory Inventory,
    IReadOnlyList<uint> EquippedRuntimeFormIds);

internal static class FalloutOpeningInventoryGrantResolver
{
    private static readonly string[] ItemSignatures =
    [
        "ALCH",
        "AMMO",
        "ARMO",
        "IMOD",
        "KEYM",
        "MISC",
        "WEAP",
    ];

    internal static FalloutOpeningInventoryGrant Resolve(
        FalloutPluginStack stack,
        FalloutOpeningControlGraph stages,
        string questEditorId)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentException.ThrowIfNullOrWhiteSpace(questEditorId);
        if (!stages.Quests.TryGetValue(questEditorId, out var questStages))
            throw new KeyNotFoundException($"Native opening quest is absent: {questEditorId}.");
        var quest = questStages.Values.First().Quest;
        var sources = questStages.Values.Select(value => value.Source).ToList();
        foreach (var info in stack.EffectiveRecords("INFO"))
        {
            var links = info.ReadSubrecords().Where(value => value.Signature == "QSTI").ToArray();
            if (links.Length != 1 || links[0].Data.Length != sizeof(uint) ||
                info.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) != quest)
                continue;
            sources.AddRange(info.ReadSubrecords().Where(value => value.Signature == "SCTX")
                .Select(value => ReadSource(info, value.Data.Span)));
        }

        var additions = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var equipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var rawLine in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Split(';', 2)[0].Trim();
                var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 3 &&
                    tokens[0].Equals("player.additem", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(
                            tokens[2],
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var count) || count <= 0)
                        throw new InvalidDataException(
                            $"Native opening additem count is unsupported: {line}");
                    if (!additions.TryGetValue(tokens[1], out var counts))
                        additions.Add(tokens[1], counts = []);
                    counts.Add(count);
                }
                else if (tokens.Length == 2 &&
                    tokens[0].Equals("player.equipitem", StringComparison.OrdinalIgnoreCase))
                {
                    equipped.Add(tokens[1]);
                }
            }
        }
        if (additions.Count == 0 || equipped.Count == 0 ||
            equipped.Any(value =>
                !additions.TryGetValue(value, out var counts) || counts.Count != 1))
            throw new InvalidDataException(
                $"Native opening inventory commands for {questEditorId} are incomplete.");

        var records = ItemSignatures.SelectMany(stack.EffectiveRecords)
            .Select(record => (Record: record, EditorId: TryReadEditorId(record)))
            .Where(value => value.EditorId is not null && equipped.Contains(value.EditorId))
            .GroupBy(value => value.EditorId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Record).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var requests = new List<FalloutCampaignInventoryRequest>();
        foreach (var editorId in equipped.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!records.TryGetValue(editorId, out var matches) || matches.Length != 1)
                throw new InvalidDataException(
                    $"Native opening item {editorId} must resolve to one winning item record; " +
                    $"found {(matches?.Length ?? 0)}.");
            requests.Add(new FalloutCampaignInventoryRequest(
                stack.RuntimeFormId(matches[0].FormKey),
                editorId,
                matches[0].Signature,
                additions[editorId].Single()));
        }
        var inventory = FalloutCampaignInventoryResolver.Resolve(stack, requests, null);
        var equippedRuntimeIds = equipped.Select(editorId => inventory.Items.Single(item =>
                item.EditorId.Equals(editorId, StringComparison.OrdinalIgnoreCase)).RuntimeFormId)
            .Order()
            .ToArray();
        return new FalloutOpeningInventoryGrant(inventory, equippedRuntimeIds);
    }

    private static string ReadSource(FalloutPluginRecord record, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} SCTX is not ASCII.");
        var result = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        if (result.IndexOf('\0') >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} SCTX contains an embedded null.");
        return result;
    }

    private static string? TryReadEditorId(FalloutPluginRecord record)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
        if (rows.Length == 0)
            return null;
        if (rows.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} has {rows.Length} EDIDs.");
        var bytes = rows[0].Data.Span;
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1 ||
            bytes[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} EDID is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(bytes[..end]);
    }
}

internal static class FalloutCampaignInventoryResolver
{
    private const int SimpleItemDataBytes = 8;
    private const int ArmorItemDataBytes = 12;
    private const int WeaponDataBytes = 15;
    private const int WeaponDnamBytes = 204;
    private const int ItemValueOffset = 0;
    private const int SimpleItemWeightOffset = 4;
    private const int ArmorWeaponWeightOffset = 8;
    private const int WeaponDamageOffset = 12;
    private const int WeaponClipSizeOffset = 14;

    internal static FalloutCampaignInventory Resolve(
        FalloutPluginStack stack,
        IReadOnlyList<FalloutCampaignInventoryRequest> inventory,
        FalloutCampaignWeaponRequest? equippedWeapon)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(inventory);

        var resolved = new List<FalloutCampaignItem>(inventory.Count);
        var byRuntimeId = new Dictionary<uint, FalloutCampaignItem>();
        foreach (var request in inventory)
        {
            if (request.Count <= 0 || string.IsNullOrWhiteSpace(request.EditorId) ||
                request.RecordType.Length != FalloutPlugin.SignatureSize ||
                !request.RecordType.All(character => character is >= 'A' and <= 'Z'))
                throw new InvalidDataException(
                    $"Saved inventory request {request.RuntimeFormId:x8} is invalid.");
            var key = stack.RuntimeFormKey(request.RuntimeFormId);
            var record = stack.GetEffective(key);
            if (record.Signature != request.RecordType)
                throw Error(record,
                    $"record type differs from save: expected {request.RecordType}, found {record.Signature}");
            var subrecords = record.ReadSubrecords().ToArray();
            var editorId = ReadEditorId(record, subrecords);
            if (!editorId.Equals(request.EditorId, StringComparison.OrdinalIgnoreCase))
                throw Error(record,
                    $"editor ID differs from save: expected {request.EditorId}, found {editorId}");
            var economics = ReadEconomics(record, subrecords);
            var item = new FalloutCampaignItem(
                key,
                request.RuntimeFormId,
                editorId,
                record.Signature,
                request.Count,
                economics.Value,
                economics.Weight);
            if (!byRuntimeId.TryAdd(request.RuntimeFormId, item))
                throw new InvalidDataException(
                    $"Saved inventory contains duplicate FormID {request.RuntimeFormId:x8}.");
            resolved.Add(item);
        }

        FalloutCampaignWeapon? weapon = null;
        if (equippedWeapon is { } requestWeapon)
        {
            if (!byRuntimeId.TryGetValue(requestWeapon.WeaponRuntimeFormId, out var item) ||
                item.RecordType != "WEAP")
                throw new InvalidDataException(
                    $"Equipped weapon {requestWeapon.WeaponRuntimeFormId:x8} is absent from inventory.");
            var record = stack.GetEffective(item.FormKey);
            var subrecords = record.ReadSubrecords().ToArray();
            var data = RequiredSingle(record, subrecords, "DATA", WeaponDataBytes).Span;
            var damage = BinaryPrimitives.ReadUInt16LittleEndian(data[WeaponDamageOffset..]);
            var clipSize = data[WeaponClipSizeOffset];
            var ammoKey = ReadOptionalFormId(record, subrecords, "NAM0");
            uint? ammoRuntimeId = ammoKey is null ? null : stack.RuntimeFormId(ammoKey.Value);
            var animation = ReadOptionalUInt32(record, subrecords, "DNAM", WeaponDnamBytes);
            if (damage <= 0 || clipSize <= 0 || requestWeapon.AmmoInMagazine < 0 ||
                requestWeapon.AmmoInMagazine > clipSize ||
                requestWeapon.Damage != damage || requestWeapon.ClipSize != clipSize ||
                requestWeapon.AmmoRuntimeFormId != ammoRuntimeId ||
                requestWeapon.AnimationType != animation)
                throw Error(record, "saved equipped-weapon state differs from the live winning record");
            if (ammoKey is { } resolvedAmmo && stack.GetEffective(resolvedAmmo).Signature != "AMMO")
                throw Error(record, $"NAM0 target {resolvedAmmo} is not AMMO");
            weapon = new FalloutCampaignWeapon(
                item,
                ammoKey,
                ammoRuntimeId,
                damage,
                clipSize,
                requestWeapon.AmmoInMagazine,
                animation);
        }

        return new FalloutCampaignInventory(resolved, weapon);
    }

    private static (int? Value, float? Weight) ReadEconomics(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> subrecords)
    {
        var layout = record.Signature switch
        {
            "IMOD" or "KEYM" or "MISC" => (Bytes: SimpleItemDataBytes, WeightOffset: SimpleItemWeightOffset),
            "ARMO" => (Bytes: ArmorItemDataBytes, WeightOffset: ArmorWeaponWeightOffset),
            "WEAP" => (Bytes: WeaponDataBytes, WeightOffset: ArmorWeaponWeightOffset),
            _ => (Bytes: 0, WeightOffset: 0),
        };
        if (layout.Bytes == 0)
            return (null, null);
        var data = RequiredSingle(record, subrecords, "DATA", layout.Bytes).Span;
        var value = BinaryPrimitives.ReadInt32LittleEndian(data[ItemValueOffset..]);
        var weight = BinaryPrimitives.ReadSingleLittleEndian(data[layout.WeightOffset..]);
        if (value < 0 || !float.IsFinite(weight) || weight < 0.0f)
            throw Error(record, "DATA contains invalid item economics");
        return (value, weight);
    }

    private static string ReadEditorId(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> subrecords)
    {
        var data = RequiredSingle(record, subrecords, "EDID").Span;
        var terminator = data.IndexOf((byte)0);
        if (terminator != data.Length - 1 ||
            data[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, "EDID must be a null-terminated ASCII identifier");
        return Encoding.ASCII.GetString(data[..terminator]);
    }

    private static FalloutFormKey? ReadOptionalFormId(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> subrecords,
        string signature)
    {
        var matches = subrecords.Where(value => value.Signature == signature).ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length != 1 || matches[0].Data.Length != sizeof(uint))
            throw Error(record, $"must contain at most one {signature} of {sizeof(uint)} bytes");
        return record.Plugin.AdjustOptionalFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(matches[0].Data.Span));
    }

    private static int? ReadOptionalUInt32(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> subrecords,
        string signature,
        int expectedBytes)
    {
        var matches = subrecords.Where(value => value.Signature == signature).ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length != 1 || matches[0].Data.Length != expectedBytes)
            throw Error(record, $"must contain at most one {signature} of {expectedBytes} bytes");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(matches[0].Data.Span);
        if (value > int.MaxValue)
            throw Error(record, $"{signature} animation type is outside the supported range");
        return (int)value;
    }

    private static ReadOnlyMemory<byte> RequiredSingle(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> subrecords,
        string signature,
        int? expectedBytes = null)
    {
        var matches = subrecords.Where(value => value.Signature == signature).ToArray();
        if (matches.Length != 1 || expectedBytes is { } bytes && matches[0].Data.Length != bytes)
            throw Error(record,
                $"must contain exactly one {signature}" +
                (expectedBytes is { } size ? $" of {size} bytes" : string.Empty));
        return matches[0].Data;
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"Native {record.Signature} {record.FormKey} {detail}.");
}
