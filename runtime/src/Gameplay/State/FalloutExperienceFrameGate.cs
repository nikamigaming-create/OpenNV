using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

[Flags]
internal enum FalloutExperienceSourceFlags : uint
{
    Meter = 1, LevelText = 2, Waiting = 4, WaitingMeter = Meter | Waiting, Completed = 8,
}
internal sealed record FalloutExperienceFrameGateSnapshot(string Contract, bool Ready,
    FalloutExperienceSourceFlags Flags, ulong ResetGeneration, ulong? CompletedSequence,
    int? CompletedLevel);
internal sealed record FalloutExperienceLevelIntroInput(int? CombatGroupTargetCount, string Owner)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || CombatGroupTargetCount is < 0)
            throw new InvalidDataException("XP level-intro source input is invalid.");
    }
}

// The original HUD getter observes its own producer flag. Queue emptiness,
// notification registration and an empty draw cannot write this flag.
internal sealed class FalloutExperienceFrameGate
{
    private readonly FalloutAdvancementFrameDeclaration _source;
    private bool _ready;
    private FalloutExperienceSourceFlags _flags;
    private ulong _resetGeneration;
    private ulong? _completedSequence;
    private int? _completedLevel;
    internal bool Ready => _ready;
    internal FalloutExperienceSourceFlags Flags => _flags;
    internal string Contract => _source.Contract;

    internal FalloutExperienceFrameGate(FalloutAdvancementFrameDeclaration source,
        FalloutExperienceFrameGateSnapshot? restore = null)
    {
        source.Validate(); _source = source;
        _ready = source.InitialExperienceReady;
        _flags = (FalloutExperienceSourceFlags)source.InitialExperienceFlags;
        if (restore is null) return;
        Validate(restore);
        if (restore.Contract != source.Contract)
            throw new InvalidDataException("Cold experience frame flag differs from the selected source.");
        _ready = restore.Ready; _flags = restore.Flags;
        _resetGeneration = restore.ResetGeneration;
        _completedSequence = restore.CompletedSequence; _completedLevel = restore.CompletedLevel;
    }

    internal void BeginMeter()
    {
        if (_ready || (_flags & FalloutExperienceSourceFlags.Meter) == 0)
            throw new InvalidOperationException("XP meter cannot bypass its original frame flag arbitration.");
    }

    internal bool AdmitLevelText(FalloutExperienceLevelIntroInput input)
    {
        input.Validate();
        if (_ready || (_flags & FalloutExperienceSourceFlags.Completed) != 0)
            throw new InvalidOperationException("A completed source level intro cannot publish again before its actual reset.");
        if (input.CombatGroupTargetCount is not { } count)
            throw new NotSupportedException("XP level intro has no living source combat-group target count: " + input.Owner);
        if (count != 0)
        {
            _flags |= FalloutExperienceSourceFlags.Waiting;
            return false;
        }
        _flags = FalloutExperienceSourceFlags.LevelText;
        return true;
    }

    internal void LevelTextHidden(ulong sequence, int level)
    {
        if (sequence == 0 || level <= 1 || _ready || _flags != FalloutExperienceSourceFlags.LevelText)
            throw new InvalidOperationException("XP completion requires the current original level-text producer.");
        _ready = true; _flags = FalloutExperienceSourceFlags.Completed;
        _completedSequence = sequence; _completedLevel = level;
    }

    internal void CompletedMenuSubmitted(FalloutLevelUpMenuSession menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        // The driver separately admits the actual retained request before
        // finishing it. A native release, cancellation or queued frame cannot
        // supply this original menu-submit reset.
        if (!menu.Completed || menu.Error is not null || !menu.HasExperienceFrameSource(_source) || !_ready ||
            _flags != FalloutExperienceSourceFlags.Completed || _completedLevel != menu.Level)
            throw new InvalidOperationException("XP frame reset does not join the actual completed level-up menu.");
        _ready = false; _flags = FalloutExperienceSourceFlags.Meter;
        _completedSequence = null; _completedLevel = null;
        _resetGeneration = checked(_resetGeneration + 1);
    }

    internal FalloutExperienceFrameGateSnapshot Capture() => new(_source.Contract, _ready,
        _flags, _resetGeneration, _completedSequence, _completedLevel);

    internal static void Validate(FalloutExperienceFrameGateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!FalloutAdvancementRuntimeReceipt.Digest(state.Contract) ||
            state.Flags is not (FalloutExperienceSourceFlags.Meter or FalloutExperienceSourceFlags.LevelText or
                FalloutExperienceSourceFlags.WaitingMeter or
                FalloutExperienceSourceFlags.Completed) ||
            state.Ready != (state.Flags == FalloutExperienceSourceFlags.Completed) ||
            state.Ready != (state.CompletedSequence is not null) ||
            state.Ready != (state.CompletedLevel is not null) ||
            state.CompletedSequence is 0 || state.CompletedLevel is <= 1)
            throw new InvalidDataException("Experience frame flag continuation is incomplete or fabricated.");
    }
}
