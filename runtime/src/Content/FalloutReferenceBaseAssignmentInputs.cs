using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferenceBaseAssignmentInputs(string Source, FalloutCombatActorIdentity Actor,
    FalloutReferencePositionDependency Winning, byte Kind, bool Destructible, uint ActorBaseFlags, bool ActorResponse)
{
    internal const uint DestructibleReferenceFlag = 0x1000000;
    internal static FalloutReferenceBaseAssignmentInputs Read(FalloutReferencePositionSource source,
        FalloutPluginStack records, FalloutCombatActorIdentity actor)
    {
        source.Validate(); actor.Validate(); var record = records.GetEffective(actor.Base);
        var digest = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
        if (record.IsDeleted || record.Signature != "NPC_" || record.Flags != actor.BaseFlags || digest != actor.BaseSha256)
            throw new InvalidDataException("Base assignment changed its actual winning NPC source.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(value => value.Signature == "TPLT" && (value.Data.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span) != 0)))
            throw new NotSupportedException("source-reference-Base-assignment-current-template-components-unbound");
        if (FalloutDestructible.Read(records, actor.Base) is not null)
            throw new NotSupportedException("source-reference-Base-assignment-nonnull-destructible-predicate-unbound");
        var stats = fields.Where(value => value.Signature == "ACBS").ToArray() is [var row] && row.Data.Length == 24 ? row :
            throw new InvalidDataException("Base assignment lacks exact current Actor base flags.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(stats.Data.Span);
        return new(source.Identity, actor, new(record.FormKey, record.Signature, record.Flags, digest), 0x2a, false, flags, (flags & 0x200) == 0);
    }
    internal void Validate(FalloutReferencePositionSource source, FalloutCombatActorIdentity actor)
    {
        if (Source != source.Identity || Actor != actor || Winning.Form != actor.Base || Winning.Signature != "NPC_" ||
            Winning.Flags != actor.BaseFlags || Winning.Sha256 != actor.BaseSha256 || Kind != 0x2a || Destructible ||
            ActorResponse != ((ActorBaseFlags & 0x200) == 0))
            throw new InvalidDataException("Base assignment inputs replaced the selected source component predicates.");
    }
}
