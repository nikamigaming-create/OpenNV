using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class CombatAlarmContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        static FalloutFormKey Key(uint id) => new("Actors.esm", id);
        var first = world.Get(Key(0x900)); var second = world.Get(Key(0x901));
        var unrelated = world.Get(Key(0x902)); var target = world.Get(Key(0x906));
        var player = records.RuntimeFormKey(0x14);
        first.Engagement = new(player); second.Engagement = new(player);
        unrelated.Engagement = new(target.Reference); target.Engagement = new(first.Reference);
        var stopped = new List<FalloutFormKey>();
        first.StopCombat = () => stopped.Add(first.Reference);
        second.StopCombat = () => stopped.Add(second.Reference);
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Combat-alarm command invented a presentation effect.")));
        void Execute(FalloutFormKey caller, uint script, string command) => scripts.ExecuteProgram(
            records.GetEffective(caller), records.GetEffective(Key(script)),
            FalloutGameModeProgram.Read($"begin GameMode\n{command}\nend"), 0);
        Execute(first.Reference, 0x891, "player.scaonactor");
        if (first.Engagement is not null || second.Engagement is not null || stopped.Count != 2 ||
            unrelated.Engagement?.Target != target.Reference || target.Engagement?.Target != first.Reference)
            throw new InvalidDataException("Combat alarm crossed target ownership or did not retire each current owner.");
        Execute(first.Reference, 0x891, "player.StopCombatAlarmOnActor");
        if (stopped.Count != 2) throw new InvalidDataException("A settled combat alarm repeated its retirement callback.");
        Execute(target.Reference, 0x891, "scaonactor");
        if (unrelated.Engagement is not null || target.Engagement is null)
            throw new InvalidDataException("Implicit combat alarm cleared the target's own unrelated fight.");
        second.Engagement = new(first.Reference);
        Execute(target.Reference, 0x892, "BoundCreature.StopCombatAlarmOnActor");
        if (second.Engagement is not null || target.Engagement is not null)
            throw new InvalidDataException("Explicit combat alarm lost its compiled actor reference.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        if (cold.Get(first.Reference).Engagement is not null || cold.Get(second.Reference).Engagement is not null ||
            cold.Get(target.Reference).Engagement is not null)
            throw new InvalidDataException("Cold state restored a retired fighting flag.");
        second.Engagement = new(player);
        foreach (var invalid in new[] { Key(0x903), Key(0x840), Key(0x9030) })
        {
            try { world.StopCombatAlarmOnActor(invalid); throw new InvalidOperationException("Invalid combat-alarm subject was accepted."); }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException) { }
        }
        if (second.Engagement is null) throw new InvalidDataException("An invalid combat alarm changed a fighting flag.");
        first.Engagement = new(player);
        first.StopCombat = () =>
        {
            if (first.Engagement is not null) throw new InvalidDataException("Combat-end observer saw an active retired fighting flag.");
            first.Engagement = new(target.Reference); second.Engagement = new(target.Reference);
        };
        world.StopCombatAlarmOnActor(player);
        if (first.Engagement?.Target != target.Reference || second.Engagement?.Target != target.Reference)
            throw new InvalidDataException("Combat alarm erased a new engagement created by its completion event.");
        Console.WriteLine("OPENNV_COMBAT_ALARM_CONTRACT_PASS player=true implicit=true compiledActor=true aliases=true distinctTargets=true onceOnlyRetirement=true cold=true invalidActorAtomic=true completionRetarget=true");
    }
}
