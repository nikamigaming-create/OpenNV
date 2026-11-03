using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Content;

internal enum FalloutKarmaAlignment { Good, Neutral, Evil, VeryGood, VeryEvil }
internal sealed record FalloutKarmaSource(FalloutFormKey Owner, string Winner, string Sha256, int Initial)
{
    internal static FalloutKarmaSource Read(FalloutPluginStack records, FalloutFormKey actorBase, FalloutActorTemplateSelection? selection = null)
    {
        // Karma belongs to the source traits template, independently of stats.
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actorBase), 1, selection);
        if (source.Signature is not ("NPC_" or "CREA")) throw new InvalidDataException("Karma source is not an actor base.");
        var rows = source.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (rows.Length != 1 || rows[0].Data.Length != 24) throw new InvalidDataException("Karma source has invalid ACBS extent.");
        var initial = FalloutActorValueCommands.Integer(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rows[0].Data.Span[16..])));
        return new(source.FormKey, source.Plugin.Name, Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant(), initial);
    }
}

internal sealed record FalloutKarmaReward(int Amount, FalloutHudEventKind Notice);
internal static class FalloutKarma
{
    internal static FalloutKarmaAlignment Alignment(FalloutPluginStack records, float karma)
    {
        if (!float.IsFinite(karma)) throw new InvalidDataException("Karma alignment has no finite current value.");
        float Setting(string name) => FalloutGameSettingFloats.Read(records, name);
        if (karma <= Setting("fAlignEvilMaxKarma"))
            return karma <= Setting("fAlignVeryEvilMaxKarma") ? FalloutKarmaAlignment.VeryEvil : FalloutKarmaAlignment.Evil;
        if (karma >= Setting("fAlignGoodMinKarma"))
            return karma >= Setting("fAlignVeryGoodMinKarma") ? FalloutKarmaAlignment.VeryGood : FalloutKarmaAlignment.Good;
        return FalloutKarmaAlignment.Neutral;
    }

    internal static FalloutKarmaReward Reward(FalloutPluginStack records, int current, int requested)
    {
        var lost = requested < 0;
        var amount = requested;
        // The command compares the native signed-32 addition, not a widened sum.
        var projected = unchecked(current + requested);
        var minimum = unchecked((int)FalloutGameSettingIntegers.Read(records, "iKarmaMin"));
        var maximum = unchecked((int)FalloutGameSettingIntegers.Read(records, "iKarmaMax"));
        if (lost && projected < minimum) amount = unchecked(minimum - current);
        if (!lost && projected > maximum) amount = unchecked(maximum - current);
        var threshold = unchecked((int)FalloutGameSettingIntegers.Read(records, "iKarmaChangeThreshold"));
        var kind = lost
            ? amount < unchecked(-threshold) ? FalloutHudEventKind.KarmaMajorLost : FalloutHudEventKind.KarmaMinorLost
            : amount >= threshold ? FalloutHudEventKind.KarmaMajorGained : FalloutHudEventKind.KarmaMinorGained;
        return new(amount, kind);
    }

    internal static (string Text, string Image, string Sound) Notice(FalloutHudEventKind kind) => kind switch
    {
        FalloutHudEventKind.KarmaMajorLost => ("sKarmaMajorLost", "sKarmaMajorLostImage", "UIKarmaDown"),
        FalloutHudEventKind.KarmaMinorLost => ("sKarmaMinorLost", "sKarmaMinorLostImage", "UIKarmaDown"),
        FalloutHudEventKind.KarmaMajorGained => ("sKarmaMajorGained", "sKarmaMajorGainedImage", "UIKarmaUp"),
        FalloutHudEventKind.KarmaMinorGained => ("sKarmaMinorGained", "sKarmaMinorGainedImage", "UIKarmaUp"),
        _ => throw new InvalidDataException("HUD event is not a source karma notice."),
    };
    internal static bool IsNotice(FalloutHudEventKind kind) => kind is FalloutHudEventKind.KarmaMajorLost or
        FalloutHudEventKind.KarmaMinorLost or FalloutHudEventKind.KarmaMajorGained or FalloutHudEventKind.KarmaMinorGained;
}
