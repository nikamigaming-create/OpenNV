using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Authoritative gameplay state. Native pose/physics publication is an explicit
// lease of this owner; it never infers sleep from sitting or Unconscious.
internal sealed class FalloutPlayerPhysicalActivity
{
    internal FalloutPlayerPhysicalSource Source { get; }
    internal bool Published { get; private set; }
    internal bool Sleeping { get; private set; }
    internal FalloutPlayerFurnitureKind? FurnitureKind { get; private set; }
    internal FalloutPlayerFurniturePhase FurniturePhase { get; private set; }
    internal FalloutPlayerKnockdownPhase KnockdownPhase { get; private set; }
    internal long Attempt { get; private set; }
    internal FalloutPlayerPhysicalFailure? Failure { get; private set; }
    internal bool HasPhysicalMotion => FurniturePhase != FalloutPlayerFurniturePhase.None || KnockdownPhase != FalloutPlayerKnockdownPhase.Upright;

    internal FalloutPlayerPhysicalActivity(FalloutPlayerPhysicalSource source,
        FalloutPlayerPhysicalSnapshot? restore = null)
    {
        source.Validate(); Source = source;
        if (restore is null) return;
        restore.Validate(); restore.Source.RequireCurrent(source);
        Sleeping = restore.Sleeping; Attempt = restore.Attempt; Failure = restore.Failure;
        FurnitureKind = restore.Furniture?.Kind;
        FurniturePhase = restore.Furniture?.Phase ?? FalloutPlayerFurniturePhase.None;
        KnockdownPhase = restore.Knockdown?.Phase ?? FalloutPlayerKnockdownPhase.Upright;
        // Cold publication must bind the actual source body and continuation.
    }
    internal void PublishNativeOwner()
    {
        if (Published) throw new InvalidOperationException("Physical player native lease is already published.");
        Published = true;
    }
    internal void RetireNativeOwner() => Published = false;
    internal void RequireHealthy()
    {
        if (!Published) throw new NotSupportedException("Player physical state has no current native/source publication.");
        if (Failure is { } fault) throw new InvalidOperationException($"Player physical operation {fault.Attempt}:{fault.Operation} failed: {fault.Error}");
    }
    internal void Execute(string operation, Action action)
    {
        RequireHealthy(); ArgumentException.ThrowIfNullOrWhiteSpace(operation); ArgumentNullException.ThrowIfNull(action);
        Attempt = checked(Attempt + 1);
        try { action(); }
        catch (Exception error) when (Ordinary(error)) { Retain(operation, error); throw; }
    }
    internal void Retain(string operation, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Attempt == 0) Attempt = 1;
        Failure ??= new(Attempt, operation, string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message,
            FurniturePhase, KnockdownPhase);
    }
    internal void CommitFurniture(FalloutPlayerFurnitureKind kind, FalloutPlayerFurniturePhase phase)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(phase) || KnockdownPhase != FalloutPlayerKnockdownPhase.Upright)
            throw new InvalidOperationException("Furniture and knockdown cannot own the same physical player.");
        if (Sleeping && phase != FalloutPlayerFurniturePhase.Occupied)
            throw new InvalidOperationException("A source sleep clock must retire before the player can leave its bed.");
        FurnitureKind = phase == FalloutPlayerFurniturePhase.None ? null : kind;
        FurniturePhase = phase;
    }
    internal void CommitKnockdown(FalloutPlayerKnockdownPhase phase)
    {
        if (!Enum.IsDefined(phase) || FurniturePhase != FalloutPlayerFurniturePhase.None || Sleeping)
            throw new InvalidOperationException("Knockdown cannot bypass the player's current furniture or sleep owner.");
        KnockdownPhase = phase;
    }
    // The actual sleep/wait consumer owns this independent flag. Occupying a
    // bed alone never calls it. Its committed time/effect prefix is external.
    internal void BeginSleepClock(Action begin)
    {
        ArgumentNullException.ThrowIfNull(begin);
        if (Sleeping) throw new InvalidOperationException("A source player sleep clock is already active.");
        // Original IsPCSleeping reads its independent player flag. Actor bed
        // pose/GetSleeping does not admit or substitute for this time owner.
        Execute("begin-source-sleep-clock", () => { Sleeping = true; begin(); });
    }
    internal void EndSleepClock(Action end)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (!Sleeping) throw new InvalidOperationException("No source player sleep clock is active.");
        Execute("end-source-sleep-clock", () => { end(); Sleeping = false; });
    }
    internal void CommitSleepFlag(bool sleeping)
    {
        if (!Published || Failure is not null)
            throw new InvalidOperationException("Source sleep flag has no healthy published physical owner.");
        Sleeping = sleeping;
    }
    internal int SleepingState
    {
        get
        {
            RequireHealthy();
            return FurnitureKind != FalloutPlayerFurnitureKind.Sleeping ? 0 : FurniturePhase switch
            {
                FalloutPlayerFurniturePhase.Approaching => throw new NotSupportedException("Source bed loading has a distinct process producer; NAVM approach does not publish it."),
                FalloutPlayerFurniturePhase.Entering => 2,
                FalloutPlayerFurniturePhase.Occupied => 3,
                FalloutPlayerFurniturePhase.Exiting => 4,
                _ => 0,
            };
        }
    }
    internal int KnockedState { get { RequireHealthy(); return KnockdownPhase == FalloutPlayerKnockdownPhase.Upright ? 0 : 1; } }
    internal bool IsPcSleeping { get { RequireHealthy(); return Sleeping; } }
    internal FalloutAdvancementActivityObservation Observe(FalloutAdvancementActivityFact fact)
    {
        var identity = $"physical-player:{Source.Identity}:attempt{Attempt}:furniture{FurniturePhase}:knockdown{KnockdownPhase}";
        if (!Published || Failure is not null)
            return new(FalloutAdvancementActivityState.Unowned, identity + ":" + (Failure?.Error ?? "native-publication-absent"));
        var satisfied = fact switch
        {
            FalloutAdvancementActivityFact.PlayerAwake => !Sleeping,
            FalloutAdvancementActivityFact.PlayerUpright => KnockdownPhase == FalloutPlayerKnockdownPhase.Upright,
            _ => throw new ArgumentOutOfRangeException(nameof(fact)),
        };
        return new(satisfied ? FalloutAdvancementActivityState.Satisfied : FalloutAdvancementActivityState.Held, identity);
    }
    internal static bool Ordinary(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
