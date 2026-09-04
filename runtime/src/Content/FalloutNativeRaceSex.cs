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
    string EyesEditorId);

internal sealed record FalloutNativeRaceSexContract(
    FalloutFormKey Player,
    FalloutNativeRaceSexSelection Male,
    FalloutNativeRaceSexSelection Female,
    bool InitialFemale)
{
    internal FalloutNativeRaceSexSelection Initial => ForSex(InitialFemale);

    internal FalloutNativeRaceSexSelection ForSex(bool female) => female ? Female : Male;

    internal bool Contains(FalloutNativeRaceSexSelection selection) =>
        selection == Male || selection == Female;
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
        return new FalloutNativeRaceSexContract(player.FormKey, male, female, initialFemale);
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
            throw new InvalidDataException($"Native HAIR {hair.FormKey} DATA layout is unsupported.");
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
