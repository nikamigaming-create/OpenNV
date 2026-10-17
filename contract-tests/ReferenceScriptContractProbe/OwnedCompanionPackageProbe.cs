using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompanionPackageProbe
{
    internal static void RunAccompany(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(root), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(records.RuntimeFormKey(0x152afa));
        var bytes = record.ReadData();
        var accompany = FalloutFollowPackage.ReadAccompany(record);
        using var world = new FalloutReferenceWorld(records);
        var actor = world.Get(records.RuntimeFormKey(0x10588e));
        var leader = world.Get(accompany.Target);
        var selected = FalloutAiPackages.Select(records, actor.Base, condition => condition.Function switch
        {
            79 => 0,
            84 => world.GetDeadCount(condition.FormArgument1),
            _ => throw new NotSupportedException($"Unowned Accompany fixture condition {condition.Function}.")
        });
        if (selected?.FormKey != accompany.Form || accompany.Target != records.RuntimeFormKey(0x104e85) ||
            records.GetEffective(leader.Base).Signature != "NPC_" || accompany.Distance != 128 ||
            accompany.Flags != 0x04501000 || !accompany.DefensiveCombat || accompany.Running ||
            accompany.AllowsUnprovokedCombat(null) || !accompany.AllowsUnprovokedCombat(leader.Reference) ||
            accompany.AllowsUnprovokedCombat(records.RuntimeFormKey(0x14)) || !record.ReadData().SequenceEqual(bytes))
            throw new InvalidDataException("Owned Accompany source or defensive target policy differs.");
        var door = records.RuntimeFormKey(0x10618e);
        var arrival = world.TravelPortalArrival(door, out var destination);
        if (!world.CanNpcOpenDoor(door, actor.Reference) || destination != records.RuntimeFormKey(0x10636f) ||
            world.TravelPortal(actor.Reference, arrival.Cell) != door || world.Placement(actor.Reference).Cell != actor.Cell)
            throw new InvalidDataException("Owned Accompany door capability or source portal route differs.");
        Console.WriteLine("OPENNV_OWNED_ACCOMPANY_PACKAGE_PASS sourceTarget=true sourceDistance=true sourceSelection=true defensiveTarget=true sourceDoorCapability=true directedPortal=true sourceReadonly=true nativeMovement=separate");
    }

    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var normal = FalloutFollowPackage.Read(records.GetEffective(records.RuntimeFormKey(0x1572eb)));
        var distant = FalloutFollowPackage.Read(records.GetEffective(records.RuntimeFormKey(0x1572ea)));
        var dialogue = FalloutScriptPackage.Read(records.GetEffective(records.RuntimeFormKey(0x1579db)));
        if (normal.Distance != 330 || distant.Distance != 500 || normal.Target != records.RuntimeFormKey(0x14) ||
            distant.Target != normal.Target || dialogue.Procedure != 15 || dialogue.LocationType is not null)
            throw new InvalidDataException("Owned follow/dialogue package differs from the selected source contract.");
        var health = FalloutActorHealthSource.Read(records, records.RuntimeFormKey(0x10c769), new(1, 1));
        var later = FalloutActorHealthSource.Read(records, records.RuntimeFormKey(0x10c769), new(40, 1));
        if (health.Health != 180 || later.Health != 540) throw new InvalidDataException("Owned scaled creature health differs.");
        using var world = new FalloutReferenceWorld(records);
        var placed = records.RuntimeFormKey(0x1732d1);
        using var holdingWorld = new FalloutReferenceWorld(records);
        holdingWorld.InitializeActorTemplates(placed, 1);
        if (holdingWorld.CaptureEncounterZones().Count != 1)
            throw new InvalidDataException("Authored holding zone was not retained.");
        world.MoveTo(placed, records.RuntimeFormKey(0x1572e6));
        world.InitializeActorTemplates(placed, 1);
        if (world.Health(placed).Base != 180 || world.Placement(placed).Cell == world.Get(placed).Cell)
            throw new InvalidDataException("Moved actor used its source holding cell for encounter admission.");
        var talked = false; var distantRequested = false;
        float Condition(FalloutCondition condition) => condition.Function switch
        {
            50 => talked ? 1 : 0,
            244 or 35 => 0,
            53 => condition.Argument2 == 5 && distantRequested ? 1 : 0,
            79 => condition.Argument2 == 30 ? 1 : 0,
            _ => throw new NotSupportedException($"Unexpected reached package condition {condition.Function}.")
        };
        var actor = records.RuntimeFormKey(0x10c769);
        if (FalloutAiPackages.Select(records, actor, Condition)?.FormKey != dialogue.Form)
            throw new InvalidDataException("First dialogue lost source priority.");
        talked = true;
        if (FalloutAiPackages.Select(records, actor, Condition)?.FormKey != normal.Form)
            throw new InvalidDataException("Ordinary follow did not become eligible.");
        distantRequested = true;
        if (FalloutAiPackages.Select(records, actor, Condition)?.FormKey != distant.Form)
            throw new InvalidDataException("Requested follow distance lost source priority.");
        Console.WriteLine("OPENNV_OWNED_COMPANION_PACKAGES_PASS followDistances=true dialogueWithoutStart=true scaledHealth=true ordinaryRecruitment=separate");
    }
}
