using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

internal static class OwnedPlayerStatusValuesProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var beforeStream = File.OpenRead(content.FalloutExecutablePath);
        var before = Convert.ToHexString(SHA256.HashData(beforeStream));
        var source = FalloutExecutableStringTable.ReadDefaultStatusActorValues(content.FalloutExecutablePath);
        var values = new FalloutPlayerActorValues(records);
        Bind(values);
        if (!values.IsStatusValue("Paralysis") || values.StatusValue("Paralysis") != 47 || values.ReadCurrent(47) != 0 ||
            source.Values.Single(value => value.Index == 47).HasChangeCallback != true)
            throw new InvalidDataException("Owned status declaration/default getter differs from its actual descriptor.");
        var unchanged = JsonSerializer.Serialize(values.Capture());
        foreach (var operation in new[] { "SetAV", "ModAV", "ForceAV" })
        {
            try { values.Change("Paralysis", operation, 1); }
            catch (NotSupportedException error) when (error.Message.StartsWith("Player status command has an unbound independent source change callback:",
                StringComparison.Ordinal)) { continue; }
            throw new InvalidDataException("Owned status command skipped its independent source change callback.");
        }
        if (unchanged != JsonSerializer.Serialize(values.Capture())) throw new InvalidDataException("Unbound source callback changed a status pool.");
        values.AddModifier(47, FalloutActorValuePool.Temporary, .75f);
        values.AddModifier(47, FalloutActorValuePool.Permanent, .5f);
        values.AddModifier(47, FalloutActorValuePool.Damage, -.25f);
        var state = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(values.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, state); Bind(cold);
        if (values.ReadCurrent(47) != 1 || cold.ReadCurrent(47) != 1 ||
            JsonSerializer.Serialize(values.Capture().StatusValues) != JsonSerializer.Serialize(cold.Capture().StatusValues))
            throw new InvalidDataException("Owned cold status pools lost their separate mutable/effect values.");
        using var afterStream = File.OpenRead(content.FalloutExecutablePath);
        if (Convert.ToHexString(SHA256.HashData(afterStream)) != before) throw new InvalidDataException("Owned status input changed.");
        Console.WriteLine($"OPENNV_OWNED_PLAYER_STATUS_VALUES_PASS descriptors={source.Values.Count} slot=47 current=true modifierPoolFixtures=true cold=true commandCallbackRefused=true sourceReadOnly=true gameplay=UNEXECUTED");

        void Bind(FalloutPlayerActorValues owner)
        {
            var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(owner.Source.Player), "RNAM");
            var skills = new FalloutPlayerSkills(records, () => owner.BaseSpecial, _ => false, () => [], FalloutGlobalState.Read(records),
                new FalloutPlayerInventory(), owner.Source.Player, () => race, () => false, actorValues: owner);
            owner.BindConstantModifiers(skills.Modifiers);
        }
    }
}
