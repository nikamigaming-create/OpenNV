using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    // Construct the same source-owned HUD component without native presentation
    // or a campaign/Main call. This catches winning declaration/XML failures
    // before the much longer ordinary startup diagnostic.
    internal static int AuditOwnedPlayerControlHud(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        _ = FalloutExecutableStringTable.ReadPlayerReferenceHud(content.FalloutExecutablePath, main);
        var before = SHA256.HashData(File.ReadAllBytes(content.FalloutExecutablePath));
        var source = FalloutExecutableStringTable.ReadPlayerControlHud(content.FalloutExecutablePath, controls);
        var settings = records.IniSettings;
        var selected = new[]
        {
            Setting(FalloutPlayerControlHudSource.CrosshairSetting, FalloutIniCollection.Prefs, 1, 'b'),
            Setting(FalloutPlayerControlHudSource.SafeXSetting, FalloutIniCollection.Main, 15, 'i'),
            Setting(FalloutPlayerControlHudSource.SafeYSetting, FalloutIniCollection.Main, 15, 'i'),
            Setting(FalloutPlayerControlHudSource.WideXSetting, FalloutIniCollection.Main, 15, 'i'),
            Setting(FalloutPlayerControlHudSource.WideYSetting, FalloutIniCollection.Main, 15, 'i'),
            Setting(FalloutPlayerControlHudSource.WidthSetting, FalloutIniCollection.Renderer, 640, 'i'),
            Setting(FalloutPlayerControlHudSource.HeightSetting, FalloutIniCollection.Renderer, 480, 'i'),
        };
        var process = Guid.NewGuid();
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), content.StackId, process);
        var fields = scene.ControlFields;
        var input = FalloutDirectInputState.CreateHeadless(content.StackId, new(_ => null));
        var thread = scene.ThreadContext;
        var currentInterface = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), content.StackId, process);
        using var timer = new FalloutSourceFrameTimerState(FalloutSourceFrameTimer.Read(runtime.Receipt), new FalloutNativeSourceTickCounter(), process);
        using var clock = new FalloutRendererClockState(FalloutRendererClockConfiguration.Read(records, main), process);
        FalloutPlayerControlHudState? hud = null;
        try
        {
            scene.PublishControlCommandInput(input);
            var display = new FalloutPlayerControlDisplayState(source, content.StackId, process);
            display.ApplyDeclaredConfiguration(settings);
            display.PublishCurrentLayout(Guid.NewGuid(), "owned-offline-source-display-layout", () => fields.RequireActualInput(input));
            var ui = FalloutUiComponentStore.Open(records, content);
            var context = new FalloutPlayerControlHudContext(source, content.StackId, process, currentInterface, timer, clock, input, fields);
            hud = new(source, content.StackId, process, ui, settings, thread, context);
            scene.BindControlHud(hud, display);
            hud.Initialize(display, fields.ReadDisabledWord);
            var saved = hud.Capture(); var dimensions = display.Capture();
            var allowed = selected[0].Number == 0 ? 0xffbfu : 0xffffu;
            Require(saved is { InitializationReturned: true, Mode: 5, MenuMode: 8, VisibilityMask: 0,
                Call: { Returned: true, Allocation: { EnteredKind: 13, RestoredKind: 59, Returned: true },
                    Frame.Clock.HasReturnedUpdate: false, Frame.Clock.ScaledSecondsBits: 0, Frame.ControlMode: 0 } } &&
                saved.AllowedMask == allowed && saved.Tiles is { Count: 18 } && saved.Tiles.All(tile => tile.Alive && !tile.Animated) &&
                saved.Initialization is { Returned: true, Writes.Count: 3 } && currentInterface.Capture().Menus.Transitions.Count == 1,
                "Owned HUD constructor changed its actual winning settings, XML, source prior kind or constructor clock branch.");
            Require(dimensions.WidthSetting == selected[5] && dimensions.HeightSetting == selected[6] &&
                dimensions.Width == selected[5].Number && dimensions.Height == selected[6].Number &&
                SHA256.HashData(File.ReadAllBytes(content.FalloutExecutablePath)).AsSpan().SequenceEqual(before),
                "Owned HUD display lost its Renderer declarations or changed the read-only source executable.");
            var (program, instruction, command) = ReadOwnedBootstrapControlCommand(records);
            var player = records.RuntimeFormKey(0x14);
            var actorSource = FalloutCombatGroupDeclaration.ForExecutable(main.EngineSha256);
            using var common = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), content.StackId,
                actor => FalloutCombatActorSource.Read(records, actorSource, actor),
                _ => throw new InvalidOperationException("Owned POV constructor queried unrelated gameplay pools."),
                _ => throw new InvalidOperationException("Owned POV constructor queried a native body."));
            common.Construct(player, 1, FalloutDetectionProcessLevel.High);
            var observation = new FalloutPlayerControlCommandObservation(program.Source.FormKey,
                "owned-offline-original-compiled-control-command", Guid.NewGuid(), command.Enable, command.Arguments);
            var fighting = new FalloutPlayerControlFightingOwner(controls, content.StackId, process, common, player, 1,
                () => scene.ReadControlCommandMoverFlags(observation.Observation),
                () => clock.ReadMainControlMode(process), () => { });
            scene.ApplySourceControlCommand(observation, new FalloutPlayerControlFlagConsumers(controls, content.StackId, process,
                observation.Observation, fields, hud, fighting, scene));
            var returned = scene.Capture();
            var currentPointOfView = returned.PointOfView ?? throw new InvalidDataException("Owned command omitted its actual POV fields.");
            Require(returned is { PlayerView: 0, FirstPerson: null, ControlFields: { DisabledWord: 0x1f, FlagStores: 2, Call.Returned: true },
                PointOfView: { DesiredViewStores: 1, SharedScalarStores: 1, SharedScalarBits: 0, DesiredView: 0, Calls.Count: 1 } } &&
                currentPointOfView.Calls[0] is { GetterReturned: true, FirstPerson: null, Returned: true, DesiredViewStored: not null, SharedScalarStored: not null },
                "Actual owned five-operand command omitted mandatory HUD/process/POV stores or invented a native camera callback.");
            var currentControlFields = returned.ControlFields ?? throw new InvalidDataException("Owned command omitted its current flag fields.");
            var controlCall = currentControlFields.Call ?? throw new InvalidDataException("Owned command omitted its original flag writer.");
            Console.WriteLine("OPENNV_OWNED_PLAYER_CONTROL_HUD_PASS " + JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-player-control-HUD-constructor/v2", game = content.Game, stack = content.StackId,
                executable = main.EngineSha256, source = source.Identity,
                settings = selected.Select(value => new { value.Declaration.Name, kind = value.Declaration.Kind.ToString(),
                    collection = value.Declaration.Collection.ToString(), canonical = value.Declaration.NumericDefault, current = value.Number, value.Origin }),
                actualWinningXml = true, retainedInterfaceQueue = true, currentSourceDisplay = dimensions,
                hudInitializationReturned = saved.InitializationReturned, saved.AllowedMask,
                tileValues = saved.Tiles, sourceReadOnly = true, framesRecorded = false,
                originalCompiledControl = new { source = program.Source.FormKey, program.Scope.ScopeSha256,
                    program.ProgramSha256, instruction.Offset, originalOperands = command.Arguments, mask = controlCall.Mask,
                    controlCall.Returned, pointOfView = returned.PointOfView },
                gameplay = "UNEXECUTED", ordinaryStartup = "UNEXECUTED"
            }));
            return 0;
        }
        finally { hud?.Retire(); currentInterface.Retire(); thread.Retire(); input.Retire(); }

        FalloutIniValue Setting(string name, FalloutIniCollection collection, uint initial, char kind)
        {
            var value = settings.Find(collection, name);
            Require(value is not null && value.Declaration.Name == name && value.Declaration.Collection == collection &&
                value.Declaration.Kind == kind && value.Declaration.Payload == initial && value.Number is { } number &&
                !string.IsNullOrWhiteSpace(value.Origin) &&
                (kind == 'b' ? number is 0 or 1 : number == Math.Truncate(number) && number is >= int.MinValue and <= int.MaxValue),
                "Owned HUD lost a required typed source constructor input: " + name + " in " + collection);
            return value!;
        }
    }

    private static (FalloutCompiledScriptProgram Program, FalloutCompiledInstruction Instruction, FalloutPlayerControlCommand Command)
        ReadOwnedBootstrapControlCommand(FalloutPluginStack records)
    {
        var record = records.GetEffective(new("Fallout3.esm", 0x1f388));
        var fields = record.ReadSubrecords().ToArray(); var stage = -1;
        var commands = new List<(FalloutCompiledScriptProgram, FalloutCompiledInstruction, FalloutPlayerControlCommand)>();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature == "INDX") { stage = index; continue; }
            if (fields[index].Signature != "QSDT" || stage < 0) continue;
            var scope = FalloutScriptScope.QuestEntry(record, stage, index);
            if (!scope.Compiled) continue;
            var program = FalloutCompiledScriptProgram.Read(record, scope, false);
            foreach (var instruction in program.Instructions.Where(row => row.Opcode >= 0x1000))
            {
                var declaration = FalloutCompiledCommandDeclarations.Get(instruction.Opcode, records);
                if (declaration.Name != "DisablePlayerControls") continue;
                var operands = new FalloutCompiledOperandContext(
                    _ => throw new InvalidDataException("Selected control command queried a nonliteral reference."),
                    _ => throw new InvalidDataException("Selected control command queried a nonliteral variable."),
                    _ => throw new InvalidDataException("Selected control command queried a nonliteral global."),
                    _ => throw new InvalidDataException("Selected control command entered a query."), new(),
                    opcode => FalloutCompiledCommandDeclarations.Get(opcode, records));
                var decoded = FalloutCompiledOperands.Command(instruction.Opcode, instruction.Receiver, instruction.Payload, operands);
                Require(decoded.Arguments.Count == 5 && decoded.Arguments.All(argument => argument.SourceParameterType == 1 &&
                    argument.Value.Kind == FalloutScriptValueKind.Number && argument.Value.Number == 1),
                    "Actual compiled bootstrap no longer declares five explicit source Int32 literal-one control operands.");
                var command = new FalloutPlayerControlCommand(false, decoded.Arguments.Select(argument => argument.Value.Number != 0).ToArray());
                Require(FalloutPlayerControlFieldsState.CommandMask(command) == 0x1f,
                    "Actual compiled bootstrap command was replaced by source omitted argument defaults.");
                commands.Add((program, instruction, command));
            }
        }
        return commands.Count == 1 ? commands[0] : throw new InvalidDataException("Owned bootstrap has no unique actual compiled control command.");
    }
}
