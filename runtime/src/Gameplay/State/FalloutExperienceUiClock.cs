namespace OpenNV.Runtime.Gameplay.State;

internal readonly record struct FalloutExperienceClockReading(long TilesMilliseconds, long LevelMilliseconds);

// Original tile interpolation uses wall ticks. The level-text timestamp uses
// the separate pausable engine timer. This joins real pause notifications;
// neither GameTime nor an arbitrary frame delta owns either millisecond epoch.
internal sealed class FalloutExperienceUiClock
{
    private Guid _owner;
    private long? _lastWall;
    private long _level;
    private bool _paused;
    internal FalloutExperienceUiClock(long levelMilliseconds)
    {
        if (levelMilliseconds < 0 || levelMilliseconds > uint.MaxValue)
            throw new InvalidDataException("XP level-text timer exceeds its admitted source counter.");
        _level = levelMilliseconds;
    }
    internal Guid Attach(long wallMilliseconds, bool paused)
    {
        if (_owner != Guid.Empty || wallMilliseconds < 0)
            throw new InvalidOperationException("XP pause clock has no unique live owner/epoch.");
        _owner = Guid.NewGuid(); _lastWall = wallMilliseconds; _paused = paused; return _owner;
    }
    internal void Pause(Guid owner, long wallMilliseconds, bool paused)
    {
        Require(owner); Advance(wallMilliseconds); _paused = paused;
    }
    internal FalloutExperienceClockReading Read(long wallMilliseconds)
    {
        Require(_owner); Advance(wallMilliseconds); return new(wallMilliseconds, _level);
    }
    internal void Detach(Guid owner)
    {
        Require(owner); _owner = Guid.Empty; _lastWall = null;
    }
    private void Require(Guid owner)
    {
        if (owner == Guid.Empty || _owner != owner || _lastWall is null)
            throw new NotSupportedException("XP level-text clock lacks its actual current gameplay pause lease.");
    }
    private void Advance(long wallMilliseconds)
    {
        if (_lastWall is not { } last || wallMilliseconds < last)
            throw new InvalidDataException("XP clock moved backwards or lost its current process epoch.");
        if (!_paused) _level = checked(_level + wallMilliseconds - last);
        if (_level > uint.MaxValue)
            throw new NotSupportedException("XP original timer wrap requires its explicit source continuation owner.");
        _lastWall = wallMilliseconds;
    }
}
