using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutDetectionActionSoundSnapshot(string Schema, FalloutFormKey Actor,
    int Level, float CountdownSeconds, long Revision);

// The source actor's action-noise level/timer are independent of audible voices,
// movement and CreateDetectionEvent. Initial values require the real actor
// construction/restore owner; this component cannot infer them from inactivity.
internal sealed class FalloutDetectionActionSound
{
    private const string Schema = "opennv-detection-action-sound/v1";
    private readonly FalloutFormKey _actor;
    private int _level;
    private float _countdown;
    private long _revision;

    internal int Level => _level;
    internal float CountdownSeconds => _countdown;

    internal FalloutDetectionActionSound(FalloutFormKey actor, int admittedInitialLevel,
        float admittedInitialCountdown, Func<FalloutFormKey, bool> selectedActor)
    {
        ArgumentNullException.ThrowIfNull(selectedActor);
        if (!selectedActor(actor) || !float.IsFinite(admittedInitialCountdown))
            throw new InvalidDataException("Detection action sound lacks a selected actor and admitted initial timer.");
        _actor = actor; _level = admittedInitialLevel; _countdown = admittedInitialCountdown;
    }

    // Call at an actual bound attack/fire/action setter. A radio loop, rendered
    // animation or combat-target change does not authorize this source write.
    internal void SetLevel(int sourceLevel, Func<float> readActorAlertSoundTimer)
    {
        ArgumentNullException.ThrowIfNull(readActorAlertSoundTimer);
        var timer = readActorAlertSoundTimer();
        if (!float.IsFinite(timer)) throw new InvalidDataException("Action sound requires a finite winning actor alert timer.");
        var revision = checked(_revision + 1);
        _level = sourceLevel; _countdown = timer; _revision = revision;
    }

    // The actor update checks the prior countdown. Crossing zero retains the
    // current level until the next actor update; do not clamp or expire early.
    // Paused world frames do not call this method. The source actor elapsed-time
    // owner must already account for its actual engine/menu time policy.
    internal void Advance(float sourceActorElapsedSeconds)
    {
        if (!float.IsFinite(sourceActorElapsedSeconds) || sourceActorElapsedSeconds < 0)
            throw new InvalidDataException("Detection action sound lacks a finite source actor clock.");
        var revision = checked(_revision + 1);
        if (_countdown > 0)
        {
            var next = (float)((double)_countdown - sourceActorElapsedSeconds);
            if (!float.IsFinite(next)) throw new NotSupportedException("Action sound countdown exceeds its finite Float32 domain.");
            _countdown = next;
        }
        else _level = 0;
        _revision = revision;
    }

    internal FalloutDetectionActionSoundSnapshot Capture() => new(Schema, _actor, _level, _countdown, _revision);

    internal void Restore(FalloutDetectionActionSoundSnapshot snapshot)
    {
        if (snapshot.Schema != Schema || snapshot.Actor != _actor || snapshot.Revision < 0 ||
            !float.IsFinite(snapshot.CountdownSeconds))
            throw new InvalidDataException("Saved action sound has incompatible source identity, timer or chronology.");
        _level = snapshot.Level; _countdown = snapshot.CountdownSeconds; _revision = snapshot.Revision;
    }

    internal static int SourceSoundLevel(uint authoredLevel, bool firstSourceModifier,
        bool secondSourceModifier, Func<string, int> readWinningInteger)
    {
        ArgumentNullException.ThrowIfNull(readWinningInteger);
        var level = firstSourceModifier ? 2u : authoredLevel;
        if (secondSourceModifier) level = 1;
        // These are the verified source enum branches. An unrecognized source
        // enum returns zero in this leaf; missing owner/state is never that enum.
        return level switch
        {
            0 => readWinningInteger("iSoundLevelLoud"),
            1 => readWinningInteger("iSoundLevelNormal"),
            2 => readWinningInteger("iSoundLevelSilent"),
            _ => 0,
        };
    }
}
