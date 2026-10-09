using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private bool _perceptionInputsAttached;
    private IDisposable? _perceptionInputLease, _perceptionPlayerLease;
    internal object? ActorPerceptionState => _scripts.References?.ActorPerceptionState;
    internal string? ActorPerceptionSaveBlocker => !_perceptionInputsAttached ?
        "source-actor-perception-native-input-lifetime-absent" : _scripts.References!.ActorPerceptionSaveBlocker;

    internal void ConfigureSourceActorPerception(FalloutActorPerceptionSnapshot? restore)
    {
        var source = _pluginStack.OwnedSource ?? throw new InvalidOperationException("Perception has no exact selected source.");
        var declaration = FalloutActorPerceptionDeclaration.Read(source.FalloutExecutablePath);
        var world = _scripts.References ?? throw new InvalidOperationException("Perception has no shared source reference world.");
        if (world.ActorPerceptionConfigured) world.RequireActorPerceptionBinding(declaration, source.StackId, restore);
        else world.ConfigureActorPerception(declaration, source.StackId, restore);
    }
    private void AttachSourceActorPerception()
    {
        if (_perceptionInputsAttached || !IsInsideTree()) throw new InvalidOperationException("Perception native inputs require a unique attached driver.");
        var world = _scripts.References!;
        _perceptionInputLease = world.BindPerceptionSourceInputs(reference => ReferenceScriptPlacement(reference, true));
        // Native producers publish actual source body/activity only. No ray or
        // renderer membership is registered as a signed detection producer.
        try { _perceptionPlayerLease = _player.BindPlayerPerception(world, () => _activeCell); }
        catch { _perceptionInputLease.Dispose(); _perceptionInputLease = null; throw; }
        _perceptionInputsAttached = true;
    }
    private void AdvanceSourceActorPerception(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0 || delta > float.MaxValue)
            throw new InvalidDataException("Actor perception has no finite engine simulation interval.");
        if (!_perceptionInputsAttached || !IsInsideTree()) throw new NotSupportedException("Actor perception native input owner is absent.");
        // The save drain can make the driver Always while gameplay is paused.
        // Its source action/light/cache clocks retain their pausable lifetime.
        if (GetTree().Paused || !CanProcess()) return;
        _scripts.References!.AdvanceActorPerception((float)delta);
    }
    private FalloutActorPerceptionSnapshot CaptureSourceActorPerception()
    {
        if (ActorPerceptionSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return _scripts.References!.CaptureActorPerception();
    }
    private void RetireSourceActorPerceptionInputs()
    {
        _perceptionInputsAttached = false;
        var errors = new List<Exception>();
        var player = _perceptionPlayerLease; _perceptionPlayerLease = null;
        var input = _perceptionInputLease; _perceptionInputLease = null;
        try { player?.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { input?.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Source perception input retirement failed.", errors);
    }
}
