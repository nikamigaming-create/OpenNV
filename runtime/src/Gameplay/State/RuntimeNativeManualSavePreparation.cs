using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class RuntimeNativeManualSavePreparation : Node
{
    private readonly RuntimeManualSaveRequests _requests;
    private readonly Guid _session;
    private readonly string _source;
    private readonly RuntimeNativePlayer _player;
    private readonly FalloutSoundVoices _sounds;
    private readonly Func<string?> _invalidation;
    private readonly Func<RuntimeManualSaveAdmission> _admission;
    private readonly Func<Guid, RuntimeSaveSlotMetadata> _writer;
    private readonly Func<Guid, RuntimeManualSaveWork>? _backgroundWriter;
    private readonly Action<RuntimeManualSaveReceipt> _publish;
    private readonly Action _settled;
    private readonly ulong _maximumWaitMilliseconds;
    private readonly IReadOnlyList<Node> _sourceProducers;
    private readonly Action<Exception> _reportFailure;
    private readonly FalloutScriptManualSaveRequests? _sourceRequests;
    private readonly Func<string?>? _originalSourceBlocker;
    internal RuntimeManualSaveSourceOrder? SourceOrder { get; private set; }
    private FalloutFiniteSoundSaveDrain? _audio;
    private RuntimeNativeSaveProducerPause? _sourcePause;
    private SceneTree? _tree;
    private Action? _releaseInput;
    private bool _priorPaused, _begun, _finished, _waitingAdmission, _pauseAcquired;
    private ulong _admissionStarted;
    private Input.MouseModeEnum _priorMouse;

    internal RuntimeNativeManualSavePreparation(RuntimeManualSaveRequests requests, Guid session, string source,
        RuntimeNativePlayer player, FalloutSoundVoices sounds, Func<string?> invalidation,
        Func<RuntimeManualSaveAdmission> admission, Func<Guid, RuntimeSaveSlotMetadata> writer,
        Action<RuntimeManualSaveReceipt> publish, Action settled,
        ulong maximumWaitMilliseconds = RuntimeManualSavePreparation.MaximumWaitMilliseconds,
        IReadOnlyList<Node>? sourceProducers = null, Action<Exception>? reportFailure = null,
        FalloutScriptManualSaveRequests? sourceRequests = null, Func<string?>? originalSourceBlocker = null,
        Func<Guid, RuntimeManualSaveWork>? backgroundWriter = null)
    {
        _requests = requests; _session = session; _source = source; _player = player; _sounds = sounds;
        _invalidation = invalidation; _admission = admission; _writer = writer; _publish = publish; _settled = settled;
        _maximumWaitMilliseconds = maximumWaitMilliseconds;
        _sourceProducers = sourceProducers ?? [];
        _reportFailure = reportFailure ?? (error => GD.PushError($"OPENNV_NATIVE_MANUAL_SAVE_PREPARATION_FAILURE {error}"));
        _sourceRequests = sourceRequests; _originalSourceBlocker = originalSourceBlocker;
        _backgroundWriter = backgroundWriter;
        Name = "ManualSavePreparation"; ProcessMode = ProcessModeEnum.Always;
    }

    internal void Begin()
    {
        if (_begun || !IsInsideTree() || !_requests.Pending)
            throw new InvalidOperationException("Native save preparation has no unique pending scene request.");
        _begun = true;
        _tree = GetTree(); _admissionStarted = Time.GetTicksMsec();
        try
        {
            if (_invalidation() is { } reason) throw new InvalidOperationException(reason);
            AcquireOrWait();
            if (!_requests.Pending) Finish();
        }
        catch (Exception error)
        {
            _requests.Fail(error.Message);
            _reportFailure(error);
            Finish();
        }
    }

    public override void _Process(double delta)
    {
        if (!_begun || _finished) return;
        try
        {
            if (!_requests.Pending) return;
            if (_requests.Writing) { _requests.CompletePendingWrite(); return; }
            if (_invalidation() is { } reason) { _requests.Cancel(reason); return; }
            if (_waitingAdmission) { AcquireOrWait(); return; }
            if (_tree?.Paused != true || !_player.ModalInput || Input.MouseMode != Input.MouseModeEnum.Visible)
                throw new FalloutFiniteSoundSaveDrainInvalidatedException("Manual save lost its gameplay/input pause lease.");
            _sourcePause!.Validate();
            SourceOrder?.Validate(_sourceRequests!, _requests.Receipt!, Engine.GetProcessFrames());
            _requests.DrainPreparedWork(_session, _source, Engine.GetProcessFrames(), Time.GetTicksMsec(),
                () => _audio!.ObservePending(), _admission, WriteOrderedCheckpoint);
            if (_requests.Pending) _publish(_requests.Receipt!);
        }
        catch (FalloutFiniteSoundSaveDrainInvalidatedException error) { _requests.Cancel(error.Message); }
        catch (Exception error)
        {
            _requests.Fail(error.Message);
            _reportFailure(error);
        }
        finally { if (!_requests.Pending) Finish(); }
    }

    private void AcquireOrWait()
    {
        if (_sourceRequests is not null)
        {
            _requests.ObserveOrderedSourceSave(_sourceRequests.Receipt);
            SourceOrder = new(_sourceRequests, _requests.Receipt!, Engine.GetProcessFrames());
            _requests.ObserveOrderedSourceSave(SourceOrder.Receipt);
        }
        var admission = _admission();
        admission.Validate();
        if (admission.Kind == RuntimeManualSaveAdmissionKind.Refused)
        {
            SourceOrder?.Dispose(); SourceOrder = null;
            if (RuntimeManualSaveAdmissionWait.ShouldWait(admission, _tree!.Paused,
                    _admissionStarted, Time.GetTicksMsec()))
            {
                _waitingAdmission = true;
                _publish(_requests.Receipt!);
                return;
            }
            _requests.Fail(admission.Blocker!);
            return;
        }
        _waitingAdmission = false;
        _priorPaused = _tree!.Paused; _priorMouse = Input.MouseMode;
        _audio = _sounds.PrepareFiniteSaveDrain();
        _tree.Paused = true; _pauseAcquired = true;
        _sourcePause = new(_sourceProducers);
        _releaseInput = _player.AcquirePausedSaveInput();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _requests.Prepare(Engine.GetProcessFrames(), Time.GetTicksMsec(), admission, _audio.Voices, _maximumWaitMilliseconds);
        _audio.Activate();
        _publish(_requests.Receipt!);
    }

    internal void Cancel(string reason)
    {
        if (_finished) return;
        if (_requests.Writing) _requests.CompletePendingWrite(wait: true);
        _requests.Cancel(reason);
        Finish();
    }

    internal bool PermitsOriginalSourceDrain()
    {
        var source = SourceOrder;
        if (!_begun || _finished || source is not { Draining: true } || !_requests.Pending ||
            _invalidation() is not null || _tree?.Paused != true || !_player.ModalInput ||
            Input.MouseMode != Input.MouseModeEnum.Visible) return false;
        _sourcePause!.Validate();
        return source.PermitsOriginalSourceDrain(_sourceRequests!, _requests.Receipt!, Engine.GetProcessFrames()) &&
            _audio!.ObservePending().Count == 0 && _admission().Kind == RuntimeManualSaveAdmissionKind.Ready;
    }

    private RuntimeManualSaveWork WriteOrderedCheckpoint(Guid id)
    {
        if (SourceOrder is { } source)
        {
            try
            {
                if (_originalSourceBlocker is null)
                    throw new NotSupportedException("Ordered source save lacks its original coordinator/driver admission callback.");
                source.DrainBeforeManual(_requests.Receipt!, Engine.GetProcessFrames(), _originalSourceBlocker);
                source.Validate(_sourceRequests!, _requests.Receipt!, Engine.GetProcessFrames());
                _sourceRequests!.RequireCapture();
            }
            finally { _requests.ObserveOrderedSourceSave(source.Receipt); }
        }
        if (_invalidation() is { } reason) throw new FalloutFiniteSoundSaveDrainInvalidatedException(reason);
        _sourcePause!.Validate();
        if (_audio!.ObservePending().Count != 0 || _admission().Kind != RuntimeManualSaveAdmissionKind.Ready)
            throw new NotSupportedException("Ordered source/manual preparation lost its complete quiescent capture boundary.");
        return _backgroundWriter is { } writer ? writer(id) : new(() => true, () => Task.FromResult(_writer(id)));
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        List<Exception> errors = [];
        try
        {
            // Restore the tree while audio still has Always mode, then restore
            // each original playback flag after Godot's pause notifications.
            try { if (_pauseAcquired && _tree is not null && GodotObject.IsInstanceValid(_tree)) _tree.Paused = _priorPaused; }
            catch (Exception error) { errors.Add(error); }
        }
        finally
        {
            try { _audio?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                _audio = null;
                try { SourceOrder?.Dispose(); }
                catch (Exception error) { errors.Add(error); }
                finally
                {
                    SourceOrder = null;
                    try { _sourcePause?.Dispose(); }
                    catch (Exception error) { errors.Add(error); }
                    finally
                    {
                        _sourcePause = null;
                        try { _releaseInput?.Invoke(); }
                        catch (Exception error) { errors.Add(error); }
                        finally
                        {
                            _releaseInput = null;
                            try { if (_pauseAcquired) Input.MouseMode = _priorMouse; }
                            catch (Exception error) { errors.Add(error); }
                        }
                    }
                }
            }
        }
        try
        {
            if (errors.Count != 0)
            {
                var error = new AggregateException("Manual save lease cleanup failed.", errors);
                _requests.ReportCleanupFailure(error.Message + " " + string.Join("; ", errors.Select(item => item.Message)));
                GD.PushError($"OPENNV_NATIVE_MANUAL_SAVE_CLEANUP_FAILURE {error}");
            }
            try { _publish(_requests.Receipt!); }
            catch (Exception error)
            {
                _requests.ReportCleanupFailure("Save feedback publication failed: " + error.Message);
                GD.PushError($"OPENNV_NATIVE_MANUAL_SAVE_FEEDBACK_FAILURE {error}");
            }
        }
        finally
        {
            _settled();
            if (!IsQueuedForDeletion())
            {
                if (IsInsideTree()) QueueFree(); else Free();
            }
        }
    }

    public override void _ExitTree()
    {
        if (_finished || !_begun) return;
        if (_requests.Writing) _requests.CompletePendingWrite(wait: true);
        _requests.Cancel("Native save-preparation owner retired before its complete checkpoint committed.");
        Finish();
    }
}
