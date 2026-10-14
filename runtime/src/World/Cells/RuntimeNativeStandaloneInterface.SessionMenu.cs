using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeStandaloneInterface
{
    private Control? _sourceSessionMenuNode;
    private FalloutStandaloneInterfaceObject? _sourceSessionMenu;
    private FalloutUiComponentStore? _sourceSessionUi;
    private FalloutUiComponentBinding? _sourceSessionRoot;
    private Action? _sourceSessionClosed;
    private bool _sourceSessionBound;

    internal void BindSessionMenuLifecycles(FalloutImageSpaceState images, FalloutDirectInputState input,
        FalloutPlayerThreadContextState thread, Action requireActualOwners,
        Func<FalloutPlayerControlHudFrame> readActualFrame, string executable)
    {
        RequireNativeSessionInterface();
        var source = FalloutExecutableStringTable.ReadStandaloneMenuLifecycles(executable, _state.Source);
        _state.BindMenuLifecycles(source, images, input, thread, requireActualOwners, readActualFrame);
        _sourceSessionBound = true; ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MinValue + 1;
    }

    internal FalloutStandaloneInterfaceObject OpenSourceSessionMenu(Control nativeMenu, FalloutUiComponentStore ui)
    {
        RequireNativeSessionInterface(); ArgumentNullException.ThrowIfNull(nativeMenu); ArgumentNullException.ThrowIfNull(ui);
        if (_sourceSessionMenuNode is not null || !nativeMenu.IsInsideTree() || nativeMenu.IsQueuedForDeletion() ||
            nativeMenu.GetViewport() != GetViewport())
            throw new InvalidOperationException("Source session menu requires its actual fresh current native UI factory.");
        var path = FalloutStandaloneMenuLifecycleSource.StartMenuPath;
        if (!_source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("StartMenu source XML is absent.", path);
        var publication = new FalloutStandaloneInterfaceNativePublication(Guid.NewGuid(), _state.Process, _state.Source.Identity,
            _source.StackId, FalloutStandaloneInterfaceObjectRole.OtherMenu, nativeMenu.GetInstanceId(), path,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "actual-native-session-menu/" + nativeMenu.GetInstanceId());
        // This actual adapter instance owns the logical Menu and its winning
        // source UI root. Its panel layout has a separate pixel evidence lane.
        _sourceSessionMenuNode = nativeMenu; _sourceSessionUi = ui;
        try
        {
            _sourceSessionMenu = _state.ConstructStartMenuFields(publication, () =>
                !_retired && IsInstanceValid(nativeMenu) && nativeMenu.IsInsideTree() && !nativeMenu.IsQueuedForDeletion() &&
                nativeMenu.GetViewport() == GetViewport());
            _sourceSessionRoot = ui.RecreateMenuRoot("StartMenu");
            _state.BindStartMenuRoot(Guid.NewGuid(), _sourceSessionMenu, ui, _sourceSessionRoot);
            _state.StoreStartMenuVisible(Guid.NewGuid(), _sourceSessionMenu, BitConverter.SingleToUInt32Bits(1));
            _state.ReplaceStartMenuTransition(Guid.NewGuid(), _sourceSessionMenu);
            return _sourceSessionMenu;
        }
        catch (Exception error)
        {
            _state.RetainFailure(error); _failed(error); throw;
        }
    }

    internal void CloseSourceSessionMenu(Control actualMenu, Action actualNativeClosed)
    {
        RequireNativeSessionInterface(); ArgumentNullException.ThrowIfNull(actualNativeClosed);
        if (!ReferenceEquals(actualMenu, _sourceSessionMenuNode) || _sourceSessionMenu is not { } menu)
            throw new InvalidOperationException("Session closure changed its actual current source/native menu owner.");
        if (_sourceSessionClosed is not null) return;
        try
        {
            // Keep the real node alive until the actual queue/root release
            // returns. Its close-reference store precedes lifecycle2 allocation.
            _sourceSessionClosed = actualNativeClosed;
            _state.ReplaceStartMenuTransition(Guid.NewGuid(), menu, closing: true);
        }
        catch (Exception error) { _state.RetainFailure(error); _failed(error); throw; }
    }

    internal void AdvanceSourceSessionMenu()
    {
        RequireNativeSessionInterface();
        if (_sourceSessionMenu is not { } menu) return;
        if (_sourceSessionMenuNode is null)
        {
            CompleteReturnedMenuCloseTail(_state, ref _sourceSessionMenu);
            return;
        }
        if (_state.HasMenuFieldTransition(menu.Identity)) _state.AdvanceStartMenuFields(Guid.NewGuid(), menu);
        if (_sourceSessionClosed is not { } closed || !_state.IsStartMenuTransitionReturned(menu)) return;
        _state.CloseStartMenuFields(Guid.NewGuid(), menu); _state.RetireStartMenuFields(menu);
        _sourceSessionMenuNode = null; _sourceSessionRoot = null; _sourceSessionUi = null; _sourceSessionClosed = null;
        // Only this actual source return authorizes Root's native QueueFree
        // and pause/mouse restoration. Mode4 remains until the next UI update.
        closed();
    }

    internal static bool CompleteReturnedMenuCloseTail(FalloutStandaloneInterfaceState state,
        ref FalloutStandaloneInterfaceObject? retainedMenu)
    {
        if (retainedMenu is not { } menu) return false;
        // The actual caller has released its native node, but the source
        // object remains a receipt until the independently ordered UI/input
        // child returns. Failure preserves it for diagnostics and retirement.
        state.ReturnClosedMenuMode(Guid.NewGuid(), menu);
        retainedMenu = null;
        return true;
    }

    internal void RetireSourceSessionMenu()
    {
        // Main/menu callers must already have drained. Failed factory prefixes
        // retire independently; cleanup does not replay queue/input callbacks.
        if (_sourceSessionMenu is { } menu) _state.RetireFailedStartMenuFields(menu);
        _sourceSessionMenuNode = null; _sourceSessionMenu = null; _sourceSessionUi = null; _sourceSessionRoot = null; _sourceSessionClosed = null;
    }

    public override void _Process(double delta)
    {
        if (_retired || !_sourceSessionBound || _sourceSessionMenu is null) return;
        try { AdvanceSourceSessionMenu(); }
        catch (Exception error)
        {
            ProcessMode = ProcessModeEnum.Disabled; _state.RetainFailure(error); _failed(error);
            GD.PushError("OPENNV_SOURCE_SESSION_MENU_REFUSED " + error);
        }
    }
    private bool IsKnownSessionMenu(uint code)
    {
        return code == FalloutStandaloneMenuLifecycleSource.StartMenu && _sourceSessionMenuNode is { } node &&
            IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion() &&
            node.GetViewport() == GetViewport() && _state.HasCurrentNativeMenu(code, node.GetInstanceId());
    }
    private void RequireNativeSessionInterface()
    {
        if (_retired || !IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
            !ReferenceEquals(RuntimeLiveContentSource.Current, _source))
            throw new InvalidOperationException("Session menu lost its actual living source Interface projection.");
    }
}
