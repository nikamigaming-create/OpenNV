using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutPluginRecord? _nativeStartingQuest;
    private FalloutNewGameBootstrap? _nativeBootstrap;
    private bool _nativeBootstrapMovie;
    private NativeGamebryoMovie? _nativeBootstrapMovieOwner;
    private FalloutNativeRaceSexSelection? _nativeBootstrapCharacter;

    private CanvasLayer? _nativeStartMenuLayer;

    private async Task BootstrapNativeNewGame()
    {
        if (_nativePluginCampaign?.ModuleFailure is { } failure)
            throw new NotSupportedException("New Game has unresolved selected native modules: " + failure);
        _nativeScriptStorage?.Auxiliary.ResetForNewGame();
        _nativeUi?.Reset();
        _nativeGameTime!.InitializeNewGame();
        _nativeBootstrapCharacter = (_nativeRaceSexContract ?? throw new InvalidOperationException("New Game has no player appearance owner.")).Initial;
        var scripts = _nativeQuestScripts ?? throw new InvalidOperationException("New Game has no quest clock owner.");
        var content = RuntimeLiveContentSource.Current!;
        var records = _nativePluginStack!;
        var initialLevel = FalloutPlayerActorValueSource.Read(records).Level;
        _nativeReferences!.BindPlayerAppearance(() => FalloutNativeCharacterCreation.ActorState(records,
            (_nativeRaceSexContract ?? throw new InvalidOperationException("Startup appearance contract is absent.")).Player,
            _nativeBootstrapCharacter ?? throw new InvalidOperationException("Startup appearance state is absent.")) with
        { PlayerYoung = scripts.Scripts.Session.PlayerYoung });
        var inventory = new FalloutInventoryCommands(records, _nativeReferences!, _nativeInventory, () => initialLevel, _nativeGlobals);
        _nativeBootstrap = new(_nativePluginStack!, FalloutInstallationSettings.Read(content), _nativeQuestState!, scripts.Scripts,
            _nativeReferences!, BootstrapNativeCommand, BootstrapNativeEffect, () => !_nativeBootstrapMovie,
            _nativeGlobals, _nativeGameTime, inventory, () => initialLevel);
        PrepareNativeSaveOrderSelection();
        _nativeBootstrap.Start();
        scripts.Bootstrap = _nativeBootstrap;
        GD.Print($"OPENNV_NEW_GAME_QUEST_START source={_nativeStartingQuest!.FormKey} winner={_nativeStartingQuest.Plugin.Name}");
        while (true)
        {
            if (_retiringNativeSession || !IsInsideTree()) throw new OperationCanceledException("New Game session retired during startup.");
            if (_nativeBootstrapMovieOwner?.Error is { } movieError) throw new NotSupportedException(movieError);
            if (scripts.StartupError is { } error) throw new NotSupportedException(error);
            if (!_nativeBootstrapMovie && !scripts.StartupMessagePending && _nativeBootstrap.PreparePlacement() is not null) break;
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
        var parts = command.Split('.');
        var operation = parts[^1].ToLowerInvariant();
        if (parts.Length > 2) throw new InvalidDataException("Startup command receiver is ambiguous.");
        switch (operation)
        {
            case "sexchange" when arguments.Count <= 2 && (parts.Length == 1 ||
                _nativePluginStack!.RuntimeFormId(bindings.Reference(parts[0])) == 0x14):
                _nativeBootstrapCharacter = FalloutScriptStartupCommands.ChangeSex(_nativePluginStack!,
                    _nativeRaceSexContract ?? throw new InvalidOperationException("Startup appearance contract is absent."),
                    _nativeBootstrapCharacter ?? throw new InvalidOperationException("Startup appearance state is absent."),
                    () => FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current ??
                        throw new InvalidOperationException("Startup character content owner is absent.")), arguments);
                return;
            case "playmusic" when parts.Length == 1 && arguments.Count == 1:
                (_nativeQuestScripts ?? throw new InvalidOperationException("Startup session is absent.")).Scripts.Session
                    .SetSourceMusic(_nativePluginStack!, bindings.Form(arguments[0]).FormKey);
                return;
            case "forceweather" when parts.Length == 1 && arguments.Count == 1:
                (_nativeSkyLighting ?? throw new InvalidOperationException("Startup sky state is absent."))
                    .ForceWeather(bindings.Form(arguments[0]).FormKey);
                return;
            case "playbink" when parts.Length == 1:
                break;
            default:
                throw new NotSupportedException($"Startup command {command} has no pre-world owner.");
        }
        _ = source;
        if (_nativeBootstrapMovie) throw new InvalidOperationException("Startup movie player is already active.");
        if (_nativeQuestScripts?.StartupMessagePending == true)
            throw new NotSupportedException("Concurrent startup movie and message input require a shared modal pause owner.");
        var request = FalloutMovieCommand.FromArguments(arguments);
        var movie = new NativeGamebryoMovie();
        try
        {
            movie.Configure(request, _ => Callable.From(() =>
            {
                // A retired movie cannot release a replacement's compiled suffix.
                if (!ReferenceEquals(_nativeBootstrapMovieOwner, movie)) return;
                _nativeBootstrapMovie = false;
                _nativeBootstrapMovieOwner = null;
            }).CallDeferred());
            _nativeBootstrapMovieOwner = movie;
            _nativeBootstrapMovie = true;
            AddChild(movie);
            if (movie.GetParent() != this) throw new InvalidOperationException("Startup movie has no actual presentation parent.");
        }
        catch
        {
            if (ReferenceEquals(_nativeBootstrapMovieOwner, movie))
            { _nativeBootstrapMovieOwner = null; _nativeBootstrapMovie = false; }
            movie.GetParent()?.RemoveChild(movie);
            movie.Free();
            throw;
        }
    }

    private IEnumerable<uint>? NativeStartupMenus()
    {
        var menus = NativeActiveMenus()?.ToHashSet() ?? [];
        if (_nativeStartMenuLayer is { } title && GodotObject.IsInstanceValid(title) && title.IsInsideTree()) menus.Add(4);
        return menus.Count == 0 ? null : menus;
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
