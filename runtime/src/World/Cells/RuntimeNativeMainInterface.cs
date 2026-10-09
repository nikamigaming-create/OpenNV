using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.World.Cells;

// The actual source own-menu class has a native product lifetime. This
// adapter publishes that lifetime and the independent Dialog constructor;
// native census, generic GameMode and SceneTree pause never write its fields.
internal sealed partial class RuntimeNativeMainInterface : Node
{
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutMainInterfaceState _state;
    private readonly FalloutPipBoyState _pipBoy;
    private readonly NativeOwnedGameplayHud _hud;
    private readonly RuntimeLiveContentSource _source;
    private readonly Func<IEnumerable<uint>?> _menus;
    private readonly Action<Exception> _failed;
    private bool _retired;
    internal FalloutMainInterfaceState SourceState => _state;
    private RuntimeNativeMainInterface(FalloutReferenceWorld world, FalloutPipBoyState pipBoy,
        NativeOwnedGameplayHud hud, RuntimeLiveContentSource source, Func<IEnumerable<uint>?> actualMenus, Action<Exception> failed)
    {
        _world = world; _state = world.SourceMainInterface; _pipBoy = pipBoy; _hud = hud; _source = source;
        _menus = actualMenus; _failed = failed; Name = "SourceMainFOPipboyManager"; ProcessMode = ProcessModeEnum.Disabled;
    }
    internal static RuntimeNativeMainInterface Attach(Node parent, FalloutReferenceWorld world, FalloutPipBoyState pipBoy,
        NativeOwnedGameplayHud hud, RuntimeLiveContentSource source, Func<IEnumerable<uint>?> actualMenus, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(actualMenus); ArgumentNullException.ThrowIfNull(failed);
        if (!world.HasSourceMainInterface || !parent.IsInsideTree() || parent.IsQueuedForDeletion() ||
            !hud.IsInsideTree() || hud.IsQueuedForDeletion() || hud.Error is not null || !ReferenceEquals(source, RuntimeLiveContentSource.Current) ||
            hud.StandaloneHudStack != source.StackId || parent.GetViewport() != hud.GetViewport())
            throw new InvalidOperationException("Source Main interface requires the actual current driver/source/native HUD root.");
        var node = new RuntimeNativeMainInterface(world, pipBoy, hud, source, actualMenus, failed);
        try
        {
            parent.AddChild(node);
            if (!node.IsInsideTree() || node.GetParent() != parent)
                throw new InvalidOperationException("Main source manager native attachment did not return.");
            node._state.PublishOwnManager(node.Publication(node, FalloutStandaloneInterfaceObjectRole.PipBoyManager,
                "menus/main/hud_main_menu.xml", hud.StandaloneHudSha256), node.LivingManager);
            return node;
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { node.Retire(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { node.Free(); } catch (Exception cleanup) { errors.Add(cleanup); }
            if (errors.Count != 1) throw new AggregateException("Main native interface construction and retirement failed.", errors);
            throw;
        }
    }
    private bool LivingManager()
    {
        if (_retired || !IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() || !IsInstanceValid(_hud) ||
            !_hud.IsInsideTree() || _hud.IsQueuedForDeletion() || !ReferenceEquals(RuntimeLiveContentSource.Current, _source)) return false;
        if (_hud.Error is { } error) throw new NotSupportedException("source-Main-native-HUD-failure:" + error);
        var actual = _menus()?.ToArray() ?? throw new NotSupportedException("Source interface native menu census is absent.");
        // Presence catches an unjoined source writer. It never infers mode,
        // context, active bytes or a returned source constructor from counts.
        if (actual.Length != 0) throw new NotSupportedException("source-Main-native-menu-stack-context-writer-unowned/" + string.Join(',', actual));
        if (_pipBoy.Open) throw new NotSupportedException("source-Main-FOPipboyManager-device-transition-unowned");
        return true;
    }
    internal FalloutStandaloneInterfaceObject ConstructDialog(NativeOwnedDialogueMenu dialog)
    {
        if (_retired || !IsInsideTree() || !dialog.IsInsideTree() || dialog.GetViewport() != GetViewport() ||
            dialog.StandaloneDialogStack != _source.StackId)
            throw new InvalidOperationException("Main dialog publication changed the actual current source/native root.");
        return _state.ConstructDialog(Publication(dialog, FalloutStandaloneInterfaceObjectRole.DialogMenu,
            "menus/dialog/dialog_menu.xml", dialog.StandaloneDialogSha256),
            () => IsInstanceValid(dialog) && dialog.IsInsideTree() && !dialog.IsQueuedForDeletion());
    }
    private FalloutStandaloneInterfaceNativePublication Publication(Node node, FalloutStandaloneInterfaceObjectRole role,
        string resource, string expectedHash)
    {
        if (!_source.TryRead(resource, null, out var bytes, out _) ||
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedHash)
            throw new InvalidDataException("Main native source factory no longer uses its exact winning XML input.");
        return new(Guid.NewGuid(), _state.Process, _state.Source.Identity, _source.StackId, role,
            node.GetInstanceId(), resource, expectedHash, "actual-native-source-Main-" + role + "/" + node.GetInstanceId());
    }
    internal void Retire()
    {
        if (_retired) return;
        _world.RequireSourceMainInterfaceNativeRetirement(); _state.Retire(); _retired = true;
    }
    public override void _ExitTree()
    {
        try { Retire(); }
        catch (Exception error) { _state.RetainFailure(error); _failed(error); GD.PushError("OPENNV_SOURCE_MAIN_INTERFACE_RETIRE " + error); }
    }
}
