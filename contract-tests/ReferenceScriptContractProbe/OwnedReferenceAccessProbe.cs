using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

// Full selected winning result scripts, with native presentation effects collected
// explicitly. This proves source access ownership, not preceding campaign play.
internal static class OwnedReferenceAccessProbe
{
    internal static void Run(string mod, string root, string game, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var fields = quest.ReadSubrecords().ToArray();
        var sourceHash = Convert.ToHexString(SHA256.HashData(quest.ReadData()));
        var room = FalloutDialogueTopic.Find(records, "REFR", "CG01PlayroomDoor").FormKey;
        var main = FalloutDialogueTopic.Find(records, "REFR", "CG01MainDoor").FormKey;
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, world.Get(room).Cell); world.LoadCell(cell);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind is not (FalloutReferenceEffectKind.DoorOpenState or FalloutReferenceEffectKind.SetStage))
                throw new NotSupportedException("Selected access results reached an unrelated native effect.");
            effects.Add(effect);
        }));
        var results = new FalloutQuestStages(records, quests, executor.StageSteps,
            condition => FalloutPlatformConditions.Evaluate(condition) ?? quests.Evaluate(condition));
        var before = world.GetLockLevel(room);
        results.Enter(quest.FormKey, 18);
        if (world.GetLocked(room) != 1 || world.GetLockLevel(room) != 100 ||
            !effects.Any(effect => effect.Kind == FalloutReferenceEffectKind.SetStage && effect.Target == quest.FormKey && effect.Stage == 20))
            throw new InvalidDataException("Full winning stage18 did not lock the actual room door before requesting stage20.");
        var count = effects.Count;
        results.Enter(quest.FormKey, 18);
        if (effects.Count != count) throw new InvalidDataException("Selected source result replayed after its stage was entered.");
        results.Enter(quest.FormKey, 72);
        if (world.GetLocked(room) != 0 || world.GetLockLevel(room) != 100 || world.Ownership(room).Owner != records.RuntimeFormKey(7) ||
            world.GetLocked(main) != 1 || world.GetLockLevel(main) != 100 ||
            !effects.Any(effect => effect.Kind == FalloutReferenceEffectKind.DoorOpenState && effect.Target == room && effect.Enable))
            throw new InvalidDataException("Full winning stage72 lost room Unlock/default ownership or MainDoor locking.");
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
        if (cold.GetLocked(room) != 0 || cold.GetLockLevel(room) != 100 || cold.Ownership(room) != world.Ownership(room) ||
            cold.GetLocked(main) != 1 || cold.GetLockLevel(main) != 100 ||
            !sourceHash.Equals(Convert.ToHexString(SHA256.HashData(quest.ReadData())), StringComparison.Ordinal))
            throw new InvalidDataException("Cold selected access state or owned source changed.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-reference-access/v1",
            quest = quest.FormKey.ToString(),
            sourceHash,
            room = room.ToString(),
            main = main.ToString(),
            sourceDifficulty = before,
            resultStages = new[] { 18, 72 },
            fullWinningResults = true,
            locked = true,
            unlocked = true,
            defaultOwner = cold.Ownership(room).Owner?.ToString(),
            difficultyRetained = true,
            coldAccess = true,
            sourceUnchanged = true,
            once = true,
            nativeEffects = effects.Select(effect => new
            { kind = effect.Kind.ToString(), target = effect.Target?.ToString(), effect.Enable, effect.Stage }),
            boundary = "isolated-source-result-fixture; native-effects-collected; preceding-and-nested-stage-play-unverified",
            recording = false,
            parity = "unverified"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("OPENNV_OWNED_REFERENCE_ACCESS_PASS stages=18,72 fullWinningResults=true coldAccess=true sourceUnchanged=true gameplay=unverified recording=false");
    }
}
