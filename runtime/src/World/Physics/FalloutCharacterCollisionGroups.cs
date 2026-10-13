namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutCharacterCollisionGroupsSnapshot(string Schema, string EngineSha256, ushort Counter);

// One campaign physics owner shares this counter across creation-info factories.
// Construction allocates; reading an existing phantom filter never allocates.
internal sealed class FalloutCharacterCollisionGroups : IDisposable
{
    internal const string Schema = "source-character-collision-groups/v1";
    internal const ushort InitialCounter = 10;
    internal const uint CharacterLayer = 30;
    private readonly string _engineSha256;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ushort _counter = InitialCounter;
    private bool _retired;

    internal Guid Identity { get; } = Guid.NewGuid();

    internal FalloutCharacterCollisionGroups(FalloutPhysicalConstructorInputs inputs,
        FalloutCharacterCollisionGroupsSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _engineSha256 = inputs.EngineSha256;
        if (restore is null) return;
        if (restore.Schema != Schema || restore.EngineSha256 != _engineSha256 || restore.Counter < InitialCounter)
            throw new InvalidDataException("Character collision-group state changed its selected source or counter domain.");
        _counter = restore.Counter;
    }

    internal uint AllocateCreationInfoFilter()
    {
        RequireCurrent();
        // The source stores the truncated increment back to the shared counter.
        // A zero wrap resets that shared counter to ten, as well as the result.
        _counter = unchecked((ushort)(_counter + 1));
        if (_counter == 0) _counter = InitialCounter;
        return ((uint)_counter << 16) | CharacterLayer;
    }

    internal FalloutCharacterCollisionGroupsSnapshot Capture()
    {
        RequireCurrent();
        return new(Schema, _engineSha256, _counter);
    }

    public void Dispose()
    {
        RequireThread();
        _retired = true;
    }

    private void RequireCurrent()
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(_retired, this);
    }

    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Character collision-group allocation escaped its actual physics owner thread.");
    }
}
