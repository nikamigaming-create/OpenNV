using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompanionPackageProbe
{
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
