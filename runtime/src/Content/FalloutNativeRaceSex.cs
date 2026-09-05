using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeRaceSexSelection(
    bool Female,
    uint RaceRuntimeFormId,
    string RaceEditorId,
    uint HairRuntimeFormId,
    string HairEditorId,
    uint EyesRuntimeFormId,
    string EyesEditorId,
    FalloutNativeFaceState? Face = null);

// The player's editable appearance retains original source Float32 bytes.
// A null value is the explicit legacy-save state: use the winning NPC fields.
internal sealed record FalloutNativeFaceState(byte[] SymmetricGeometry, byte[] AsymmetricGeometry,
    byte[] SymmetricTexture, byte[] HairColor, byte[] HairLength, uint[] HeadParts)
{
    internal void Validate()
    {
        foreach (var bytes in new[] { SymmetricGeometry, AsymmetricGeometry, SymmetricTexture })
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length % 4 != 0) throw new InvalidDataException("Player face coefficient extent is invalid.");
            for (var at = 0; at < bytes.Length; at += 4)
                if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)))) throw new InvalidDataException("Player face coefficient is non-finite.");
        }
        if (HairColor is not { Length: 4 } || HairLength is null || HairLength.Length is not (0 or 4) ||
            HairLength.Length == 4 && !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(HairLength)) ||
            HeadParts is null || HeadParts.Any(id => id == 0) || HeadParts.Distinct().Count() != HeadParts.Length)
            throw new InvalidDataException("Player face colour, length or head-part state is invalid.");
    }
}

internal sealed record FalloutNativeRaceSexPart(uint RuntimeFormId, string EditorId, string DisplayName,
    bool Male, bool Female);

internal sealed record FalloutNativeRaceSexRace(uint RuntimeFormId, string EditorId, string DisplayName,
    IReadOnlyList<FalloutNativeRaceSexPart> Hair, IReadOnlyList<FalloutNativeRaceSexPart> Eyes,
    uint MaleDefaultHair, uint FemaleDefaultHair)
{
    internal IReadOnlyList<FalloutNativeRaceSexPart> HairFor(bool female) => Hair.Where(part => female ? part.Female : part.Male).ToArray();
    internal IReadOnlyList<FalloutNativeRaceSexPart> EyesFor(bool female) => Eyes.Where(part => female ? part.Female : part.Male).ToArray();
}

internal sealed record FalloutNativeRaceSexContract(
    FalloutFormKey Player,
    FalloutNativeRaceSexSelection Male,
    FalloutNativeRaceSexSelection Female,
    bool InitialFemale,
    IReadOnlyList<FalloutNativeRaceSexRace> Races)
{
    internal FalloutNativeRaceSexSelection Initial => ForSex(InitialFemale);

    internal FalloutNativeRaceSexSelection ForSex(bool female) => female ? Female : Male;

    internal bool Contains(FalloutNativeRaceSexSelection selection)
    {
        var race = Races.SingleOrDefault(race => race.RuntimeFormId == selection.RaceRuntimeFormId && race.EditorId == selection.RaceEditorId);
        return race is not null && race.HairFor(selection.Female).Any(part => part.RuntimeFormId == selection.HairRuntimeFormId && part.EditorId == selection.HairEditorId) &&
            race.EyesFor(selection.Female).Any(part => part.RuntimeFormId == selection.EyesRuntimeFormId && part.EditorId == selection.EyesEditorId);
    }

    internal FalloutNativeRaceSexSelection Select(uint raceId, bool female, FalloutNativeRaceSexSelection current)
    {
        var race = Races.Single(race => race.RuntimeFormId == raceId);
        var hairs = race.HairFor(female); var eyes = race.EyesFor(female);
        var hair = hairs.SingleOrDefault(part => part.RuntimeFormId == current.HairRuntimeFormId) ??
            hairs.SingleOrDefault(part => part.RuntimeFormId == (female ? race.FemaleDefaultHair : race.MaleDefaultHair)) ??
            hairs.FirstOrDefault() ?? throw new NotSupportedException("Playable race has no eligible hair.");
        var eye = eyes.SingleOrDefault(part => part.RuntimeFormId == current.EyesRuntimeFormId) ??
            eyes.FirstOrDefault() ?? throw new NotSupportedException("Playable race has no eligible eyes.");
        return new(female, race.RuntimeFormId, race.EditorId, hair.RuntimeFormId, hair.EditorId, eye.RuntimeFormId, eye.EditorId);
    }
}

internal static class FalloutNativeRaceSexResolver
{
    private const uint FemaleActorFlag = 0x0000_0001;
    private const byte PlayableHairFlag = 0x01;
    private const byte NotMaleHairFlag = 0x02;
    private const byte NotFemaleHairFlag = 0x04;
    private const int ActorConfigurationBytes = 24;

    internal static FalloutNativeRaceSexContract Resolve(FalloutPluginStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var players = stack.EffectiveRecords("NPC_")
            .Where(record => ReadEditorId(record)
                .Equals("Player", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (players.Length != 1)
            throw new InvalidDataException(
                $"Native player identity must resolve to one winning NPC_; found {players.Length}.");
        var player = players[0];
        var actorConfiguration = Single(player, "ACBS");
        if (actorConfiguration.Length != ActorConfigurationBytes)
            throw new InvalidDataException("Native Player ACBS layout is unsupported.");
        var initialFemale =
            (BinaryPrimitives.ReadUInt32LittleEndian(actorConfiguration.Span) & FemaleActorFlag) != 0;
        var race = RequireLinked(stack, player, "RNAM", "RACE");
        var playerHair = RequireLinked(stack, player, "HNAM", "HAIR");
        var playerEyes = RequireLinked(stack, player, "ENAM", "EYES");
        var raceHair = FormList(race, "HNAM")
            .Select(key => RequireRecord(stack, key, "HAIR"))
            .ToArray();
        var raceEyes = FormList(race, "ENAM")
            .Select(key => RequireRecord(stack, key, "EYES"))
            .ToArray();
        if (raceHair.Length == 0 || raceEyes.Length == 0 ||
            !raceHair.Any(value => value.FormKey == playerHair.FormKey) ||
            !raceEyes.Any(value => value.FormKey == playerEyes.FormKey))
            throw new InvalidDataException(
                $"Native Player appearance is outside race {race.FormKey}'s live part lists.");

        var maleHair = PreferredHair(raceHair, playerHair, female: false);
        var femaleHair = PreferredHair(raceHair, playerHair, female: true);
        var eye = playerEyes;
        var male = Selection(stack, race, maleHair, eye, female: false);
        var female = Selection(stack, race, femaleHair, eye, female: true);
        var races = stack.EffectiveRecords("RACE").Where(IsPlayableRace).Select(record => Race(stack, record)).ToArray();
        var result = new FalloutNativeRaceSexContract(player.FormKey, male, female, initialFemale, races);
        Validate(result, result.Initial);
        return result;
    }

    private static bool IsPlayableRace(FalloutPluginRecord record)
    {
        var data = Single(record, "DATA");
        if (data.Length != 36) throw new InvalidDataException($"RACE {record.FormKey} DATA layout is unsupported.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(data.Span[32..]) & 1) != 0;
    }

    private static FalloutNativeRaceSexRace Race(FalloutPluginStack stack, FalloutPluginRecord race)
    {
        // RACE.DATA playability and its HNAM/ENAM lists define selectable parts.
        // HAIR/EYES DATA use independent playability and sex exclusion bits.
        // https://tes5edit.github.io/fopdoc/FalloutNV/Records/RACE.html
        FalloutNativeRaceSexPart[] Parts(string field, string signature) => FormList(race, field)
            .Select(key => RequireRecord(stack, key, signature)).Where(record => HairSupports(record, false) || HairSupports(record, true))
            .Select(record => new FalloutNativeRaceSexPart(stack.RuntimeFormId(record.FormKey), ReadEditorId(record), DisplayName(record),
                HairSupports(record, false), HairSupports(record, true))).ToArray();
        var defaults = Single(race, "DNAM");
        if (defaults.Length != 8) throw new InvalidDataException($"RACE {race.FormKey} default-hair layout is unsupported.");
        uint Default(int offset)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(defaults.Span[offset..]);
            return id == 0 ? 0 : stack.RuntimeFormId(RequireRecord(stack, race.Plugin.AdjustFormId(id), "HAIR").FormKey);
        }
        return new(stack.RuntimeFormId(race.FormKey), ReadEditorId(race), DisplayName(race), Parts("HNAM", "HAIR"), Parts("ENAM", "EYES"), Default(0), Default(4));
    }

    private static string DisplayName(FalloutPluginRecord record)
    {
        var bytes = Single(record, "FULL").Span;
        if (bytes.Length <= 1 || bytes[^1] != 0 || bytes[..^1].Contains((byte)0))
            throw new InvalidDataException($"Selectable {record.Signature} {record.FormKey} has no complete name.");
        return Encoding.Latin1.GetString(bytes[..^1]);
    }

    internal static void Validate(
        FalloutNativeRaceSexContract contract,
        FalloutNativeRaceSexSelection selection)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(selection);
        if (!contract.Contains(selection))
            throw new InvalidDataException(
                "Native campaign character identity differs from the live Player/RACE graph.");
        selection.Face?.Validate();
    }

    private static FalloutNativeRaceSexSelection Selection(
        FalloutPluginStack stack,
        FalloutPluginRecord race,
        FalloutPluginRecord hair,
        FalloutPluginRecord eyes,
        bool female) => new(
            female,
            stack.RuntimeFormId(race.FormKey),
            ReadEditorId(race),
            stack.RuntimeFormId(hair.FormKey),
            ReadEditorId(hair),
            stack.RuntimeFormId(eyes.FormKey),
            ReadEditorId(eyes));

    private static FalloutPluginRecord PreferredHair(
        IReadOnlyList<FalloutPluginRecord> candidates,
        FalloutPluginRecord playerDefault,
        bool female)
    {
        var allowed = candidates.Where(value => HairSupports(value, female)).ToArray();
        if (allowed.Length == 0)
            throw new InvalidDataException(
                $"Native Player race has no playable {(female ? "female" : "male")} hair.");
        return allowed.FirstOrDefault(value => value.FormKey == playerDefault.FormKey) ?? allowed[0];
    }

    private static bool HairSupports(FalloutPluginRecord hair, bool female)
    {
        var data = Single(hair, "DATA");
        if (data.Length != 1)
            throw new InvalidDataException($"Native {hair.Signature} {hair.FormKey} DATA layout is unsupported.");
        var flags = data.Span[0];
        return (flags & PlayableHairFlag) != 0 &&
            (female ? (flags & NotFemaleHairFlag) == 0 : (flags & NotMaleHairFlag) == 0);
    }

    private static FalloutPluginRecord RequireLinked(
        FalloutPluginStack stack,
        FalloutPluginRecord owner,
        string signature,
        string expectedType) =>
        RequireRecord(stack, RequiredForm(owner, signature), expectedType);

    private static FalloutPluginRecord RequireRecord(
        FalloutPluginStack stack,
        FalloutFormKey key,
        string signature)
    {
        var record = stack.GetEffective(key);
        if (record.Signature != signature)
            throw new InvalidDataException(
                $"Native appearance link {key} is {record.Signature}, expected {signature}.");
        return record;
    }

    private static FalloutFormKey RequiredForm(FalloutPluginRecord record, string signature)
    {
        var row = Single(record, signature);
        if (row.Length != sizeof(uint))
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} layout is unsupported.");
        return record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Span));
    }

    private static IReadOnlyList<FalloutFormKey> FormList(
        FalloutPluginRecord record,
        string signature)
    {
        var bytes = Single(record, signature);
        if (bytes.Length == 0 || bytes.Length % sizeof(uint) != 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} list is unsupported.");
        var result = new FalloutFormKey[bytes.Length / sizeof(uint)];
        for (var index = 0; index < result.Length; ++index)
            result[index] = record.Plugin.AdjustFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[(index * sizeof(uint))..]));
        return result;
    }

    private static ReadOnlyMemory<byte> Single(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} requires one {signature}; found {rows.Length}.");
        return rows[0].Data;
    }

    private static string ReadEditorId(FalloutPluginRecord record)
    {
        var bytes = Single(record, "EDID").Span;
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1 || end == 0 ||
            bytes[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} EDID is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(bytes[..end]);
    }
}
