using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutPluginRecord? _nativeStartingQuest;
    private FalloutNewGameBootstrap? _nativeBootstrap;
    private bool _nativeBootstrapMovie;
    private NativeGamebryoMovie? _nativeBootstrapMovieOwner;

    private bool NativeUsesOpeningStart => _nativeStartingQuest is null ||
        _nativeOpeningControls!.Quests.Values.Any(stages => stages.Values.Any(stage => stage.Quest == _nativeStartingQuest.FormKey));

    private async Task BootstrapNativeNewGame()
    {
        _nativeScriptStorage?.Auxiliary.ResetForNewGame();
        _nativeUi?.Reset();
        _nativeGameTime!.InitializeNewGame();
        var scripts = _nativeQuestScripts ?? throw new InvalidOperationException("New Game has no quest clock owner.");
        var content = RuntimeLiveContentSource.Current!;
        _nativeBootstrap = new(_nativePluginStack!, FalloutInstallationSettings.Read(content), _nativeQuestState!, scripts.Scripts,
            _nativeReferences!, BootstrapNativeCommand, BootstrapNativeEffect, () => !_nativeBootstrapMovie, _nativeGlobals, _nativeGameTime);
        _nativeBootstrap.Start();
        scripts.Bootstrap = _nativeBootstrap;
        GD.Print($"OPENNV_NEW_GAME_QUEST_START source={_nativeStartingQuest!.FormKey} winner={_nativeStartingQuest.Plugin.Name}");
        while (true)
        {
            if (_retiringNativeSession || !IsInsideTree()) throw new OperationCanceledException("New Game session retired during startup.");
            if (_nativeBootstrapMovieOwner?.Error is { } movieError) throw new NotSupportedException(movieError);
            if (scripts.StartupError is { } error) throw new NotSupportedException(error);
            if (_nativeBootstrap.Placement() is not null && !_nativeBootstrapMovie) break;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void InitializeNativePlayerInventory()
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("New Game has no reference world.");
        var player = _nativePluginStack!.RuntimeFormKey(0x14);
        _ = world.EquippedArmor(player, 1, _nativeGlobals);
        _nativeInventory.Replace(world.Inventory(player, 1, _nativeGlobals).Contents.Capture());
        world.BindPlayerInventory(_nativeInventory);
    }

    private void BootstrapNativeCommand(FalloutFormKey source, FalloutScriptBindings bindings, string command, IReadOnlyList<string> arguments)
    {
        if (!command.Equals("PlayBink", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Startup command {command} has no pre-world owner.");
        if (_nativeBootstrapMovie) throw new InvalidOperationException("Startup movie player is already active.");
        var movie = new NativeGamebryoMovie();
        _nativeBootstrapMovieOwner = movie;
        _nativeBootstrapMovie = true;
        movie.Configure(FalloutMovieCommand.FromScript(command + " " + string.Join(' ', arguments)).Single(),
            _ => Callable.From(() => { _nativeBootstrapMovie = false; _nativeBootstrapMovieOwner = null; }).CallDeferred());
        AddChild(movie);
    }

    private void BootstrapNativeEffect(FalloutReferenceScriptEffect effect)
    {
        if (effect.Kind == FalloutReferenceEffectKind.ImageSpace)
        {
            if (effect.Enable) _nativeImageSpaceState.Apply(FalloutImageSpaceModifierReader.Read(_nativePluginStack!.GetEffective(effect.Target!.Value)));
            else _nativeImageSpaceState.Remove(effect.Target!.Value);
            return;
        }
        throw new NotSupportedException($"Startup effect {effect.Kind} has no pre-world owner.");
    }

    private Transform3D NativePlayerPlacementTransform(FalloutReferencePlacement placement) => new(
        GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1),
        GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * _configuration.World.GameUnitsToMeters);
}
