using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void PackageEvaluation(string baseRoot, string mod, string root, string actorId,
        string questId, short[] stages, string[] dependencies, bool stoppedFailure = false, string? expectedPackageId = null)
    {
        Node3D? presentation = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var actorIdentity = actorId.Split(':');
            var caller = actorIdentity.Length == 2 ? new FalloutFormKey(actorIdentity[0], Convert.ToUInt32(actorIdentity[1], 16))
                : FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(baseRoot, "FalloutNV.exe")));
            var quests = new FalloutQuestState(records);
            foreach (var stage in stages) quests.EnterStage(quest, stage);
            // Isolated command fixture supplies prior stage/conversation state;
            // the preceding campaign and its effects are not executed.
            world.Get(caller).Enabled = true;
            world.Get(caller).TalkedToPlayer = true;
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(caller, 1);
            // Same-day start history is explicit fixture input, not an executed
            // campaign prefix. Stopped initialization checks leave it empty;
            // ordinary source conditions still select the named expected result.
            var npc = FalloutDialogueTopic.RequiredForm(records.GetEffective(caller), "NAME");
            var packageOwner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(npc), 32, templates);
            var expectedPackages = expectedPackageId?.Split(',').Select(value =>
            {
                var parts = value.Split(':');
                return parts is { Length: 2 } ? new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16)) :
                    throw new InvalidDataException("Expected package needs a source owner and object identity.");
            }).ToArray();
            foreach (var field in packageOwner.ReadSubrecords().Where(field => !stoppedFailure && field.Signature == "PKID"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Fixture package identity has an invalid extent.");
                var packageKey = packageOwner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                world.MarkPackageStart(caller, records.GetEffective(packageKey), clock);
            }
            presentation = new Node3D(); AddChild(presentation);
            using var presentationLifetime = new PackageFixtureLifetime(presentation);
            var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(caller, 1), templates);
            Transform3D Placement(FalloutPlacedReference reference) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            actor.Transform = Placement(placed); presentation.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false);
            actor.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: world);
            if (actor.AiError is null || actor.CurrentPackage is not { } selected)
                throw new InvalidDataException("Evaluation fixture needs a selected source package with an unowned native procedure.");
            if (expectedPackages is not null && !expectedPackages.Contains(selected))
                throw new InvalidDataException("Source conditions selected a different package than the named fixture.");
            var package = records.GetEffective(selected); var hash = SHA256.HashData(package.ReadData());
            var before = actor.Transform;
            // EVP legitimately draws new random conditions. A named cold-binding
            // fixture keeps the already selected result; the separate command
            // fixture checks queued evaluation on its deterministic source case.
            var evaluationCommand = expectedPackages is null;
            if (evaluationCommand)
            {
                actor.EvaluatePackages(false);
                var pending = JsonSerializer.SerializeToElement(actor.AiState);
                if (!pending.GetProperty("evaluationPending").GetBoolean() || actor.CurrentPackage != selected ||
                    actor.Transform != before || world.Get(caller).ProcedureCaptureBlocker is null ||
                    pending.GetProperty("currentProcedure").ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("EVP did not retain source selection independently of native procedure execution.");
                try { _ = world.Get(caller).Capture(); throw new InvalidDataException("Pending package selection became saveable."); }
                catch (NotSupportedException) { }
                actor._Process(0);
                if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("evaluationPending").GetBoolean() ||
                    actor.AiError is null || actor.CurrentPackage != selected || actor.Transform != before ||
                    world.Get(caller).ProcedureCaptureBlocker is null ||
                    JsonSerializer.SerializeToElement(actor.AiState).GetProperty("currentProcedure").ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Native continuation lost its retained procedure fault or invented movement/completion.");
            }
            var stoppedCold = world.Get(caller).PackageBindingFailureCaptureReady;
            if (stoppedFailure && !stoppedCold)
                throw new InvalidDataException("Stopped initialization still published an active or pending procedure owner.");
            if (stoppedCold)
            {
                actor._Process(.25);
                var snapshots = world.Capture();
                var savedActor = snapshots.Single(value => value.Reference == caller);
                if (savedActor.PackageBindingFailure is null || savedActor.PackageAssignment is not null ||
                    world.PendingProcedureCaptureCount != 0 || world.StoppedPackageBindingCount != 1)
                    throw new InvalidDataException("Stopped initialization has no explicit, visible save continuation.");
                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!);
                cold.LoadCell(cell);
                var resumed = RuntimeNativeNpc.Create(records, content, placed, units,
                    (_, _, _, _) => new StandardMaterial3D(), cold.EquippedArmor(caller, 1),
                    cold.InitializeActorTemplates(caller, 1));
                try
                {
                    presentation.AddChild(resumed); resumed.SetProcess(false); resumed.SetPhysicsProcess(false);
                    resumed.Transform = Placement(placed);
                    resumed.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: cold);
                    var recaptured = cold.Get(caller).Capture();
                    var failureSame = JsonSerializer.Serialize(recaptured.PackageBindingFailure) ==
                        JsonSerializer.Serialize(savedActor.PackageBindingFailure);
                    if (resumed.AiError != actor.AiError || resumed.CurrentPackage != selected ||
                        resumed.Transform != actor.Transform || recaptured.Animation != savedActor.Animation ||
                        !failureSame || cold.PendingPackageEventCount != 0 || cold.PendingProcedureCaptureCount != 0 ||
                        cold.StoppedPackageBindingCount != 1)
                        throw new InvalidDataException("Cold initialization changed source, pose, clock, RNG, poll or consumed events. " +
                            $"poseSame={resumed.Transform == actor.Transform} clockSame={recaptured.Animation == savedActor.Animation} " +
                            $"failureSame={failureSame} before={JsonSerializer.Serialize(savedActor.PackageBindingFailure)} " +
                            $"after={JsonSerializer.Serialize(recaptured.PackageBindingFailure)}");
                    actor._Process(.125); resumed._Process(.125);
                    if (JsonSerializer.Serialize(cold.Get(caller).Capture().PackageBindingFailure) !=
                        JsonSerializer.Serialize(world.Get(caller).Capture().PackageBindingFailure) ||
                        cold.Get(caller).Animation.Capture() != world.Get(caller).Animation.Capture())
                        throw new InvalidDataException("Cold initialization diverged after resumed blink/base/poll advancement.");
                }
                finally { resumed.Free(); }
            }
            else
            {
                try { _ = world.Get(caller).Capture(); throw new InvalidDataException("Unowned active continuation became saveable."); }
                catch (NotSupportedException) { }
            }

            const string failure = "Synthetic retained package result failure.";
            world.Get(caller).ScriptError = failure;
            foreach (var reset in new[] { false, true })
            {
                try { actor.EvaluatePackages(reset); throw new InvalidDataException("EVP erased a retained result failure."); }
                catch (NotSupportedException error) when (error.Message == failure) { }
            }
            if (world.Get(caller).ScriptError != failure || actor.Transform != before ||
                !hash.AsSpan().SequenceEqual(SHA256.HashData(package.ReadData())))
                throw new InvalidDataException("Package evaluation changed source bytes, pose or its retained result failure.");
            GD.Print($"OPENNV_NATIVE_PACKAGE_EVALUATION_PASS actor={caller} package={selected} sourceSelection=true " +
                $"evaluationCommand={evaluationCommand} queuedPendingSaveRefused={evaluationCommand} nativeFaultVisible=true stoppedCold={stoppedCold} retainedResults=true " +
                "sourceReadonly=true fixture=isolated-owned-command campaignAndParity=unverified recording=false");
        }
        finally { if (GodotObject.IsInstanceValid(presentation)) presentation!.Free(); }
    }

    private sealed class PackageFixtureLifetime(Node node) : IDisposable
    {
        public void Dispose() => node.Free();
    }
}
