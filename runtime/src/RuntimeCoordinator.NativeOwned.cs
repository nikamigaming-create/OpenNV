using System.Text.Json;
using System.Diagnostics;
using System.Text;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private const string NativeNewVegasInitialCellPlugin = "FalloutNV" + ".esm";
    private const string NativeFallout3InitialCellPlugin = "Fallout3" + ".esm";
    private const uint NativeFallout3InitialCellObjectId = 0x28138;
    private const uint NativeFallout3PlayerStartObjectId = 0x39562;
    private const int NativeMenuCanvasLayer = 100;
    private const float NativeMenuBackgroundRed = 0.015f;
    private const float NativeMenuBackgroundGreen = 0.02f;
    private const float NativeMenuBackgroundBlue = 0.015f;
    private const float NativeMenuWidthPixels = 520.0f;
    private const int NativeMenuTitleFontPixels = 30;
    private const int DocWakeDialogueStage = 3;
    private const int DocInitialDialogueStage = 8;
    private const int DocFollowupDialogueStage = 15;
    private const int DocWalkDialogueStage = 25;
    private const int DocNameDialogueStage = 35;
    private const int DocChairExitDialogueStage = 40;
    private const int DocVigorApproachDialogueStage = 50;
    private const int DocVigorResultDialogueStage = 70;
    private const int DocPsychResultDialogueStage = 79;
    private const int DocTagSkillResultDialogueStage = 95;
    private const int DocTraitResultDialogueStage = 105;
    private FalloutPluginStack? _nativePluginStack;
    private FalloutCellScene? _nativeInitialCell;
    private Node3D? _nativeCurrentCellRoot;
    private readonly Dictionary<string, Node3D> _nativeNifPrototypes =
        new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _nativePrewarmedInitialCellRoot;
    private RuntimeNativePlayer? _nativePlayer;
    private FalloutOpeningControlGraph? _nativeOpeningControls;
    private FalloutOpeningStageTransitionGraph? _nativeOpeningTransitions;
    private FalloutOpeningInventoryGrant? _nativeOpeningGrant;
    private FalloutNativeRaceSexContract? _nativeRaceSexContract;
    private FalloutNativeVigorContract? _nativeVigorContract;
    private FalloutNativeTagSkillContract? _nativeTagSkillContract;
    private FalloutNativeTraitFarewellContract? _nativeTraitFarewellContract;
    private FalloutNativeCampaignRestore? _nativeOpeningRestore;
    private RuntimeNativeOpeningStageDriver? _nativeOpeningStageDriver;
    private bool _nativeSkipOpeningMovie;
    private bool _nativeContinueOpening;

    private void LoadNativeLiveStack()
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        SetLoadingStatus("INDEXING LIVE PLUGINS");
        if (DisplayServer.GetName() == "headless" || _options.ContainsKey("new-game"))
        {
            IndexNativeLiveStack(source.PluginSources);
            if (_options.ContainsKey("new-game"))
                LoadNativeInitialCell();
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            return;
        }
        ShowNativeLiveMenu(source.PluginSources);
        DismissLoadingScreen();
    }

    private void IndexNativeLiveStack(IReadOnlyList<FalloutPluginSource> sources)
    {
        var content = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        _nativePluginStack = FalloutPluginStack.Load(sources, out var loadMetrics);
        var initialCell = content.Campaign == RuntimeLiveContentSource.Fallout3Game
            ? new FalloutFormKey(NativeFallout3InitialCellPlugin, NativeFallout3InitialCellObjectId)
            : new FalloutFormKey(NativeNewVegasInitialCellPlugin, 0x103df9);
        _nativeInitialCell = FalloutCellSceneReader.Read(
            _nativePluginStack,
            initialCell);
        if (content.Campaign == RuntimeLiveContentSource.FalloutNewVegasGame)
        {
            _nativeOpeningControls = FalloutOpeningPlayerControlResolver.Resolve(
                _nativePluginStack,
                ["VCG00", "VCG01"]);
            _nativeOpeningGrant = FalloutOpeningInventoryGrantResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls,
                "VCG01");
            _nativeRaceSexContract = FalloutNativeRaceSexResolver.Resolve(_nativePluginStack);
            _nativeVigorContract = FalloutNativeVigorResolver.Resolve(
                _nativePluginStack,
                _nativeInitialCell);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeOpeningTransitions,
                "VCG01",
                [
                    DocWakeDialogueStage,
                    DocInitialDialogueStage,
                    DocFollowupDialogueStage,
                    DocWalkDialogueStage,
                    DocNameDialogueStage,
                    DocChairExitDialogueStage,
                    DocVigorApproachDialogueStage,
                ]);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeOpeningTransitions,
                "VCG01",
                [DocVigorResultDialogueStage]);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeOpeningTransitions,
                "VCG01",
                [DocPsychResultDialogueStage]);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeOpeningTransitions,
                "VCG01",
                [DocTagSkillResultDialogueStage]);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeOpeningTransitions,
                "VCG01",
                [DocTraitResultDialogueStage]);
            _nativeTagSkillContract = FalloutNativeTagSkillResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls);
            _nativeTraitFarewellContract = FalloutNativeTraitFarewellResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeInitialCell);
            var savePath = Path.GetFullPath(RequireOption(_options, "save-path"));
            _nativeOpeningRestore = null;
            if (File.Exists(savePath))
            {
                try
                {
                    _nativeOpeningRestore = FalloutNativeCampaignSave.Read(
                        savePath,
                        content.SaveCompatibilityId,
                        _nativePluginStack,
                        _nativeVigorContract ?? throw new InvalidOperationException(
                            "Native Vigor contract was not resolved."),
                        _nativeTagSkillContract ?? throw new InvalidOperationException(
                            "Native tag-skill contract was not resolved."),
                        _nativeOpeningGrant ?? throw new InvalidOperationException(
                            "Native opening inventory grant was not resolved."),
                        _nativeTraitFarewellContract ?? throw new InvalidOperationException(
                            "Native trait/farewell contract was not resolved."));
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or JsonException)
                {
                    GD.PushWarning($"OPENNV_NATIVE_CONTINUE_REJECTED {exception.Message}");
                }
            }
        }
        var archiveWarmupWait = Stopwatch.StartNew();
        content.ArchiveWarmup.GetAwaiter().GetResult();
        archiveWarmupWait.Stop();
        GD.Print(
            $"OPENNV_NATIVE_STACK_READY edition={content.Edition} campaign={content.Campaign} " +
            $"game={content.Game} plugins={_nativePluginStack.Plugins.Count} " +
            $"records={_nativePluginStack.EffectiveRecordCount} cell={_nativeInitialCell.Cell.FormKey} " +
            $"references={_nativeInitialCell.References.Count} " +
            $"models={_nativeInitialCell.BaseObjects.Values.Count(value => value.ModelPath is not null)} " +
            $"pluginOpenMs={loadMetrics.PluginHeaderScan.TotalMilliseconds:F1} " +
            $"winnerIndexMs={loadMetrics.WinnerConstruction.TotalMilliseconds:F1} " +
            $"archiveWinnerWaitMs={archiveWarmupWait.Elapsed.TotalMilliseconds:F1}");
    }

    private async void IndexNativeLiveStackForMenu(
        IReadOnlyList<FalloutPluginSource> sources,
        Label status,
        Button start,
        Button skip,
        Button continueGame)
    {
        try
        {
            await Task.Run(() => IndexNativeLiveStack(sources));
            status.Text = "Live ESM/BSA index ready\nWarming the opening CELL in RAM…";
            var initialCell = _nativeInitialCell ??
                throw new InvalidOperationException("Native initial CELL was not decoded.");
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");
            var transition = RuntimeLiveContentSource.Current?.Campaign ==
                RuntimeLiveContentSource.FalloutNewVegasGame
                ? FalloutDoorTransitionResolver.ResolveInteriorExits(stack, initialCell).Single()
                : null;
            _nativePrewarmedInitialCellRoot = BuildNativeCellRoot(
                initialCell,
                transition,
                sourceSide: true);
            status.Text = $"{_nativePluginStack!.Plugins.Count} plugins • " +
                          $"{_nativeInitialCell!.References.Count} initial CELL references\n" +
                          "Opening CELL resident in RAM • live files remain authoritative.";
            start.Disabled = false;
            skip.Disabled = false;
            continueGame.Disabled = _nativeOpeningRestore is null;
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_NATIVE_STACK_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void ShowNativeLiveMenu(IReadOnlyList<FalloutPluginSource> sources)
    {
        var layer = new CanvasLayer { Name = "NativeLiveMenu", Layer = NativeMenuCanvasLayer };
        var background = new ColorRect
        {
            Color = new Color(
                NativeMenuBackgroundRed,
                NativeMenuBackgroundGreen,
                NativeMenuBackgroundBlue,
                1.0f),
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        layer.AddChild(background);
        var center = new CenterContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        layer.AddChild(center);
        var stack = new VBoxContainer { CustomMinimumSize = new Vector2(NativeMenuWidthPixels, 0) };
        center.AddChild(stack);
        var title = new Label
        {
            Text = "OPENNV — LIVE RETAIL FILES",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", NativeMenuTitleFontPixels);
        stack.AddChild(title);
        var status = new Label
        {
            Text = "Indexing the active ESM/ESP stack in memory…",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.AddChild(status);
        var start = new Button { Text = "NEW GAME — LOAD LIVE CELL", Disabled = true };
        start.Pressed += () =>
        {
            _nativeContinueOpening = false;
            _nativeSkipOpeningMovie = false;
            layer.QueueFree();
            SetLoadingStatus("STREAMING LIVE CELL / NIF / DDS");
            Callable.From(LoadNativeInitialCell).CallDeferred();
        };
        stack.AddChild(start);
        var skip = new Button { Text = "SKIP INTRO — LIVE DOC HANDOFF", Disabled = true };
        skip.Pressed += () =>
        {
            _nativeContinueOpening = false;
            _nativeSkipOpeningMovie = true;
            layer.QueueFree();
            SetLoadingStatus("STREAMING LIVE CELL / NIF / DDS");
            Callable.From(LoadNativeInitialCell).CallDeferred();
        };
        stack.AddChild(skip);
        var continueGame = new Button { Text = "CONTINUE — RESTORE LIVE CAMPAIGN", Disabled = true };
        continueGame.Pressed += () =>
        {
            _nativeContinueOpening = true;
            _nativeSkipOpeningMovie = false;
            layer.QueueFree();
            SetLoadingStatus("RESTORING SAVE AGAINST LIVE PLUGINS");
            Callable.From(LoadNativeInitialCell).CallDeferred();
        };
        stack.AddChild(continueGame);
        var quit = new Button { Text = "QUIT" };
        quit.Pressed += () => GetTree().Quit();
        stack.AddChild(quit);
        AddChild(layer);
        IndexNativeLiveStackForMenu(sources, status, start, skip, continueGame);
    }

    private void LoadNativeInitialCell()
    {
        _ = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during startup.");
        var cell = _nativeInitialCell ??
            throw new InvalidOperationException("Native initial CELL was not decoded.");
        var stack = _nativePluginStack ??
            throw new InvalidOperationException("Native plugin stack was not indexed.");
        var fallout3 = RuntimeLiveContentSource.Current.Campaign ==
            RuntimeLiveContentSource.Fallout3Game;
        var transition = fallout3
            ? null
            : FalloutDoorTransitionResolver.ResolveInteriorExits(stack, cell).Single();
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        var activeCell = restore?.State.ActiveCell ?? cell.Cell.FormKey;
        var sourceSide = true;
        var activeScene = cell;
        if (!fallout3 && activeCell != cell.Cell.FormKey)
        {
            if (transition is null || activeCell != transition.DestinationScene.Cell.FormKey)
                throw new InvalidDataException(
                    $"Native Continue active CELL is outside the admitted route: {activeCell}.");
            activeScene = transition.DestinationScene;
            sourceSide = false;
        }
        var root = sourceSide && activeScene.Cell.FormKey == cell.Cell.FormKey &&
            _nativePrewarmedInitialCellRoot is not null
            ? _nativePrewarmedInitialCellRoot
            : BuildNativeCellRoot(activeScene, transition, sourceSide);
        _nativePrewarmedInitialCellRoot = null;
        if (!sourceSide && transition is not null)
            root.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(
                FalloutLandscapeTransportResolver.Resolve(stack, transition),
                _configuration.World.GameUnitsToMeters));
        AddChild(root);
        if (activeScene.Cell.Lighting is not null)
            AddNativeCellEnvironment(root, activeScene);
        else if (transition is not null)
            root.SetMeta(
                "opennv_exterior_environment_pending",
                transition.DestinationWorldspace.ToString());
        if (fallout3)
            AddNativeFallout3PlayerCamera(root, cell);
        else
            AddNativePlayer(root, cell, sourceSide);
        _nativeCurrentCellRoot = root;
        GD.Print(
            $"OPENNV_NATIVE_ACTIVE_CELL cell={activeScene.Cell.FormKey} " +
            $"restored={(restore is not null)} sourceSide={sourceSide}");
        DismissLoadingScreen();
    }

    private Node3D BuildNativeCellRoot(
        FalloutCellScene cell,
        FalloutDoorTransition? transition,
        bool sourceSide)
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during CELL streaming.");
        var root = new Node3D { Name = $"NativeCell_{cell.Cell.FormKey}" };
        const string parityScope = "world/active-cell";
        string ParityIdentity(FalloutPlacedReference reference) =>
            $"{cell.Cell.FormKey}/{reference.FormKey}";
        _parityObservations.ReplaceScope(
            parityScope,
            cell.References.Select(reference =>
            {
                var baseObject = cell.BaseObjects[reference.Base];
                return (
                    ParityIdentity(reference),
                    ParityCategoryFor(baseObject.Signature),
                    NativeReferenceState(reference, baseObject, "source"));
            }));
        var placed = 0;
        var placedLights = 0;
        foreach (var reference in cell.References)
        {
            if (!cell.BaseObjects.TryGetValue(reference.Base, out var baseObject))
                throw new InvalidDataException(
                    $"Live CELL reference {reference.FormKey} has no decoded base object.");
            if (FalloutCellSceneReader.IsInitiallyDisabled(reference))
            {
                _parityObservations.Observe(
                    parityScope,
                    ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "disabled"));
                continue;
            }
            if (baseObject.Light is not null)
            {
                AddNativePlacedLight(root, reference, baseObject);
                _parityObservations.Observe(
                    parityScope,
                    ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "light"));
                placedLights++;
                continue;
            }
            if (baseObject.ModelPath is null)
                continue;
            if (!_nativeNifPrototypes.TryGetValue(baseObject.ModelPath, out var prototype))
            {
                if (!source.TryRead(baseObject.ModelPath, null, out var nif, out var nifSource))
                    throw new FileNotFoundException(
                        $"Winning model {baseObject.ModelPath} for {baseObject.FormKey} is missing.");
                GD.Print(
                    $"OPENNV_NATIVE_NIF_LOADING model={baseObject.ModelPath} source={nifSource} " +
                    $"base={baseObject.FormKey}");
                var built = NativeNifMeshBuilder.Build(nif, _configuration.World.GameUnitsToMeters);
                prototype = built.Root;
                prototype.Name = $"Prototype_{baseObject.FormKey}";
                _nativeNifPrototypes.Add(baseObject.ModelPath, prototype);
                GD.Print(
                    $"OPENNV_NATIVE_NIF_READY model={baseObject.ModelPath} source={nifSource} " +
                    $"nodes={built.Nodes} surfaces={built.Surfaces} vertices={built.Vertices} triangles={built.Triangles} " +
                    $"collisionBodies={built.CollisionBodies} collisionShapes={built.CollisionShapes} " +
                    $"collisionTriangles={built.CollisionTriangles}");
            }
            var instance = prototype.Duplicate((int)Node.DuplicateFlags.UseInstantiation) as Node3D ??
                throw new InvalidOperationException($"Could not instantiate native NIF {baseObject.ModelPath}.");
            instance.Name = $"Reference_{reference.FormKey}";
            instance.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            instance.Transform = ReferenceTransform(reference);
            root.AddChild(instance);
            _parityObservations.Observe(
                parityScope,
                ParityIdentity(reference),
                NativeReferenceState(reference, baseObject, "model"));
            var portalReference = transition is null
                ? null
                : sourceSide ? transition.SourceDoor : transition.DestinationDoor;
            if (transition is not null && reference.FormKey == portalReference!.FormKey)
                AddNativeDoorPortal(instance, transition, sourceSide);
            placed++;
        }
        var coverage = _parityObservations.Snapshot();
        var missing = coverage.Missing.Count(identity =>
            identity.StartsWith(parityScope + "/", StringComparison.Ordinal));
        GD.Print(
            $"OPENNV_NATIVE_CELL_READY cell={cell.Cell.FormKey} placed={placed} lights={placedLights} " +
            $"residentPrototypes={_nativeNifPrototypes.Count} discovered={cell.References.Count} " +
            $"observed={cell.References.Count - missing} missing={missing} " +
            $"coverage={(missing == 0 ? "exact" : "diverged")} " +
            "source=live-retail-files");
        return root;
    }

    private static ParityCategory ParityCategoryFor(string signature) => signature switch
    {
        "NPC_" or "CREA" => ParityCategory.Actor,
        "LIGH" => ParityCategory.Renderer,
        "SOUN" => ParityCategory.Audio,
        _ => ParityCategory.World,
    };

    private static byte[] NativeReferenceState(
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject,
        string disposition)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteText(writer, reference.FormKey.ToString());
        WriteText(writer, reference.EditorId);
        WriteText(writer, reference.Base.ToString());
        WriteText(writer, baseObject.Signature);
        WriteText(writer, baseObject.EditorId);
        WriteText(writer, disposition);
        writer.Write(reference.Flags);
        foreach (var value in reference.Position)
            writer.Write(value);
        foreach (var value in reference.RotationRadians)
            writer.Write(value);
        writer.Write(reference.Scale);
        WriteText(writer, baseObject.ModelPath ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private void AddNativeDoorPortal(
        Node3D doorInstance,
        FalloutDoorTransition transition,
        bool sourceSide)
    {
        var reference = sourceSide ? transition.SourceDoor : transition.DestinationDoor;
        var destination = sourceSide ? transition.DestinationDoor : transition.SourceDoor;
        var destinationScene = sourceSide ? transition.DestinationScene : transition.SourceScene;
        var destinationWorld = sourceSide
            ? transition.DestinationWorldspace
            : transition.SourceScene.Cell.Worldspace;
        var portal = new RuntimeNativeDoorPortal();
        portal.Configure(
            reference.FormKey,
            destination.FormKey,
            destinationScene.Cell.FormKey,
            destinationWorld,
            () => StreamNativeDoorTransition(transition, toDestination: sourceSide));
        doorInstance.AddChild(portal);
    }

    private void StreamNativeDoorTransition(
        FalloutDoorTransition transition,
        bool toDestination)
    {
        if (_nativeOpeningStageDriver is not { Stage: FalloutNativeCampaignSave.CompletedOpeningStage })
        {
            GD.Print(
                $"OPENNV_NATIVE_DOOR_STREAM_BLOCKED stage=" +
                $"{_nativeOpeningStageDriver?.QuestEditorId}:" +
                $"{_nativeOpeningStageDriver?.Stage} required=VCG01:" +
                $"{FalloutNativeCampaignSave.CompletedOpeningStage}");
            return;
        }
        var current = _nativeCurrentCellRoot ??
            throw new InvalidOperationException("Native door activation has no current CELL root.");
        var expectedCell = toDestination
            ? transition.SourceScene.Cell.FormKey
            : transition.DestinationScene.Cell.FormKey;
        if (current.Name != $"NativeCell_{expectedCell}")
            throw new InvalidOperationException(
                $"Native door activation expected CELL {expectedCell}, found {current.Name}.");
        var targetScene = toDestination ? transition.DestinationScene : transition.SourceScene;
        var targetRoot = BuildNativeCellRoot(
            targetScene,
            transition,
            sourceSide: !toDestination);
        if (toDestination)
        {
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");
            targetRoot.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(
                FalloutLandscapeTransportResolver.Resolve(stack, transition),
                _configuration.World.GameUnitsToMeters));
        }
        AddChild(targetRoot);
        if (targetScene.Cell.Lighting is not null)
            AddNativeCellEnvironment(targetRoot, targetScene);
        else
            targetRoot.SetMeta(
                "opennv_exterior_environment_pending",
                transition.DestinationWorldspace.ToString());
        var entry = toDestination
            ? transition.SourceDoor.Teleport!
            : transition.DestinationDoor.Teleport!;
        var player = _nativePlayer ??
            throw new InvalidOperationException("Native door activation has no authoritative player.");
        player.Teleport(TeleportTransform(entry));
        _nativeCurrentCellRoot = targetRoot;
        current.QueueFree();
        _nativeOpeningStageDriver.PersistWorldState(targetScene.Cell.FormKey);
        GD.Print(
            $"OPENNV_NATIVE_DOOR_STREAM source={expectedCell} destination={targetScene.Cell.FormKey} " +
            $"door={(toDestination ? transition.SourceDoor.FormKey : transition.DestinationDoor.FormKey)} " +
            $"entry={entry.Door} world={transition.DestinationWorldspace} " +
            "source=live-retail-files");
    }

    private void AddNativePlacedLight(
        Node3D root,
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject)
    {
        root.AddChild(RuntimeNativePlacedLightBuilder.Build(
            reference,
            baseObject,
            ReferenceTransform(reference),
            _configuration.World.GameUnitsToMeters,
            _configuration.Renderer.PointLightEnergyScale,
            _configuration.Renderer.MinimumPointLightEnergy,
            _configuration.Renderer.AuthoredPointLightShadows));
    }

    private void AddNativePlayer(
        Node3D cellRoot,
        FalloutCellScene initialCell,
        bool addOpeningInteractions)
    {
        if (_nativePlayer is not null)
            throw new InvalidOperationException("Native player was already created.");
        var start = FalloutNewGamePlayerStartResolver.Resolve(
            _nativePluginStack ?? throw new InvalidOperationException("Native plugin stack was not indexed."),
            initialCell);
        var marker = start.Reference;
        _nativePlayer = new RuntimeNativePlayer();
        _nativePlayer.Configure(_configuration, ReferenceTransform(marker));
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
            _nativePlayer.RestoreTransform(
                restore.State.PlayerPosition,
                restore.State.PlayerRotation);
        AddChild(_nativePlayer);
        _nativeOpeningStageDriver = new RuntimeNativeOpeningStageDriver();
        _nativeOpeningStageDriver.Configure(
            _nativeOpeningTransitions ??
                throw new InvalidOperationException("Native opening transition graph was not resolved."),
            _nativeOpeningControls ??
                throw new InvalidOperationException("Native opening control graph was not resolved."),
            _nativePlayer,
            _nativeOpeningGrant ??
                throw new InvalidOperationException("Native opening inventory grant was not resolved."),
            _nativeRaceSexContract ??
                throw new InvalidOperationException("Native race/sex contract was not resolved."),
            _nativeVigorContract ??
                throw new InvalidOperationException("Native Vigor contract was not resolved."),
            _nativeTagSkillContract ??
                throw new InvalidOperationException("Native tag-skill contract was not resolved."),
            _nativeTraitFarewellContract ??
                throw new InvalidOperationException("Native trait/farewell contract was not resolved."),
            _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed."),
            RequireOption(_options, "save-path"),
            RuntimeLiveContentSource.Current?.SaveCompatibilityId ??
                throw new InvalidOperationException("Native save compatibility identity is absent."),
            initialCell.Cell.FormKey,
            restore,
            _configuration.Player.DesktopInput.Activate.Action,
            "VCG00",
            0);
        AddChild(_nativeOpeningStageDriver);
        if (addOpeningInteractions)
        {
            AddNativeVigorInteraction(
                cellRoot,
                _nativeVigorContract ??
                    throw new InvalidOperationException("Native Vigor contract was not resolved."),
                _nativeOpeningStageDriver);
            AddNativeFarewellInteraction(
                cellRoot,
                _nativeTraitFarewellContract ??
                    throw new InvalidOperationException("Native trait/farewell contract was not resolved."),
                _nativeOpeningStageDriver);
        }
        if (_nativeSkipOpeningMovie && restore is null)
            _nativeOpeningStageDriver.CompleteBlocker("playbink");
        GD.Print(
            $"OPENNV_NATIVE_PLAYER_START reference={marker.FormKey} editorId={marker.EditorId} " +
            $"quest={start.Quest} stage={start.Stage} candidates={start.Candidates.Count} " +
            $"packageLinked={start.Candidates.Count(value => value.DirectPackageLocationCount > 0)} " +
            $"restored={(restore is not null)} inventory={restore?.Inventory.Items.Count ?? 0} " +
            "owner=character-body controls=live-qust-sctx source=live-retail-files");
    }

    private void AddNativeVigorInteraction(
        Node3D cellRoot,
        FalloutNativeVigorContract contract,
        RuntimeNativeOpeningStageDriver driver)
    {
        var dimensions = contract.TriggerDimensionsGameUnits;
        var trigger = new RuntimeNativeVigorTrigger
        {
            Transform = ReferenceTransform(contract.TriggerReference),
        };
        trigger.Configure(
            new Vector3(dimensions[0], dimensions[2], dimensions[1]) *
                _configuration.World.GameUnitsToMeters,
            _configuration.Player.CollisionLayer,
            driver.EnterVigorTrigger);
        cellRoot.AddChild(trigger);
        var tester = cellRoot.GetChildren().OfType<Node3D>()
            .SingleOrDefault(value =>
                value.HasMeta("opennv_reference_form_key") &&
                value.GetMeta("opennv_reference_form_key").AsString() ==
                    contract.TesterReference.FormKey.ToString());
        if (tester is null)
        {
            var baseObject = _nativeInitialCell?.BaseObjects.GetValueOrDefault(contract.TesterReference.Base);
            throw new InvalidDataException(
                $"Native Vigor tester presentation is absent: {contract.TesterReference.FormKey} " +
                $"base={contract.TesterReference.Base} flags=0x{contract.TesterReference.Flags:x8} " +
                $"initiallyDisabled={FalloutCellSceneReader.IsInitiallyDisabled(contract.TesterReference)} " +
                $"signature={baseObject?.Signature ?? "missing"} model={baseObject?.ModelPath ?? "none"}.");
        }
        var activator = new RuntimeNativeVigorActivator();
        activator.Configure(driver.ActivateVigorTester);
        tester.AddChild(activator);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_READY trigger={contract.TriggerReference.FormKey} " +
            $"tester={contract.TesterReference.FormKey} total={contract.RequiredTotal} " +
            "source=live-refr-acti-scpt-xprm writes=0");
    }

    private void AddNativeFarewellInteraction(
        Node3D cellRoot,
        FalloutNativeTraitFarewellContract contract,
        RuntimeNativeOpeningStageDriver driver)
    {
        var dimensions = contract.ExitTriggerDimensionsGameUnits;
        var trigger = new RuntimeNativeFarewellTrigger
        {
            Transform = ReferenceTransform(contract.ExitTriggerReference),
        };
        trigger.Configure(
            new Vector3(dimensions[0], dimensions[2], dimensions[1]) *
                _configuration.World.GameUnitsToMeters,
            _configuration.Player.CollisionLayer,
            driver.EnterFarewellTrigger);
        cellRoot.AddChild(trigger);
        GD.Print(
            $"OPENNV_NATIVE_FAREWELL_READY trigger={contract.ExitTriggerReference.FormKey} " +
            $"traits={contract.Traits.Count} maximum={contract.MaximumTraits} " +
            $"stage={contract.ExitTriggerFromStage}->{contract.FarewellStage}->" +
            $"{contract.CompletedStage} source=live-refr-acti-scpt-info-qust writes=0");
    }

    private void AddNativeFallout3PlayerCamera(Node3D root, FalloutCellScene cell)
    {
        var expected = new FalloutFormKey(
            NativeFallout3InitialCellPlugin,
            NativeFallout3PlayerStartObjectId);
        var marker = cell.References.SingleOrDefault(reference => reference.FormKey == expected) ??
            throw new InvalidDataException(
                $"Fallout 3 initial CELL {cell.Cell.FormKey} has no exact player start {expected}.");
        var camera = new Camera3D
        {
            Name = $"NativePlayerStart_{marker.FormKey}",
            Transform = ReferenceTransform(marker),
            Current = true,
        };
        root.AddChild(camera);
        GD.Print(
            $"OPENNV_NATIVE_FO3_PLAYER_START reference={marker.FormKey} editorId={marker.EditorId} " +
            "source=standalone-fallout3");
    }

    private void AddNativeCellEnvironment(Node3D root, FalloutCellScene cell)
    {
        var lighting = cell.Cell.Lighting ??
            throw new InvalidDataException(
                $"Native CELL {cell.Cell.FormKey} has no resolved XCLL/LGTM lighting.");
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = ByteColor(lighting.FogRgb),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ByteColor(lighting.AmbientRgb),
            AmbientLightEnergy = _configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(_configuration.Renderer.ToneMapper),
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = ByteColor(lighting.FogRgb),
            FogLightEnergy = _configuration.Renderer.FogLightEnergy,
            FogDensity = _configuration.Renderer.FogDensity,
            FogDepthBegin = lighting.FogNear * _configuration.World.GameUnitsToMeters,
            FogDepthEnd = lighting.FogFar * _configuration.World.GameUnitsToMeters,
            FogDepthCurve = lighting.FogPower,
        };
        root.AddChild(new WorldEnvironment
        {
            Name = $"NativeEnvironment_{cell.Cell.FormKey}",
            Environment = environment,
        });
        var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
            lighting.DirectionalXDegrees,
            lighting.DirectionalZDegrees);
        root.AddChild(new DirectionalLight3D
        {
            Name = $"NativeDirectional_{cell.Cell.FormKey}",
            Transform = new Transform3D(
                RetailLighting.DirectionalLightBasis(surfaceToLight), Vector3.Zero),
            LightColor = RetailLighting.GodotLightColor(ByteColor(lighting.DirectionalRgb)),
            LightEnergy = lighting.DirectionalFade * _configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = _configuration.ActorReview.DirectionalShadows,
        });
    }

    private Transform3D ReferenceTransform(FalloutPlacedReference reference) => new(
        GamebryoCoordinate.ConvertReferenceEuler(
            new Vector3(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]),
            reference.Scale),
        GamebryoCoordinate.ConvertVector(
            new Vector3(reference.Position[0], reference.Position[1], reference.Position[2])) *
        _configuration.World.GameUnitsToMeters);

    private Transform3D TeleportTransform(FalloutTeleportDestination destination) => new(
        GamebryoCoordinate.ConvertReferenceEuler(
            new Vector3(
                destination.RotationRadians[0],
                destination.RotationRadians[1],
                destination.RotationRadians[2]),
            1.0f),
        GamebryoCoordinate.ConvertVector(new Vector3(
            destination.Position[0], destination.Position[1], destination.Position[2])) *
        _configuration.World.GameUnitsToMeters);

    private static Color ByteColor(IReadOnlyList<byte> rgb) => new(
        rgb[0] / (float)byte.MaxValue,
        rgb[1] / (float)byte.MaxValue,
        rgb[2] / (float)byte.MaxValue);
}
