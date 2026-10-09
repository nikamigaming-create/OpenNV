using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutUnlockedContainerActivation(ulong InputSequence,
    FalloutFormKey Reference, FalloutFormKey Base, FalloutFormKey Actor, string ReferenceSha256, string BaseSha256);
internal sealed record FalloutInterfaceActivationFrameSnapshot(string Schema, string Contract,
    bool Pending, ulong FactorySequence, FalloutFormKey? LastFactoryReference, string? Failure);

// The second original advancement predicate belongs to interface activation,
// not to PlayerCharacter, generic menu absence or notification queue emptiness.
internal sealed class FalloutInterfaceActivationFrame : IDisposable
{
    internal const string Schema = "opennv-interface-activation-frame/v1";
    private readonly FalloutAdvancementFrameDeclaration _source;
    private readonly FalloutPluginStack _records;
    private readonly FalloutReferenceWorld _world;
    private Guid _session;
    private bool _pending, _disposed;
    private ulong _factorySequence;
    private ulong _lastInputSequence;
    private FalloutFormKey? _lastFactory;
    private FalloutUnlockedContainerActivation? _activation;
    private string? _failure;
    internal string? SaveBlocker => _failure is not null ? "interface-activation-frame-failure" :
        _session == Guid.Empty ? "interface-activation-frame-owner" :
        _pending || _activation is not null ? "unlocked-container-activation-continuation" : null;
    internal object State => new
    {
        source = _source.Contract,
        pending = _pending,
        activation = _activation,
        factorySequence = _factorySequence,
        lastFactory = _lastFactory?.ToString(),
        nativeOwned = _session != Guid.Empty,
        failure = _failure,
        saveBlocker = SaveBlocker
    };

    internal FalloutInterfaceActivationFrame(FalloutAdvancementFrameDeclaration source,
        FalloutPluginStack records, FalloutReferenceWorld world, FalloutInterfaceActivationFrameSnapshot? restore = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(world);
        _source = source; _records = records; _world = world;
        _pending = source.InitialContainerActivationPending;
        if (restore is null) return;
        Validate(restore);
        if (restore.Contract != source.Contract)
            throw new InvalidDataException("Cold interface frame differs from the selected original consumer.");
        // Current lockpick input/result continuation is a separate unowned
        // capability. Refuse it rather than defaulting its pending flag false.
        if (restore.Pending) throw new NotSupportedException("Cold unlocked-container activation has no current native lockpick continuation owner.");
        _factorySequence = restore.FactorySequence; _lastFactory = restore.LastFactoryReference;
    }

    internal Guid AttachSession()
    {
        RequireHealthy();
        if (_session != Guid.Empty) throw new InvalidOperationException("Interface frame already has its actual native session lease.");
        return _session = Guid.NewGuid();
    }

    internal FalloutAdvancementActivityObservation Observe()
    {
        return new(_disposed || _failure is not null || _session == Guid.Empty ? FalloutAdvancementActivityState.Unowned :
            _pending ? FalloutAdvancementActivityState.Held : FalloutAdvancementActivityState.Satisfied,
            $"original-interface-container-activation:{_source.Contract}:pending={_pending}:factory={_factorySequence}" +
            (_failure is null ? "" : ":" + _failure));
    }

    // A future genuine lockpick owner may enter this source transition after
    // its completed unlock, with the actual ordinary input sequence. There is
    // deliberately no general Activate/script/menu-registration call to it.
    internal void UnlockedContainerActivation(Guid session, FalloutUnlockedContainerActivation activation)
    {
        RequireSession(session);
        try
        {
            ArgumentNullException.ThrowIfNull(activation);
            if (_pending || _activation is not null || activation.InputSequence <= _lastInputSequence ||
                activation.Actor != _records.RuntimeFormKey(0x14))
                throw new InvalidOperationException("Unlocked-container activation does not own a new actual player input result.");
            var instance = _world.Get(activation.Reference);
            var reference = _records.GetEffective(activation.Reference);
            var source = _records.GetEffective(instance.Base);
            if (!_world.IsEnabled(activation.Reference) || instance.Deleted || instance.DeletePending || reference.Signature != "REFR" ||
                source.Signature != "CONT" || instance.Base != activation.Base || _world.GetLocked(activation.Reference) != 0 ||
                SourceHash(reference) != activation.ReferenceSha256 || SourceHash(source) != activation.BaseSha256)
                throw new InvalidDataException("Unlocked-container activation differs from its actual winning placed object and lock state.");
            _lastInputSequence = activation.InputSequence;
            _activation = activation; _pending = true;
        }
        catch (Exception error) { Retain(error); throw; }
    }

    internal void EnterContainerFactory(Guid session, FalloutFormKey reference)
    {
        RequireSession(session);
        // Original factory entry clears the shared flag BEFORE parameter,
        // source-menu or native attachment admission. Retain that prefix on a
        // later refusal; a different request cannot claim the saved activation.
        _pending = false; _factorySequence = checked(_factorySequence + 1); _lastFactory = reference;
        try
        {
            if (_activation is { } activation && activation.Reference != reference)
                throw new InvalidOperationException("An unrelated container factory cleared a still-owned source activation.");
            _ = _world.Get(reference);
            _activation = null;
        }
        catch (Exception error) { Retain(error); throw; }
    }

    internal FalloutInterfaceActivationFrameSnapshot Capture()
    {
        RequireHealthy();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return new(Schema, _source.Contract, _pending, _factorySequence, _lastFactory, _failure);
    }

    internal static void Validate(FalloutInterfaceActivationFrameSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Schema != Schema || !FalloutAdvancementRuntimeReceipt.Digest(state.Contract) || state.Failure is not null ||
            (state.FactorySequence == 0) != (state.LastFactoryReference is null) ||
            state.LastFactoryReference is { } reference && (reference.ObjectId == 0 || string.IsNullOrWhiteSpace(reference.OwnerPlugin)))
            throw new InvalidDataException("Interface activation frame continuation is incomplete or failed.");
        if (state.Pending) throw new NotSupportedException("Pending unlocked-container activation lacks its native lockpick continuation snapshot.");
    }

    internal static string SourceHash(FalloutPluginRecord record)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(record.Signature + "\0" + record.FormKey + "\0" + record.Flags + "\0"));
        foreach (var name in record.Plugin.Masters.Append(record.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant() + "\0"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private void RequireSession(Guid session)
    {
        RequireHealthy();
        if (session == Guid.Empty || session != _session)
            throw new InvalidOperationException("Interface frame transition has no matching living native session.");
    }
    private void RequireHealthy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null) throw new InvalidOperationException(_failure);
    }
    private void Retain(Exception error) => _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    public void Dispose() { _disposed = true; _session = Guid.Empty; }
}
