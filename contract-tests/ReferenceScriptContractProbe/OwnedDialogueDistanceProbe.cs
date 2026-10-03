using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDialogueDistanceProbe
{
    internal static void Run(string game, string ttwRoot, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", ttwRoot, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var info = records.GetEffective(new("Fallout3.esm", 0x01f9c9));
        var actor = records.GetEffective(new("Fallout3.esm", 0x0300ef));
        var marker = records.GetEffective(new("Fallout3.esm", 0x0304dc));
        if (info.Signature != "INFO" || actor.Signature != "ACHR" || marker.Signature != "REFR")
            throw new InvalidDataException("Selected range dialogue requires its winning INFO and placed references.");
        var hashes = new[] { info, actor, marker }.Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var conditions = FalloutCondition.Read(info).Where(condition => condition.Function == 1).ToArray();
        if (conditions.Length != 1) throw new InvalidDataException("Selected range INFO has no unique distance condition.");
        var condition = conditions[0];
        if (condition.Flags != 0x80 || condition.RunOn != 0 || condition.Comparison != 200 || condition.Argument2 != 0 ||
            condition.Reference != 0 || condition.FormArgument1 != marker.FormKey ||
            info.Plugin.AdjustFormId(condition.Argument1) != marker.FormKey || condition.Argument1 == marker.FormKey.ObjectId)
            throw new InvalidDataException("Selected range distance predicate or source master adjustment changed.");
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var quests = new FalloutQuestState(records);
        var questState = JsonSerializer.Serialize(quests.Capture());
        var sourceActor = world.Placement(actor.FormKey);
        var sourceMarker = world.Placement(marker.FormKey);
        sourceActor.Validate(); sourceMarker.Validate();
        if (sourceActor.Cell != sourceMarker.Cell)
            throw new InvalidDataException("Selected range source references no longer share an interior cell.");
        var queries = 0;
        var sourceDistance = Check(world, Expected(sourceActor, sourceMarker));

        // These placements are isolated query fixtures. No INFO result script,
        // campaign stage, live runtime or user's checkpoint is executed/changed.
        world.SetPlacement(actor.FormKey, Offset(3, 4, 12));
        if (Check(world, 13) != 13 || !Passes(world))
            throw new InvalidDataException("Selected source distance did not admit the near placement fixture.");
        world.SetPlacement(actor.FormKey, Offset(condition.Comparison, 0, 0));
        Check(world, condition.Comparison);
        if (Passes(world)) throw new InvalidDataException("Selected strict distance predicate admitted its exact boundary.");
        world.SetPlacement(actor.FormKey, Offset(condition.Comparison + 25, 0, 0));
        Check(world, condition.Comparison + 25);
        if (Passes(world)) throw new InvalidDataException("Selected source distance retained a stale near placement.");

        world.SetPlacement(actor.FormKey, Offset(3, 4, 12));
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))
            ?? throw new InvalidDataException("Distance placement fixture did not serialize.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        Check(cold, 13);
        if (!Passes(cold) || !cold.Placement(actor.FormKey).Position.SequenceEqual(world.Placement(actor.FormKey).Position) ||
            !cold.EditorPlacement(actor.FormKey).Position.SequenceEqual(sourceActor.Position) ||
            questState != JsonSerializer.Serialize(quests.Capture()) ||
            hashes.Any(pair => !pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))))
            throw new InvalidDataException("Cold distance placement, campaign state or winning source bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_DISTANCE_PASS info={info.FormKey} actor={actor.FormKey} marker={marker.FormKey} " +
            $"queries={queries} sourceDistanceGameUnits={sourceDistance:R} masterAdjusted=true finite=true boundary=true cold=true " +
            "sourceReadonly=true queryReadonly=true referenceState=isolated-placement-fixture ordinaryInput=separate campaignProgress=unverified parity=unverified");

        FalloutReferencePlacement Offset(float x, float y, float z) => new(sourceMarker.Cell,
            [sourceMarker.Position[0] + x, sourceMarker.Position[1] + y, sourceMarker.Position[2] + z],
            (float[])sourceActor.RotationRadians.Clone());

        FalloutDialogueConditions Context(FalloutReferenceWorld owner) => new(records, quests, actor.FormKey, identity,
            runtime: _ => throw new InvalidOperationException("Source distance escaped its spatial query owner."),
            referenceDistance: (subject, target) =>
            {
                if (subject != actor.FormKey || target != marker.FormKey)
                    throw new InvalidDataException("Source distance selected a different subject or adjusted target.");
                queries++;
                // This fixture contains source-unit placements and no retained
                // native motion, so the native metres conversion is unused.
                return owner.Distance(subject, target, null, 1);
            });

        float Check(FalloutReferenceWorld owner, float expected)
        {
            var before = JsonSerializer.Serialize(owner.Capture());
            var actual = Context(owner).Evaluate(condition);
            if (!float.IsFinite(actual) || actual < 0 || Math.Abs(actual - expected) > 0.001f ||
                before != JsonSerializer.Serialize(owner.Capture()))
                throw new InvalidDataException("Source distance differs from 3D game-unit placement or mutates query state.");
            return actual;
        }

        bool Passes(FalloutReferenceWorld owner)
        {
            var before = JsonSerializer.Serialize(owner.Capture());
            var result = FalloutCondition.AllPass([condition], Context(owner).Evaluate);
            if (before != JsonSerializer.Serialize(owner.Capture()))
                throw new InvalidDataException("Source distance predicate mutates retained reference state.");
            return result;
        }

        static float Expected(FalloutReferencePlacement from, FalloutReferencePlacement to)
        {
            var dx = (double)from.Position[0] - to.Position[0];
            var dy = (double)from.Position[1] - to.Position[1];
            var dz = (double)from.Position[2] - to.Position[2];
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
