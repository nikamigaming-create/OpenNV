using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.World.Cells;

// This living native projection owns the source Pip-Boy manager. The gameplay
// HUD is an independently typed UI root; it is never substituted as that object.
internal sealed partial class RuntimeNativeStandaloneInterface : Node
{
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutStandaloneInterfaceState _state;
    private readonly FalloutPipBoyState _pipBoy;
    private readonly NativeOwnedGameplayHud _hud;
    private readonly RuntimeLiveContentSource _source;
    private readonly Func<IEnumerable<uint>?> _actualNativeMenus;
    private readonly Action<Exception> _failed;
    private FalloutStandaloneInterfaceObject? _manager;
    private bool _retired;
    internal FalloutStandaloneInterfaceState SourceState => _state;
    internal object State => new
    {
        source = _state.State,
        pipBoy = new { _pipBoy.Available, _pipBoy.Open, _pipBoy.Page, _pipBoy.Revision },
        nativeManager = GetInstanceId(),
        managerFactory = _manager,
        retired = _retired
    };
    private RuntimeNativeStandaloneInterface(FalloutReferenceWorld world, FalloutPipBoyState pipBoy,
        NativeOwnedGameplayHud hud, RuntimeLiveContentSource source, Func<IEnumerable<uint>?> actualNativeMenus, Action<Exception> failed)
    {
        _world = world; _state = world.StandaloneInterface; _pipBoy = pipBoy; _hud = hud; _source = source;
        _actualNativeMenus = actualNativeMenus; _failed = failed;
        Name = "SourceFOPipboyManager"; ProcessMode = ProcessModeEnum.Disabled;
    }
    internal static RuntimeNativeStandaloneInterface Attach(Node parent, FalloutReferenceWorld world, FalloutPipBoyState pipBoy,
        NativeOwnedGameplayHud hud, RuntimeLiveContentSource source, Func<IEnumerable<uint>?> actualNativeMenus, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(actualNativeMenus); ArgumentNullException.ThrowIfNull(failed);
        if (!world.HasStandaloneInterface || !parent.IsInsideTree() || parent.IsQueuedForDeletion() ||
            !hud.IsInsideTree() || hud.IsQueuedForDeletion() || hud.Error is not null || !ReferenceEquals(source, RuntimeLiveContentSource.Current) ||
            hud.StandaloneHudStack != source.StackId || parent.GetViewport() != hud.GetViewport())
            throw new InvalidOperationException("Standalone interface requires its actual current source/driver/HUD roots.");
        var node = new RuntimeNativeStandaloneInterface(world, pipBoy, hud, source, actualNativeMenus, failed);
        try
        {
            parent.AddChild(node);
            if (!node.IsInsideTree() || node.GetParent() != parent) throw new InvalidOperationException("Source manager did not publish to its genuine parent.");
            node._manager = node._state.PublishManager(node.Publication(node, FalloutStandaloneInterfaceObjectRole.PipBoyManager,
                "menus/main/hud_main_menu.xml", hud.StandaloneHudSha256), node.RequireLivingManager);
            return node;
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { node.Retire(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { node.Free(); } catch (Exception cleanup) { errors.Add(cleanup); }
            if (errors.Count != 1) throw new AggregateException("Standalone native interface factory and retirement failed.", errors);
            throw;
        }
    }
    private bool RequireLivingManager()
    {
        if (_retired || !IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() || !IsInstanceValid(_hud) ||
            !_hud.IsInsideTree() || _hud.IsQueuedForDeletion() || !ReferenceEquals(RuntimeLiveContentSource.Current, _source)) return false;
        if (_hud.Error is { } error) throw new NotSupportedException("source-FO3-interface-native-HUD-failure:" + error);
        // A census is only a negative guard. It never writes source mode,
        // context, active-byte or cached Main fields from a native menu count.
        var actual = _actualNativeMenus()?.ToArray() ?? [];
        if (actual.Length != 0)
            throw new NotSupportedException("source-FO3-native-menu-tile-input-manager-dispatch-unowned/" + string.Join(',', actual));
        if (_pipBoy.Open) throw new NotSupportedException("source-FO3-FOPipboyManager-current-device-transition-unowned");
        return true;
    }
    internal FalloutStandaloneInterfaceObject ConstructDialog(NativeOwnedDialogueMenu dialog)
    {
        if (_retired || !IsInsideTree() || !dialog.IsInsideTree() || dialog.GetViewport() != GetViewport() ||
            dialog.StandaloneDialogStack != _source.StackId)
            throw new InvalidOperationException("Dialog publication changed the living native interface/source root.");
        return _state.ConstructDialog(Publication(dialog, FalloutStandaloneInterfaceObjectRole.DialogMenu,
            "menus/dialog/dialog_menu.xml", dialog.StandaloneDialogSha256),
            () => IsInstanceValid(dialog) && dialog.IsInsideTree() && !dialog.IsQueuedForDeletion());
    }
    private FalloutStandaloneInterfaceNativePublication Publication(Node node, FalloutStandaloneInterfaceObjectRole role,
        string resource, string expectedHash)
    {
        if (!_source.TryRead(resource, null, out var bytes, out _) ||
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedHash)
            throw new InvalidDataException("Native interface factory no longer uses its exact winning XML input.");
        return new(Guid.NewGuid(), _state.Process, _state.Source.Identity, _source.StackId, role,
            node.GetInstanceId(), resource, expectedHash, "actual-native-source-" + role + "/" + node.GetInstanceId());
    }
    internal void Retire()
    {
        if (_retired) return;
        if (!_state.Retired)
        {
            _world.RequireStandaloneInterfaceNativeRetirement();
            _state.Retire();
        }
        _manager = null; _retired = true;
    }
    public override void _ExitTree()
    {
        try { Retire(); }
        catch (Exception error) { _state.RetainFailure(error); _failed(error); GD.PushError("OPENNV_STANDALONE_INTERFACE_RETIRE " + error); }
    }
}
