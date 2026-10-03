using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Gameplay.State;

internal static class DialoguePackageQueryContracts
{
    internal static void Run(string directory, FalloutPluginStack records, FalloutReferenceWorld world, FalloutDialogueSpeaker identity)
    {
        // The declaring plugin's first master is deliberately not the source
        // actor plugin. Raw CTDA package/reference IDs must use master order.
        File.WriteAllBytes(Path.Combine(directory, "OtherMaster.esm"), Header());
        File.WriteAllBytes(Path.Combine(directory, "PackageQuery.esp"), Join(Header("OtherMaster.esm", "Actors.esm"),
            Record("INFO", 0x02000800, Field("CTDA", Condition())),
            Record("PACK", 0x02000801, Field("PTDT", Join(BitConverter.GetBytes(0),
                BitConverter.GetBytes(0x01000901u), new byte[8])))));
        using var declarations = FalloutPluginStack.Load(directory, ["Actors.esm", "OtherMaster.esm", "PackageQuery.esp"]);
        var source = declarations.GetEffective(new("PackageQuery.esp", 0x800));
        var condition = FalloutCondition.Read(source).Single();
        FalloutFormKey Key(uint id) => new("Actors.esm", id);
        var speaker = Key(0x900); var listener = Key(0x901); var player = Key(0x14);
        var first = Key(0x8f3); var other = Key(0x8f4);
        FalloutFormKey? speakerPackage = first, listenerPackage = other, playerPackage = other, queried = null;
        var speakerState = world.Get(speaker); var listenerState = world.Get(listener);
        speakerState.QueryCurrentPackage = () => speakerPackage;
        listenerState.QueryCurrentPackage = () => listenerPackage;
        try
        {
            FalloutFormKey? Query(FalloutFormKey reference)
            {
                queried = reference;
                return reference == player ? playerPackage : world.CurrentPackage(reference);
            }
            float Fallback(FalloutCondition value) => throw new InvalidOperationException($"Package query used fallback {value.Function}.");
            var context = new FalloutDialogueConditions(records, new(records), speaker, identity, Fallback, currentPackage: Query);
            Check(condition.FormArgument1 == first && context.Evaluate(condition) == 1 && queried == speaker,
                "Self package or declaring-plugin master adjustment changed.");
            Check(context.Evaluate(condition with { RunOn = 1 }) == 0 && queried == player,
                "Dialogue target queried the speaker instead of the player.");
            playerPackage = first;
            Check(context.Evaluate(condition with { RunOn = 1 }) == 1, "Player package query retained a stale assignment.");
            var directed = new FalloutDialogueConditions(records, new(records), speaker, identity, Fallback,
                listener: listener, listenerIdentity: identity, currentPackage: Query);
            Check(directed.Evaluate(condition with { RunOn = 1 }) == 0 && queried == listener,
                "NPC listener used the player or speaker package.");
            Check(context.Evaluate(condition with { RunOn = 2, Reference = 0x01000901 }) == 0 && queried == listener,
                "Explicit package subject lost source master adjustment.");
            listenerPackage = first;
            Check(directed.Evaluate(condition with { RunOn = 1 }) == 1 &&
                context.Evaluate(condition with { RunOn = 2, Reference = 0x01000901 }) == 1,
                "Listener or explicit-reference query retained a stale package.");
            speakerPackage = null;
            Check(context.Evaluate(condition) == 0, "An active owner with no package acquired an invented assignment.");
            speakerState.QueryCurrentPackage = null;
            Reject(() => context.Evaluate(condition));
            Reject(() => context.Evaluate(condition with { RunOn = 2 }));
            Reject(() => context.Evaluate(condition with { RunOn = 3 }));
            Reject(() => world.CurrentPackage(Key(0x903)));
            var absent = new FalloutDialogueConditions(records, new(records), speaker, identity, Fallback);
            Reject(() => absent.Evaluate(condition));
            Console.WriteLine("OPENNV_DIALOGUE_PACKAGE_QUERY_CONTRACT_PASS self=true player=true npcListener=true explicitReference=true sourceMasters=true liveReplacement=true missingOwnerRefused=true");
            using var combat = new FalloutReferenceWorld(records);
            var combatCondition = condition with { Function = 289, Argument1 = 0, Owner = declarations.GetEffective(new("PackageQuery.esp", 0x801)) };
            bool Combat(FalloutCondition value) => combat.IsInCombat(FalloutAiPackages.ConditionSubject(value, speaker));
            Check(!Combat(combatCondition) && !Combat(combatCondition with { RunOn = 1 }), "Idle actors acquired an invented engagement.");
            combat.Get(listener).Engagement = new(player);
            Check(!Combat(combatCondition) && Combat(combatCondition with { RunOn = 1 }) &&
                Combat(combatCondition with { RunOn = 2, Reference = 0x01000901 }),
                "Combat query borrowed self state or lost source target/master identity.");
            using var combatCold = new FalloutReferenceWorld(records); combatCold.Restore(combat.Capture());
            Check(combatCold.IsInCombat(listener), "Cold combat query lost retained engagement state.");
            combat.Get(listener).Engagement = new(player, "idle");
            Check(!Combat(combatCondition with { RunOn = 1 }), "Idle engagement remained in combat.");
            combat.Get(listener).Engagement = new(player); combat.Get(listener).Enabled = false;
            Check(!Combat(combatCondition with { RunOn = 1 }), "Disabled actor remained in combat.");
            combat.Get(listener).Enabled = true;
            combat.Get(listener).Injury = new(true, null, new Dictionary<byte, float>());
            Check(!Combat(combatCondition with { RunOn = 1 }), "Dead actor remained in combat.");
            var playerCombat = false;
            Check(!combat.IsInCombat(player, () => playerCombat), "Idle player acquired combat state.");
            playerCombat = true;
            Check(combat.IsInCombat(player, () => playerCombat), "Player query retained stale combat state.");
            Reject(() => combat.IsInCombat(player));
            Reject(() => combat.IsInCombat(Key(0x903)));
            Reject(() => Combat(combatCondition with { RunOn = 2 }));
            Reject(() => Combat(combatCondition with { RunOn = 4 }));
            Console.WriteLine("OPENNV_COMBAT_CONDITION_SUBJECT_PASS self=true sourceTarget=true explicitReference=true sourceMasters=true liveCombat=true deadDisabledIdleRefused=true cold=true playerOwner=true");
        }
        finally { speakerState.QueryCurrentPackage = null; listenerState.QueryCurrentPackage = null; }
    }

    private static byte[] Condition()
    {
        var data = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 161);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0x010008f3);
        return data;
    }
    private static void Check(bool pass, string error) { if (!pass) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned package query was admitted.");
    }
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static byte[] Header(params string[] masters)
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, Field("HEDR", data), Join(masters.Select(master =>
            Join(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')), Field("DATA", new byte[8]))).ToArray()));
    }
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
}
