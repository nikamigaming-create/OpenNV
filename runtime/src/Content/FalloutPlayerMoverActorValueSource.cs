using System.Buffers.Binary;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerMoverActorValueSource(FalloutPlayerMoverDeltaSource Source,
    FalloutPlayerActorValueSource Player, ushort Speed, uint BodyConditionBits, string BodySettingHash,
    string Contract)
{
    private const string Rules = "source-FO3-mover-AV-baselines/v1;" +
        "selected-NPC-AV21-unsigned-current-ActorBaseData-speed-word-loaded-from-ACBS-offset14;" +
        "actual-loaded-Player-stats-template-owner-and-same-existing-base-modifier-map;" +
        "AV29-and30-share-real-default-body-condition-GMST-formula-return-before-modifier-pools;" +
        "AV72-independent-zero-base-no-formula-integer-type-and-separate-pool-truncations;" +
        "limb-and-ignore-limb-change-callbacks-required-before-mutating-source-command-values;" +
        "winning-setting-and-record-current-writers-not-rendered-speed-or-constructor-one;" +
        "cold-preserves-actual-four-base-and-modifier-pools-without-load-factory-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(this));
    internal static FalloutPlayerMoverActorValueSource Read(FalloutPlayerMoverDeltaSource source, FalloutPluginStack records)
    {
        source.Validate(); var player = FalloutPlayerActorValueSource.Read(records);
        var stats = records.GetEffective(player.StatsOwner); var data = stats.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (data is not [{ Data.Length: 24 }]) throw new InvalidDataException("Mover AV stats lost the actual selected ACBS extent.");
        var speed = BinaryPrimitives.ReadUInt16LittleEndian(data[0].Data.Span[14..]);
        var setting = records.EffectiveRecords("GMST").Where(record => record.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && Encoding.ASCII.GetString(field.Data.Span).TrimEnd('\0') == "fAVDDefaultBodyPartCondition")).ToArray();
        uint bodyBits; string declaration;
        if (setting is [var body])
        {
            var payload = body.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
            if (payload is not [{ Data.Length: 4 }]) throw new InvalidDataException("Body-condition GMST has no selected Float32 payload.");
            bodyBits = BinaryPrimitives.ReadUInt32LittleEndian(payload[0].Data.Span); declaration = RecordHash(body);
        }
        else if (setting.Length == 0 && records.OwnedSource is { } content)
        {
            var defaults = FalloutExecutableStringTable.ReadFloatDefaults(content.FalloutExecutablePath, gameSettingsOnly: true);
            if (!defaults.TryGetValue("fAVDDefaultBodyPartCondition", out var number))
                throw new NotSupportedException("Mover AV formula has no selected executable or winning-record GMST declaration.");
            bodyBits = BitConverter.SingleToUInt32Bits(number);
            declaration = FalloutAdvancementRuntimeReceipt.Hash(source.Identity + "\0fAVDDefaultBodyPartCondition\0" + bodyBits);
        }
        else throw new InvalidDataException("Mover AV formula has no unique winning body-condition GMST.");
        return new(source, player, speed, bodyBits, declaration,
            FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Source.Validate();
        if (Player is null || !FalloutAdvancementRuntimeReceipt.Digest(Player.PlayerSha256) || !FalloutAdvancementRuntimeReceipt.Digest(Player.StatsSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(BodySettingHash) || !float.IsFinite(BitConverter.UInt32BitsToSingle(BodyConditionBits)) ||
            Contract != FalloutAdvancementRuntimeReceipt.Hash(Rules))
            throw new InvalidDataException("Mover AV source omitted its actual assigned stats/GMST/default declarations.");
    }
    internal static string RecordHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
}
