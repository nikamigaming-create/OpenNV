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
        string questId, short[] stages, string[] dependencies)
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
            var caller = FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
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
            // This fixture represents a later selection in the same day. Supply
            // actual source once-per-day start history without running their
            // prior conversations, results or presentation.
            var npc = FalloutDialogueTopic.RequiredForm(records.GetEffective(caller), "NAME");
            var packageOwner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(npc), 32, templates);
            foreach (var field in packageOwner.ReadSubrecords().Where(field => field.Signature == "PKID"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Fixture package identity has an invalid extent.");
                var packageKey = packageOwner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                world.MarkPackageStart(caller, records.GetEffective(packageKey), clock);
            }
            presentation = new Node3D(); AddChild(presentation);
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
            var package = records.GetEffective(selected); var hash = SHA256.HashData(package.ReadData());
            var before = actor.Transform;
            actor.EvaluatePackages(false);
            var pending = JsonSerializer.SerializeToElement(actor.AiState);
            if (!pending.GetProperty("evaluationPending").GetBoolean() || actor.CurrentPackage != selected ||
                actor.Transform != before || world.Get(caller).ProcedureCaptureBlocker is null ||
                pending.GetProperty("currentProcedure").ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("EVP did not retain source selection independently of native procedure execution.");
            actor._Process(0);
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("evaluationPending").GetBoolean() ||
                actor.AiError is null || actor.CurrentPackage != selected || actor.Transform != before ||
                world.Get(caller).ProcedureCaptureBlocker is null ||
                JsonSerializer.SerializeToElement(actor.AiState).GetProperty("currentProcedure").ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("Native continuation lost its retained procedure fault or invented movement/completion.");
            try { _ = world.Get(caller).Capture(); throw new InvalidDataException("Unowned procedure became saveable."); }
            catch (NotSupportedException) { }

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
                "voidCommand=true queuedContinuation=true nativeFaultVisible=true saveRefused=true retainedResults=true " +
                "sourceReadonly=true fixture=isolated-owned-command campaignAndParity=unverified recording=false");
        }
        finally { presentation?.Free(); }
    }
}
