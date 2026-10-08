using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeSpecialBookMenuAudit : Control
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args is ["--standalone", var root, var diagnostic]) await Verify(root, null, null, diagnostic, []);
            else
            {
                if (args.Length < 4) throw new ArgumentException("Use --standalone <owned-installation> <private-diagnostic>, or <owned-installation> <mod> <mod-root> <private-diagnostic> [dependencies].");
                await Verify(args[0], args[1], args[2], args[3], args[4..]);
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Verify(string baseRoot, string? mod, string? modRoot, string diagnostic, string[] dependencies)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("SPECIAL book pixels require a native renderer.");
        var protectedRoots = new List<string> { ProjectSettings.GlobalizePath("res://../"), baseRoot };
        protectedRoots.AddRange(dependencies);
        if (modRoot is not null) protectedRoots.Add(modRoot);
        if (mod is null)
        {
            var installation = NativeGameInstallation.Detect(baseRoot);
            protectedRoots.Add(installation.InstallRoot);
            var game = installation.Game switch
            {
                NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
                NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
                _ => throw new NotSupportedException("SPECIAL book audit requires an owned Fallout 3/New Vegas installation."),
            };
            RuntimeLiveContentSource.Configure(baseRoot, game);
        }
        else
        {
            var installation = new FalloutModStackSelection([new(mod, modRoot!, dependencies)]).Resolve(baseRoot);
            protectedRoots.Add(installation.BaseInstallation.InstallRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        }
        RuntimeNativeSpecialAllocationEntry? entry = null; Godot.Timer? clock = null;
        var priorPause = GetTree().Paused; var priorMouse = Input.MouseMode; var success = false; var diagnosticCreated = false;
        try
        {
            var source = RuntimeLiveContentSource.Current!; using var records = FalloutPluginStack.Load(source.PluginSources);
            diagnostic = DiagnosticPath(diagnostic, protectedRoots.Concat(source.ContentRoots));
            var sourcePaths = new[] { NativeOwnedSpecialBookMenu.MenuPath, "meshes/terminals/babybook02.nif" };
            var identities = new List<string>(); var hashes = new List<byte[]>();
            foreach (var path in sourcePaths)
            {
                if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
                identities.Add(identity); hashes.Add(SHA256.HashData(bytes));
            }
            VerifyFailedSurface(source);
            var player = records.GetEffective(records.RuntimeFormKey(7));
            var playerData = player.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (playerData.Length != 11) throw new NotSupportedException("Owned player SPECIAL layout is unbound.");
            var values = playerData.Span.Slice(4, 7).ToArray().Select(value => (int)value).ToArray();
            var baseWrites = new List<(int Slot, int Value)>();
            var rejectRead = false; var rejectWrite = false;
            var binding = new FalloutSpecialAllocationBinding("owned-fixture-player-base-with-explicit-permanent-pool-zero",
                slot => rejectRead ? throw new NotSupportedException("unbound-permanent-SPECIAL-read") : values[slot - 5],
                (slot, value) => { if (rejectWrite) throw new NotSupportedException("unbound-SPECIAL-base-write"); baseWrites.Add((slot, value)); values[slot - 5] = value; });
            var ticks = 0; var modal = false; var accepted = 0; var released = 0; Exception? failed = null;
            clock = new() { WaitTime = .01, Autostart = true }; clock.Timeout += () => ticks++; AddChild(clock);
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(ticks > 0, "Owned fixture gameplay clock did not advance.");

            NativeOwnedSpecialBookMenu Open(int? budget)
            {
                var active = new RuntimeNativeSpecialAllocationEntry(); entry = active; AddChild(active);
                NativeOwnedSpecialBookMenu? result = null;
                active.Accepted += () => accepted++; active.Released += () => released++; active.Failed += error => failed = error;
                active.Configure((accept, fail) =>
                {
                    result = new(records, binding, budget, accept, fail);
                    return new(result, () => result.State);
                }, () => modal, value => modal = value);
                if (failed is not null) throw new InvalidDataException("Owned SPECIAL book could not open.", failed);
                return result ?? throw new InvalidDataException("Owned SPECIAL book has no menu.");
            }
            void Retire()
            {
                entry?.Free(); entry = null;
            }
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            JsonElement State(NativeOwnedSpecialBookMenu menu) => JsonSerializer.SerializeToElement(menu.State, options);
            int Page(NativeOwnedSpecialBookMenu menu) => State(menu).GetProperty("page").GetInt32();
            void Key(Key key)
            {
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            }
            void Pad(JoyButton button)
            {
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed }, true);
            }
            async Task<byte[]> Pixels(NativeOwnedSpecialBookMenu menu, bool visible = true)
            {
                for (var frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (failed is not null || menu.Error is not null) throw new InvalidDataException("Owned SPECIAL book failed.", failed);
                Check(menu.IsVisibleInTree() == visible, "Owned SPECIAL book visibility disagrees with its modal lifecycle.");
                using var image = GetViewport().GetTexture().GetImage();
                Check(!image.IsEmpty(), "Owned SPECIAL book has no native pixels.");
                var pixels = image.GetData(); Check(!visible || pixels.Distinct().Count() > 8, "Owned SPECIAL book pixels are blank."); return pixels;
            }
            void Click(NativeOwnedSpecialBookMenu menu, string geometry)
            {
                var target = menu.Targets.Single(target => target.Geometry == geometry);
                Check(target.InFront && new Rect2(Vector2.Zero, menu.Size).HasPoint(target.Center) && target.Bounds.Size.X > 0 && target.Bounds.Size.Y > 0,
                    $"Owned SPECIAL target is outside its view: {geometry} center={target.Center} bounds={target.Bounds} page={Page(menu)}.");
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        Position = menu.GlobalPosition + target.Center,
                        GlobalPosition = menu.GlobalPosition + target.Center,
                        ButtonIndex = MouseButton.Left,
                        Pressed = pressed
                    }, true);
            }
            async Task FinishTurn(NativeOwnedSpecialBookMenu menu)
            {
                var deadline = Time.GetTicksMsec() + 10000;
                while (State(menu).GetProperty("turning").GetBoolean() && Time.GetTicksMsec() < deadline)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(!State(menu).GetProperty("turning").GetBoolean(), "Owned book animation never completed.");
                Check(menu.Animation.UnboundTextKeys.Count == 0, "Owned book animation lost a source text key.");
                await Pixels(menu);
            }

            Input.MouseMode = Input.MouseModeEnum.Captured;
            var book = Open(null); var cover = await Pixels(book); var pausedTicks = ticks;
            var defaultBudget = book.Declaration.DefaultBudget;
            Check(State(book).GetProperty("budget").GetInt32() == defaultBudget, "Book default budget did not follow its owned executable.");
            Check(Page(book) == 0 && book.Animation.SourceTimeSeconds == book.Animation.SequenceRange(book.Animation.ActiveSequence!).StartTime &&
                !book.Animation.IsProcessing(), "Owned book skipped its source cover page.");
            Check(GetTree().Paused && modal && ticks == pausedTicks && cover.Any(value => value != 0), "Book cover did not pause gameplay or render pixels.");
            Retire(); Check(!GetTree().Paused && !modal && Input.MouseMode == Input.MouseModeEnum.Captured && released == 1, "Cover cancellation did not restore prior input.");

            // Actual source activation prefix, in an isolated script host. This
            // is not ordinary traversal or a production command/save binding.
            using var world = new FalloutReferenceWorld(records);
            var reference = FalloutDialogueTopic.Find(records, "REFR", "CG01SpecialBookREF").FormKey;
            var cell = FalloutCellSceneReader.ParentCell(records.GetEffective(reference)) ?? throw new InvalidDataException("Owned book has no source cell.");
            world.LoadCell(FalloutCellSceneReader.Read(records, cell));
            var quest = FalloutDialogueTopic.Find(records, "QUST", "CG01").FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, 30);
            var declaredOrder = new List<string>(); short? declaredStage = null; int? declaredBudget = null;
            string? declaredCommand = null; string[]? declaredArguments = null;
            using (var rejectedWorld = new FalloutReferenceWorld(records))
            {
                rejectedWorld.LoadCell(FalloutCellSceneReader.Read(records, cell));
                var rejectedQuests = new FalloutQuestState(records); rejectedQuests.EnterStage(quest, 30); var attempts = 0;
                var rejectedScripts = new FalloutReferenceScripts(records, rejectedWorld, rejectedQuests, new((_, _) => false,
                    effect =>
                    {
                        Check(effect.Kind == FalloutReferenceEffectKind.SetStage && effect.Target == quest && declaredStage is null,
                            "Source book has an unbound activation-prefix effect.");
                        declaredStage = AdmittedQuestStage(effect.Stage); declaredOrder.Add("SetStage" + effect.Stage);
                        rejectedQuests.EnterStage(effect.Target!.Value, effect.Stage);
                    }, Command: (_, _, command, arguments) =>
                    {
                        Check(declaredCommand is null, "Source book dispatched overlapping menu commands.");
                        declaredCommand = command; declaredArguments = arguments.ToArray();
                        declaredBudget = command.ToLowerInvariant() switch
                        {
                            "ssbmp" or "showspecialbookmenuparams" when arguments.Count == 1 =>
                                int.Parse(arguments[0], System.Globalization.CultureInfo.InvariantCulture),
                            "ssbm" or "showspecialbookmenu" when arguments.Count == 0 => null,
                            _ => throw new NotSupportedException("Source book allocation command is unbound."),
                        };
                        declaredOrder.Add(command + string.Join(",", arguments)); attempts++;
                        throw new NotSupportedException("unbound-source-book-menu-owner");
                    }));
                var rejection = rejectedScripts.Activate(reference, records.RuntimeFormKey(0x14));
                Check(rejection.Error is not null && declaredStage is { } stage && rejectedQuests.StageDone(quest, stage) && attempts == 1,
                    "Unsupported source book command rolled back its executed stage prefix.");
                var stopped = JsonSerializer.Serialize(rejectedWorld.Get(reference).Capture());
                var repeated = rejectedScripts.Activate(reference, records.RuntimeFormKey(0x14));
                Check(repeated.Error == rejection.Error && repeated.Blocks == 0 && attempts == 1 &&
                    JsonSerializer.Serialize(rejectedWorld.Get(reference).Capture()) == stopped,
                    "Repeating activation cleared or replayed the unsupported source invocation.");
                // A stopped invocation retains its error before evaluating any
                // new source guard. Prove the actual completed-stage predicate
                // separately without clearing that fault or changing quest state.
                using var guardedWorld = new FalloutReferenceWorld(records);
                guardedWorld.LoadCell(FalloutCellSceneReader.Read(records, cell));
                var guardedEffects = 0; var guardedCommands = 0;
                var guardedScripts = new FalloutReferenceScripts(records, guardedWorld, rejectedQuests,
                    new((_, _) => false, _ => guardedEffects++, Command: (_, _, _, _) => guardedCommands++));
                var guarded = guardedScripts.Activate(reference, records.RuntimeFormKey(0x14));
                Check(guarded.Error is null && guardedEffects == 0 && guardedCommands == 0 &&
                    declaredStage is { } guardedStage && rejectedQuests.StageDone(quest, guardedStage),
                    "Completed source stage guard dispatched another activation effect or menu.");
            }
            var sourceStage = declaredStage ?? throw new InvalidDataException("Source book did not declare a stage prefix.");
            var sourceBudget = declaredBudget ?? defaultBudget;
            Check(declaredCommand is not null && declaredArguments is not null && declaredOrder.Count == 2,
                "Source book did not declare one stage and one menu request.");
            var order = new List<string>(); NativeOwnedSpecialBookMenu? activated = null;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                effect =>
                {
                    Check(effect.Kind == FalloutReferenceEffectKind.SetStage && effect.Target == quest && effect.Stage == sourceStage, "Book activation guessed another source effect.");
                    order.Add("SetStage" + effect.Stage); quests.EnterStage(quest, effect.Stage);
                }, Command: (_, _, command, arguments) =>
                {
                    Check(command.Equals(declaredCommand, StringComparison.OrdinalIgnoreCase) && arguments.SequenceEqual(declaredArguments!), "Winning book command/parameter changed.");
                    order.Add(command + string.Join(",", arguments)); activated = Open(declaredBudget);
                }));
            var result = scripts.Activate(reference, records.RuntimeFormKey(0x14));
            Check(result.Error is null && order.SequenceEqual(declaredOrder) && quests.StageDone(quest, sourceStage),
                "Actual winning book source did not preserve its stage-before-menu prefix: " + result.Error);
            book = activated ?? throw new InvalidDataException("Winning book activation did not reach its isolated menu callback.");
            var reopenedCover = await Pixels(book); pausedTicks = ticks;
            Check(book.FindChildren("*", "", true, false).Single(node => node.HasMeta("opennv_menu_authored_light_count"))
                .GetMeta("opennv_menu_authored_light_count").AsInt32() == 1, "Winning BabyBook02 light was replaced by a synthetic fallback.");
            Check(Page(book) == 0 && cover.AsSpan().SequenceEqual(reopenedCover), "Reopening did not restore the source cover pose/pixels.");
            var transitions = new List<string>();
            Click(book, "LookInside_Btn:0"); transitions.Add(book.Animation.ActiveSequence!);
            Pad(JoyButton.RightShoulder); Check(Page(book) == 1, "Next-page input bypassed the source animation lock.");
            await FinishTurn(book);
            Check(Page(book) == 1, "Look Inside did not open the first authored attribute.");
            if (values.Sum() >= sourceBudget) { Key(Godot.Key.Left); await Pixels(book); }
            Check(values.Sum() < sourceBudget, "Source book could not retain an ordinary decrease before the increase fixture.");
            var first = await Pixels(book); var before = values[0]; var initialWrites = baseWrites.Count;
            Key(Godot.Key.Right); var changed = await Pixels(book);
            Check(values[0] == before + 1 && baseWrites.Count == initialWrites + 1 && !first.AsSpan().SequenceEqual(changed), "Native key input did not change the source digit and immediate base owner.");
            Key(Godot.Key.Left); var restoredPixels = await Pixels(book);
            Check(values[0] == before && first.AsSpan().SequenceEqual(restoredPixels), "Native reverse input did not restore the owned digit pixels.");
            Click(book, "P1_Increase_Btn:0"); await Pixels(book); Check(values[0] == before + 1, "Source pointer increase did not mutate the base owner.");
            Retire(); Check(values[0] == before + 1 && !GetTree().Paused && !modal, "Cancelling rolled back an already-written SPECIAL value.");
            book = Open(sourceBudget); await Pixels(book); pausedTicks = ticks;
            Check(Page(book) == 0 && State(book).GetProperty("values")[0].GetInt32() == before + 1, "New book session lost the live owner or cover semantics.");
            Click(book, "LookInside_Btn:0"); await FinishTurn(book);
            if (values.Sum() >= sourceBudget) { Key(Godot.Key.Left); await Pixels(book); }
            for (var page = 2; page <= 9; page++)
            {
                Pad(JoyButton.RightShoulder); transitions.Add(book.Animation.ActiveSequence!); await FinishTurn(book);
                Check(Page(book) == page, "Source forward book transition was replaced or stopped at review.");
            }
            Check(book.Targets.Count == 0, "The source back cover acquired an invented pointer target.");
            Pad(JoyButton.RightShoulder); Check(Page(book) == 9 && accepted == 0, "The book exceeded its last page or submitted from next-page input.");
            for (var page = 8; page >= 0; page--)
            {
                Pad(JoyButton.LeftShoulder); transitions.Add(book.Animation.ActiveSequence!); await FinishTurn(book);
                Check(Page(book) == page, "Source backward book transition was replaced or skipped the cover.");
            }
            Check(transitions.Distinct(StringComparer.Ordinal).Count() == 18 && transitions.Contains(book.Declaration.ForwardSequences[^1]) &&
                transitions.Contains(book.Declaration.BackwardSequences[0]), "The owned fixture did not execute all eighteen source transitions.");
            Pad(JoyButton.LeftShoulder); Check(Page(book) == 0, "The book turned before its cover.");
            for (var page = 1; page <= 8; page++) { Pad(JoyButton.RightShoulder); await FinishTurn(book); }
            Click(book, "AllDone_Btn:0"); await Pixels(book); Check(accepted == 0 && GetTree().Paused, "Incomplete SPECIAL points were accepted.");
            Key(Godot.Key.Down); await Pixels(book);
            Check(State(book).GetProperty("index").GetInt32() == 2, "Source index navigation did not select Perception.");
            Click(book, "Index_PerceptionIncrease_Btn:0"); await Pixels(book);
            Key(Godot.Key.Up); await Pixels(book);
            var session = new FalloutSpecialAllocationSession(sourceBudget, binding);
            var allocationInputs = 0;
            while (session.Remaining > 0)
            {
                Check(++allocationInputs <= FalloutSpecialAllocationSession.AttributeCount * FalloutSpecialAllocationSession.Maximum,
                    "Source book allocation did not consume its bounded ordinary inputs.");
                var available = Enumerable.Range(0, values.Length).FirstOrDefault(index => values[index] < FalloutSpecialAllocationSession.Maximum, -1);
                Check(available >= 0, "Source book has remaining points without an admissible attribute.");
                Click(book, "Index_" + FalloutNativeVigorResolver.AttributeNames[available] + "Increase_Btn:0"); await Pixels(book);
            }
            Check(ticks == pausedTicks && order.Count == 2 && quests.Stage(quest) == sourceStage, "Menu input advanced paused gameplay or guessed source stage completion.");
            using (var image = GetViewport().GetTexture().GetImage())
            {
                var png = image.SavePngToBuffer(); Check(png.Length > 0, "SPECIAL diagnostic encoding failed.");
                using var output = CreateDiagnostic(diagnostic);
                diagnosticCreated = true; output.Write(png);
            }
            var beforeDoneWrites = baseWrites.Count; Click(book, "AllDone_Btn:0"); await Pixels(book, false);
            Pad(JoyButton.X); await Pixels(book, false);
            Check(accepted == 1 && baseWrites.Count == beforeDoneWrites && !GetTree().Paused && !modal && Input.MouseMode == Input.MouseModeEnum.Captured &&
                order.Count == 2 && quests.Stage(quest) == sourceStage, "Done committed a draft, repeated acceptance, changed a quest, or leaked input.");
            Retire();
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(ticks > pausedTicks, "Done did not resume the gameplay clock.");

            foreach (var pause in new[] { false, true })
                foreach (var priorModal in new[] { false, true })
                {
                    GetTree().Paused = pause; modal = priorModal; var initialMouse = priorModal ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
                    Input.MouseMode = initialMouse; var initialReleases = released;
                    book = Open(sourceBudget); await Pixels(book); Retire();
                    Check(GetTree().Paused == pause && modal == priorModal && Input.MouseMode == initialMouse && released == initialReleases + 1,
                        "Tree retirement lost a prior pause, modal or mouse state.");
                    var active = new RuntimeNativeSpecialAllocationEntry(); entry = active; AddChild(active);
                    Exception? rejection = null; active.Failed += error => rejection = error; active.Released += () => released++;
                    active.Configure((_, _) => throw new NotSupportedException("unbound-permanent-SPECIAL-owner"), () => modal, value => modal = value);
                    Check(rejection is not null && active.Error == rejection.Message && GetTree().Paused && modal, "Unbound SPECIAL owner was hidden or silently resumed gameplay.");
                    active.ReleasePause(); active.ReleasePause(); Retire();
                    Check(GetTree().Paused == pause && modal == priorModal && Input.MouseMode == initialMouse && released == initialReleases + 2,
                        "Failed owner release did not restore prior state exactly once.");
                    foreach (var failureKind in new[] { "constructor-read", "live-read", "base-write", "accept-callback" })
                    {
                        var failureReleases = released; var failureWrites = baseWrites.Count;
                        rejectRead = failureKind == "constructor-read"; failed = null;
                        active = new(); entry = active; AddChild(active);
                        active.Released += () => released++; active.Failed += error => failed = error;
                        NativeOwnedSpecialBookMenu? failedBook = null;
                        active.Configure((accept, fail) =>
                        {
                            failedBook = new(records, binding, sourceBudget, accept, fail);
                            return new(failedBook, () => failedBook.State);
                        }, () => modal, value => modal = value);
                        if (failureKind != "constructor-read")
                        {
                            var current = failedBook ?? throw new InvalidDataException("Failure fixture has no source book.");
                            await Pixels(current);
                            if (failureKind == "live-read") rejectRead = true;
                            if (failureKind == "base-write")
                            {
                                rejectWrite = true; Pad(JoyButton.RightShoulder); await FinishTurn(current); Key(Godot.Key.Left);
                            }
                            if (failureKind == "accept-callback")
                            {
                                active.Accepted += () => throw new NotSupportedException("unbound-allocation-accept-owner"); Pad(JoyButton.X);
                            }
                            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        Check(failed is not null && active.Error == failed.Message && GetTree().Paused && modal && baseWrites.Count == failureWrites,
                            "Source SPECIAL owner failure was hidden, mutated a draft or released gameplay: " + failureKind);
                        Check(JsonSerializer.SerializeToElement(active.State, options).GetProperty("error").GetString() == failed!.Message,
                            "SPECIAL failure telemetry repeated its failed permanent query.");
                        active.ReleasePause(); active.ReleasePause(); Retire(); rejectRead = false; rejectWrite = false; failed = null;
                        Check(GetTree().Paused == pause && modal == priorModal && Input.MouseMode == initialMouse && released == failureReleases + 1,
                            "Source SPECIAL failure retirement lost prior input state: " + failureKind);
                    }
                }
            foreach (var (path, hash) in sourcePaths.Zip(hashes))
                Check(source.TryRead(path, null, out var bytes, out _) && hash.AsSpan().SequenceEqual(SHA256.HashData(bytes)), "Owned SPECIAL book inputs were changed.");
            success = true;
            GD.Print("OPENNV_NATIVE_SPECIAL_BOOK_PASS " + JsonSerializer.Serialize(new
            {
                identities,
                menuId = 1060,
                sourceXmlInput = true,
                sourceBookModel = true,
                authoredLight = true,
                all18SourceTransitions = true,
                coverPage0 = true,
                backCoverPage9 = true,
                source.Game,
                defaultBudget,
                sourceBudget,
                sourceStage,
                sourceCommand = declaredCommand,
                sourceArguments = declaredArguments,
                sourceActivationOrder = declaredOrder,
                sourceNoReset = true,
                pointer = true,
                keyAxes = true,
                liveMutation = true,
                reversePixelsRestored = true,
                cancelRetainsEdits = true,
                reopenCoverPixels = true,
                doneNoWriteOrStage = true,
                oneAcceptance = true,
                pausedClock = true,
                priorPauseModalMouseRestoredOnce = true,
                constructorLiveReadWriteAcceptFailure = true,
                failedSurfaceNodesFreed = true,
                failureTelemetryReadable = true,
                unsupportedActivationPrefixRetained = true,
                unsupportedActivationLatched = true,
                completedStageGuard = true,
                failureVisible = true,
                sourceReadonly = true,
                recording = false,
                boundary = "isolated-owned-book-and-explicit-actor-value-callbacks;production-binding-save-timing-retail-and-XR-unverified"
            }));
        }
        finally
        {
            entry?.Free(); clock?.Free(); GetTree().Paused = priorPause; Input.MouseMode = priorMouse; RuntimeLiveContentSource.Clear();
            if (!success && diagnosticCreated) File.Delete(diagnostic);
        }
    }
    private static void VerifyFailedSurface(RuntimeLiveContentSource content)
    {
        Check(OS.IsDebugBuild(), "Owned menu failure-lifetime proof requires debug orphan-node telemetry.");
        const string path = "meshes/terminals/babybook02.nif";
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var source = FalloutNifFile.Read(bytes);
        Check(source.Roots.Count > 0, "Owned book has no visual root for its failure-lifetime fixture.");
        var unsupportedRoot = source.Blocks.First(block => block.TypeName == "NiControllerSequence").Index;
        // Redirect only a private in-memory copy's root to a non-visual source
        // block. The real builder must reject it without orphaning menu nodes.
        var rejectedBytes = bytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(rejectedBytes.AsSpan(rejectedBytes.Length - source.Roots.Count * sizeof(int)), unsupportedRoot);
        var rejectedSource = FalloutNifFile.Read(rejectedBytes);
        var nodes = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        Exception? rejection = null;
        try
        {
            var surface = new NativeOwnedNifMenuSurface([new(path, rejectedSource, Transform3D.Identity)],
                new(Transform3D.Identity, _ => 1, 1, 5000, 20, Vector3.One));
            surface.Free();
        }
        catch (NotSupportedException error) { rejection = error; }
        Check(rejection is not null && rejection.Message.Contains("NiControllerSequence", StringComparison.Ordinal),
            "Unsupported source visual root did not retain its construction failure.");
        Check(Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == nodes, "Failed owned menu surface orphaned native nodes.");
    }
    private static short AdmittedQuestStage(int stage) => stage is >= short.MinValue and <= short.MaxValue
        ? checked((short)stage) : throw new InvalidDataException("Source book stage exceeds the admitted quest-stage extent.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
