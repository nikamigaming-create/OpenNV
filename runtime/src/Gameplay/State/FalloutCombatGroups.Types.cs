using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutCombatActorIdentity(FalloutFormKey Reference, FalloutFormKey Base,
    string ReferenceSignature, uint ReferenceFlags, string ReferenceSha256,
    string BaseSignature, uint BaseFlags, string BaseSha256, bool EnginePlayer)
{
    internal void Validate()
    {
        static bool Digest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            string.IsNullOrWhiteSpace(Base.OwnerPlugin) || Base.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            !Digest(ReferenceSha256) || !Digest(BaseSha256) ||
            (ReferenceFlags & 0x20) != 0 || (BaseFlags & 0x20) != 0 ||
            EnginePlayer && (ReferenceSignature != "ENGINE_PLAYER" || ReferenceFlags != 0 || BaseSignature != "NPC_") ||
            !EnginePlayer && !(ReferenceSignature == "ACHR" && BaseSignature == "NPC_" ||
                ReferenceSignature == "ACRE" && BaseSignature == "CREA"))
            throw new InvalidDataException("Combat-group actor identity has no exact actor owner.");
    }
}

internal sealed record FalloutCombatGroupPredicate(bool? Allowed, string Owner)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner)) throw new InvalidDataException("Combat-group predicate owner is absent.");
    }
}
internal sealed record FalloutCombatGroupDetection(int? Level, string Owner)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner)) throw new InvalidDataException("Combat-group detection owner is absent.");
    }
}

internal enum FalloutCombatGroupTransitionKind
{
    ActorTarget, AssistanceJoin, CompanionJoin, PlayerTargetCandidate,
    SourceTargetRemoval, SourceMemberRemoval, PlayerSplit, ActorRetirement,
    OpaqueTargetChange, OpaqueTargetEnd, OpaqueTeammateChange, RetirementUpdate,
}
internal sealed record FalloutCombatGroupFailure(ulong Sequence, FalloutCombatGroupTransitionKind Kind,
    FalloutFormKey Actor, FalloutFormKey? Target, string Owner, string Error);
internal sealed record FalloutCombatGroupMembership(FalloutFormKey Actor, ulong Group);
internal sealed record FalloutCombatGroupLists(ulong Identity,
    IReadOnlyList<FalloutFormKey> Members, IReadOnlyList<FalloutFormKey> Targets);
internal sealed record FalloutCombatGroupTargetReceipt(FalloutFormKey Actor,
    FalloutFormKey? Target, ulong Sequence, FalloutCombatGroupTransitionKind Kind);
internal sealed record FalloutCombatGroupAdmissionReceipt(ulong Sequence, int DetectionLevel,
    string Owner, ulong? RemovedSequence = null);
internal sealed record FalloutCombatGroupCandidateReceipt(FalloutFormKey Target, ulong Sequence,
    int? DetectionLevel, string Owner, FalloutCombatGroupAdmissionReceipt? Admission);
internal sealed record FalloutCombatGroupListRemoval(ulong Group, FalloutFormKey Actor,
    bool Target, ulong Sequence);
internal sealed record FalloutCombatGroupsSnapshot(string Contract, string StackIdentity,
    FalloutFormKey Player, ulong NextGroupIdentity, ulong LastSequence,
    IReadOnlyList<FalloutCombatActorIdentity> Actors, IReadOnlyList<FalloutCombatGroupLists> Groups,
    IReadOnlyList<FalloutCombatGroupMembership> Memberships,
    IReadOnlyList<FalloutCombatGroupTargetReceipt> CurrentTargets,
    IReadOnlyList<FalloutCombatGroupCandidateReceipt> PlayerCandidates,
    IReadOnlyList<FalloutCombatGroupListRemoval> Removals,
    IReadOnlyList<FalloutCombatGroupFailure> Failures, float PlayerCombatSeconds,
    bool PlayerCombatFlag, int IncomingMembersAtUpdate, ulong LastPlayerUpdateSequence, string? OwnerFailure);

internal sealed class FalloutCombatGroupInputs(
    Func<FalloutFormKey, FalloutCombatActorIdentity> actor,
    Func<FalloutFormKey, FalloutFormKey, FalloutCombatGroupPredicate> targetPredicate,
    Func<FalloutFormKey, bool> playerTeammate,
    Func<FalloutFormKey, FalloutCombatGroupDetection> playerDetection,
    Func<ulong, FalloutFormKey, FalloutCombatGroupPredicate> targetRetirement)
{
    internal FalloutCombatActorIdentity Actor(FalloutFormKey reference) => actor(reference);
    internal FalloutCombatGroupPredicate TargetPredicate(FalloutFormKey member, FalloutFormKey target) => targetPredicate(member, target);
    internal bool PlayerTeammate(FalloutFormKey reference) => playerTeammate(reference);
    internal FalloutCombatGroupDetection PlayerDetection(FalloutFormKey target) => playerDetection(target);
    internal FalloutCombatGroupPredicate TargetRetirement(ulong group, FalloutFormKey target) => targetRetirement(group, target);
}
