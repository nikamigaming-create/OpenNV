using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutSleepWait? _playerRest;
    private NativeSleepWaitMenuSource? _playerRestMenuSource;
    private RuntimeNativeSleepWaitEntry? _playerRestEntry;
    private Func<FalloutRestObservation>? _playerRestCloseGate;
    private bool _playerRestRetired;
    internal FalloutSleepWait PlayerRest => _playerRest ??
        throw new NotSupportedException("Player sleep/wait has no current selected source owner.");
    internal object? PlayerRestState => _playerRest?.State;
    internal object? PlayerRestNativeState => _playerRestEntry?.State;
    internal uint? PlayerRestMenuId => _playerRestEntry?.MenuId;
    internal bool PlayerRestOwnsMenuClock => PlayerRest.OwnsClock;
    internal string? PlayerRestSaveBlocker => _playerRest?.SaveBlocker;

    // Join after the shared physical owner is configured and before publishing
    // its cold body. Required world/restriction/effect facts come from actual
    // source producers in host.Observe; registration never satisfies a fact.
    internal void ConfigureCurrentPlayerRest(FalloutSleepWaitHost host,
        Func<FalloutRestObservation> sourceCloseGate, FalloutSleepWaitSnapshot? restore)
    {
        if (_playerRest is not null || _playerRestRetired)
            throw new InvalidOperationException("Player sleep/wait cannot be configured twice.");
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(sourceCloseGate);
        var runtime = RequireCurrentPlayerRuntimeSource();
        var clock = _gameTime ?? throw new NotSupportedException("Rest has no actual shared calendar/global owner.");
        var source = FalloutSleepWaitSource.Read(_pluginStack, runtime.Receipt);
        var caption = new FalloutSleepWaitCaption(_pluginStack, source,
            FalloutExecutableStringTable.ReadSleepWaitCaption(runtime.OwnedSource.FalloutExecutablePath));
        var nativeSource = new NativeSleepWaitMenuSource(_pluginStack, source, clock.Stamp, caption.Format, _scripts.Ui);
        var owner = new FalloutSleepWait(source, clock, host with
        {
            WritePlayerSleepFlag = _player.WriteSourceSleepFlag,
            ReadPlayerSleeping = () => _player.CommittedPlayerSleeping,
            CloseMenu = (request, completed) =>
            {
                host.CloseMenu(request, completed);
                if (request.Origin != FalloutRestOrigin.ScriptHours)
                    (_playerRestEntry ?? throw new InvalidOperationException("Rest closure has no actual native menu lease.")).Close();
            },
        }, restore);
        if (owner.Request?.BedSource is { } bed) bed.RequireCurrent(FalloutSleepWaitBed.ReadCurrent(_pluginStack, bed.Reference));
        if (_player.CommittedPlayerSleeping != owner.Sleeping)
            throw new InvalidDataException("Current rest continuation differs from the actual physical player's committed flag.");
        _player.BindSourceSleepContinuation(source, () => owner.OwnsPhysicalSleepContinuation);
        _playerRest = owner; _playerRestMenuSource = nativeSource; _playerRestCloseGate = sourceCloseGate;
        _player.OpenSourceWaitMenu += OpenCurrentPlayerWait;
        _player.OpenSourceSleepMenu += OpenCurrentPlayerRest;
    }

    private void OpenCurrentPlayerWait() => OpenCurrentPlayerRest(new(FalloutRestKind.Wait, FalloutRestOrigin.PlayerControl));
    internal void OpenCurrentPlayerRest(FalloutRestRequest request)
    {
        if (_playerRestRetired || !IsInsideTree() || _playerRestEntry is not null ||
            ActiveMenus().Any() || BlockingExecutionFault is not null)
            throw new InvalidOperationException("Source sleep/wait cannot acquire the current native input/menu lifetime.");
        PlayerRest.Open(request);
        try { PublishPendingSourceRestMenu(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { RetainCurrentPlayerRestFailure(error); throw; }
    }

    // Called only after the restored driver/body has genuinely attached. It
    // publishes the saved request without consuming an hour or a result again.
    internal bool PublishPendingSourceRestMenu()
    {
        var owner = PlayerRest;
        owner.RequireHealthy();
        if (!owner.NeedsMenuPublication) return false;
        if (_playerRestRetired || !IsInsideTree() || _playerRestEntry is not null || owner.Published)
            throw new InvalidOperationException("Pending rest publication has no new native parent/input lease.");
        _playerRestEntry = RuntimeNativeSleepWaitEntry.Attach(this,
            _playerRestMenuSource ?? throw new NotSupportedException("Rest has no selected source tiles."), owner,
            _player.AcquireModalInput,
            _playerRestCloseGate ?? throw new NotSupportedException("Rest has no source UI-close predicate."),
            RetainCurrentPlayerRestFailure, () => _playerRestEntry = null);
        return true;
    }

    // Root invokes this exactly once in the genuine player update after source
    // commands, independently of the menu's one-second Always countdown.
    internal bool AdvanceSourceRestInCurrentPlayerFrame(ulong nativeFrame)
    {
        if (_playerRestRetired || !IsInsideTree() || !CanProcess() || GetTree().Paused ||
            !_player.IsInsideTree() || !_player.CanProcess())
            throw new InvalidOperationException("Sleep hours have no admitted current player update.");
        try { return PlayerRest.AdvancePlayerUpdate(nativeFrame); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { RetainCurrentPlayerRestFailure(error); throw; }
    }

    internal FalloutSleepWaitSnapshot CaptureCurrentPlayerRest() => PlayerRest.Capture();
    private void RetainCurrentPlayerRestFailure(Exception error)
    {
        _playerRest?.ReportNativeFailure(FalloutRestStep.Publication, error);
        if (ExecutionError is null) RetainDriverFailure(error);
    }
    internal void RetireCurrentPlayerRest()
    {
        if (_playerRestRetired) return;
        _playerRestRetired = true;
        _player.OpenSourceWaitMenu -= OpenCurrentPlayerWait;
        _player.OpenSourceSleepMenu -= OpenCurrentPlayerRest;
        var entry = _playerRestEntry; _playerRestEntry = null;
        try { entry?.Release(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { RetainCurrentPlayerRestFailure(error); throw; }
        finally
        {
            if (entry is not null && GodotObject.IsInstanceValid(entry)) entry.QueueFree();
            _playerRest?.RetirePublication();
        }
    }
}
