using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCreatureOutgoingIdle(string Owner, FalloutActorPackageIdleAnimation Animation,
    FalloutIdleDeactivationSnapshot Ease, FalloutFurnitureClipSnapshot Other, float? AccumulationTime = null)
{
    internal void Validate(FalloutPluginStack? records = null)
    {
        if (Owner is not ("script" or "ambient" or "eat") || Animation is null || Ease is null || Other is null ||
            AccumulationTime is { } time && !float.IsFinite(time))
            throw new InvalidDataException("Outgoing creature idle has no independent sequence/ease owner.");
        Animation.Validate(allowCompleted: true); Ease.Validate(); Other.Validate();
        if (Ease.DurationSeconds <= 0 || Ease.ElapsedSeconds >= Ease.DurationSeconds)
            throw new InvalidDataException("Outgoing creature idle already retired its execution slot.");
        if (records is not null)
        {
            Animation.ValidateSource(records, allowCompleted: true);
            var record = records.GetEffective(Other.Idle);
            if (record.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(record).Equals(Other.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
                !FalloutActorIdleSource.Resolve(records, record).AnimationPath.Equals(Other.Resource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Outgoing creature transition differs from its incoming source IDLE.");
        }
    }
    internal FalloutCreatureOutgoingIdle Copy() => this with { Animation = Animation.Copy() };
}

internal sealed record FalloutCreatureIdleRequests(FalloutActorIdleRequestSnapshot? Queued,
    IReadOnlyList<FalloutCreatureOutgoingIdle> Outgoing, FalloutIdleDeactivationSnapshot? IncomingEase = null,
    FalloutFurnitureClipSnapshot? IncomingFrom = null)
{
    internal void Validate(FalloutPluginStack? records = null)
    {
        if (Outgoing is null || Outgoing.Any(value => value is null) || Queued is { State: not 1 } ||
            Queued is { Owner: not ("script" or "eat") } || (IncomingEase is null) != (IncomingFrom is null) ||
            IncomingEase is not null && Outgoing.Count == 0)
            throw new InvalidDataException("Creature queued/current/outgoing request owners disagree.");
        Queued?.Validate(records); IncomingEase?.Validate(); IncomingFrom?.Validate();
        if (IncomingFrom is { } from)
        {
            if (!Outgoing.Any(value => value.Animation.Idle == from.Idle && value.Animation.IdleSha256.Equals(from.IdleSha256, StringComparison.OrdinalIgnoreCase) &&
                value.Animation.Resource.Equals(from.Resource, StringComparison.OrdinalIgnoreCase) &&
                value.Animation.Sha256.Equals(from.Sha256, StringComparison.OrdinalIgnoreCase) && value.Ease == IncomingEase))
                throw new InvalidDataException("Creature incoming ease lost its matching outgoing source/clock.");
            if (records is not null)
            {
                var source = records.GetEffective(from.Idle);
                if (source.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(source).Equals(from.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
                    !FalloutActorIdleSource.Resolve(records, source).AnimationPath.Equals(from.Resource, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Creature incoming ease differs from its outgoing source IDLE.");
            }
        }
        if (IncomingEase is { } ease && (ease.DurationSeconds <= 0 || ease.ElapsedSeconds >= ease.DurationSeconds))
            throw new InvalidDataException("Creature incoming ease has already retired its slot.");
        foreach (var value in Outgoing) value.Validate(records);
    }
    internal FalloutCreatureIdleRequests Copy() => this with
    { Queued = Queued?.Copy(), Outgoing = Outgoing.Select(value => value.Copy()).ToArray() };
}
