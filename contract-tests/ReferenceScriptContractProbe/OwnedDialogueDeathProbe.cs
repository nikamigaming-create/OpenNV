using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDialogueDeathProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var info = records.GetEffective(new("FalloutNV.esm", 0x10b3da));
        var condition = FalloutCondition.Read(info).Single(value => value.Function == 46);
        var subject = condition.Owner.Plugin.AdjustFormId(condition.Reference);
        if (condition.RunOn != 2 || condition.Flags != 0 || condition.Comparison != 0 ||
            subject != new FalloutFormKey("FalloutNV.esm", 0x10b3de) || records.GetEffective(subject).Signature != "ACHR")
            throw new InvalidDataException("Owned dialogue death predicate or its placed subject changed.");
        var sourceHash = SHA256.HashData(info.ReadData());
        var actor = records.GetEffective(new("FalloutNV.esm", 0x104e85));
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        FalloutDialogueConditions Conditions(FalloutReferenceWorld owner) => new(records, new(records), actor.FormKey, identity,
            runtime: _ => throw new InvalidOperationException("GetDead escaped the shared death owner."),
            isDead: reference =>
            {
                owner.InitializeSourceCorpse(reference);
                return owner.IsDead(reference);
            });
        var conditions = Conditions(world);
        if (conditions.Evaluate(condition) != 0 || !FalloutCondition.AllPass([condition], conditions.Evaluate, evaluateRunOn: true))
            throw new InvalidDataException("Source-alive settler failed the owned GetDead == 0 predicate.");

        // This isolated reference world exercises the existing death transition.
        // It does not run campaign scripts, edit owned data or touch a live save.
        if (!world.KillActor(subject, records.RuntimeFormKey(0x14), 1) || conditions.Evaluate(condition) != 1 ||
            FalloutCondition.AllPass([condition], conditions.Evaluate, evaluateRunOn: true))
            throw new InvalidDataException("Owned dialogue did not follow the actor's actual death transition.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        if (Conditions(cold).Evaluate(condition) != 1 || !sourceHash.SequenceEqual(SHA256.HashData(info.ReadData())))
            throw new InvalidDataException("Dialogue death lost persisted injury or changed winning source bytes.");
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_DEATH_PASS info={info.FormKey} source={info.Plugin.Name} subject={subject} " +
            "sourceAlive=true actualDeath=true predicateFlips=true cold=true sourceReadonly=true campaign=unverified parity=unverified");
    }
}
