using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerWeaponContinuationCall? _weaponContinuation;
    private Func<Guid, long, Guid, FalloutPlayerWeaponContinuationCall>? _updateWeaponContinuation;
    private Func<FalloutPlayerWeaponContinuationCall?>? _readWeaponContinuation;
    private Guid? _enteredWeaponContinuation;

    internal void BindPlayerWeaponContinuation(FalloutPlayerWeaponContinuationSource source,
        Func<Guid, long, Guid, FalloutPlayerWeaponContinuationCall> update,
        Func<FalloutPlayerWeaponContinuationCall?> read)
    {
        RequireIdle(); source.Validate(); ArgumentNullException.ThrowIfNull(update); ArgumentNullException.ThrowIfNull(read);
        if (source.Controls.Main != Source.Main || _updateWeaponContinuation is not null)
            throw new InvalidOperationException("Player weapon services changed their actual source binding.");
        _updateWeaponContinuation = update; _readWeaponContinuation = read;
    }

    internal void RequireWeaponContinuationInvocation(Guid main, long ordinal, Guid invocation)
    {
        RequireOpacityInvocation(main, ordinal);
        if (_enteredWeaponContinuation != invocation || _followingServicesCall is not
            { Step: FalloutStandaloneFollowingServicesStep.Returned } following ||
            following.Main != main || following.MainOrdinal != ordinal || following.Invocation != invocation ||
            following.Teleport?.Trace.Result == true)
            throw new InvalidOperationException("Weapon services require the actual entered Player caller after its returned false/skipped fade.");
    }

    private void UpdatePlayerWeaponServices(Guid main, long ordinal)
    {
        var following = _followingServicesCall ?? throw new InvalidOperationException("Weapon services lost the returned following prefix.");
        if (following.Teleport?.Trace.Result == true)
            throw new NotSupportedException("source-Player-following-teleport-true-result-continuation-unbound");
        var update = _updateWeaponContinuation ??
            throw new NotSupportedException("source-Player-following-teleport-frame-services-unbound");
        _enteredWeaponContinuation = following.Invocation;
        Exception? childFailure = null;
        try
        {
            RequireWeaponContinuationInvocation(main, ordinal, following.Invocation);
            _weaponContinuation = update(main, ordinal, following.Invocation); Next();
            if (_weaponContinuation.PlayerProcess != _process)
                throw new InvalidDataException("Current weapon service returned another Player process.");
            ValidatePlayerWeaponContinuation((_stack, Source.Main, _followingServicesCall, _weaponContinuation));
        }
        catch (Exception error) { childFailure = error; throw; }
        finally
        {
            try
            {
                if (_readWeaponContinuation?.Invoke() is { } call && call.Main == main &&
                    call.MainOrdinal == ordinal && call.Invocation == following.Invocation)
                {
                    if (call.PlayerProcess != _process)
                        throw new InvalidDataException("Observed weapon service belongs to another current Player process.");
                    _weaponContinuation = call; Next();
                }
            }
            catch (Exception observationFailure)
            {
                if (childFailure is not null)
                    throw new AggregateException("Player weapon child and receipt observation failed.", childFailure, observationFailure);
                throw;
            }
            finally { _enteredWeaponContinuation = null; }
        }
    }

    private static void ValidatePlayerWeaponContinuation((string Stack, FalloutMainScriptCallerSource Main,
        FalloutStandaloneFollowingServicesCall? Following, FalloutPlayerWeaponContinuationCall? Weapon) saved)
    {
        if (saved.Weapon is not { } call) return;
        var source = FalloutPlayerWeaponContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Main));
        FalloutPlayerWeaponContinuation.Validate(call, source, saved.Stack);
        if (saved.Following is not { Step: FalloutStandaloneFollowingServicesStep.Returned } following ||
            call.Main != following.Main || call.MainOrdinal != following.MainOrdinal || call.Invocation != following.Invocation ||
            following.Teleport?.Trace.Result == true)
            throw new InvalidDataException("Player weapon services lost their actual current/failed caller prefix.");
    }
}
