using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// This process field records request publication, independently of the selected
// marker IDLE, current animation, outgoing layers and successful activation.
// An absent owner is unknown; an explicit empty owner is a new process.
internal sealed record FalloutActorLastIdleRequest(FalloutFormKey? Idle, string? Sha256, bool ProcessCreated = true)
{
    internal static FalloutActorLastIdleRequest Empty { get; } = new(null, null);
    internal static FalloutActorLastIdleRequest NeverCreated { get; } = new(null, null, false);

    internal static FalloutActorLastIdleRequest Requested(FalloutPluginStack records, FalloutFormKey idle)
    {
        var source = records.GetEffective(idle);
        if (source.Signature != "IDLE") throw new InvalidDataException("Idle request history requires an IDLE argument.");
        return new(idle, Convert.ToHexString(SHA256.HashData(source.ReadData())));
    }

    internal void Validate(FalloutPluginStack? records = null)
    {
        if (Idle is not { } idle)
        {
            if (Sha256 is not null) throw new InvalidDataException("Empty idle request history has a source hash.");
            return;
        }
        if (!ProcessCreated || string.IsNullOrWhiteSpace(idle.OwnerPlugin) || idle.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Idle request history has an invalid source identity.");
        if (records is not null && !Requested(records, idle).Sha256!.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Idle request history differs from its winning IDLE source.");
    }
}

internal sealed partial class FalloutReferenceInstance
{
    internal FalloutActorLastIdleRequest? LastIdleRequest { get; set; }
    internal Func<FalloutActorLastIdleRequest?>? QueryLastIdleRequest { get; set; }
    internal bool MayInitializeIdleRequestHistory(bool hasNativeOwner) =>
        !hasNativeOwner && (LastIdleRequest is { ProcessCreated: false } || !HasRestoredSnapshot && LastIdleRequest is null);

    internal void InitializeIdleRequestHistory(bool newProcess)
    {
        // Restoring an older snapshot cannot establish an absent process field.
        if (newProcess && (LastIdleRequest is { ProcessCreated: false } || !HasRestoredSnapshot && LastIdleRequest is null))
            LastIdleRequest = FalloutActorLastIdleRequest.Empty;
    }

    internal void RecordIdleRequest(FalloutPluginStack records, FalloutFormKey idle) =>
        LastIdleRequest = FalloutActorLastIdleRequest.Requested(records, idle);
}

internal sealed partial class FalloutReferenceWorld
{
    internal float IsLastIdlePlayed(FalloutCondition condition, FalloutFormKey caller, FalloutFormKey? target = null)
    {
        if (condition.Kind != Function.IsLastIdlePlayed || condition.Argument2 != 0)
            throw new InvalidDataException("Last-idle condition differs from the source IsLastIdlePlayed declaration.");
        var idle = condition.FormArgument1;
        if (records.GetEffective(idle).Signature != "IDLE")
            throw new InvalidDataException("IsLastIdlePlayed requires an IDLE argument.");
        var subject = FalloutAiPackages.ConditionSubject(condition, caller, target);
        return IsLastIdlePlayed(subject, idle);
    }

    internal float IsLastIdlePlayed(FalloutFormKey subject, FalloutFormKey idle)
    {
        if (records.GetEffective(idle).Signature != "IDLE") throw new InvalidDataException("IsLastIdlePlayed requires an IDLE argument.");
        if (records.RuntimeFormId(subject) == 0x14)
            throw new NotSupportedException("Player last-idle request history has no process owner.");
        if (records.GetEffective(subject).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("IsLastIdlePlayed subject is not a placed actor.");
        // A retained receipt does not identify the current native process tier.
        // Low-process visibility and player history need their own owners.
        var query = Get(subject).QueryLastIdleRequest ??
            throw new NotSupportedException("IsLastIdlePlayed has unknown native process visibility.");
        var history = query() ??
            throw new NotSupportedException("IsLastIdlePlayed has unknown process request history.");
        if (!history.ProcessCreated) throw new NotSupportedException("IsLastIdlePlayed has no initialized native process.");
        // Publication and cold restore validate the immutable source receipt.
        // A bound owner reuses it without reading/hashing IDLE data per predicate.
        return history.Idle == idle ? 1 : 0;
    }
}
