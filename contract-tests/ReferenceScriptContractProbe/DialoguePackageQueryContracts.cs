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
                BitConverter.GetBytes(0x01000901u), new byte[8]))),
            Record("SCPT", 0x02000802, Join(new uint[] { 0x01000900, 0x01000901, 0x010008f3, 0x010008f4, 0x01000014 }
                .Select(id => Field("SCRO", BitConverter.GetBytes(id))).ToArray()))));
        using var declarations = FalloutPluginStack.Load(directory, ["Actors.esm", "OtherMaster.esm", "PackageQuery.esp"]);
        var source = declarations.GetEffective(new("PackageQuery.esp", 0x800));
        var condition = FalloutCondition.Read(source).Single();
        FalloutFormKey Key(uint id) => new("Actors.esm", id);
        var speaker = Key(0x900); var listener = Key(0x901); var player = Key(0x14);
        var first = Key(0x8f3); var other = Key(0x8f4);
        FalloutFormKey? speakerPackage = first, listenerPackage = other, playerPackage = other, queried = null;
        var speakerState = world.Get(speaker); var listenerState = world.Get(listener);
        using var scriptWorld = new FalloutReferenceWorld(declarations);
        scriptWorld.LoadCell(FalloutCellSceneReader.Read(declarations, speakerState.Cell));
        speakerState.QueryCurrentPackage = () => speakerPackage;
        listenerState.QueryCurrentPackage = () => listenerPackage;
        try
        {
            FalloutFormKey? Query(FalloutFormKey reference)
            {
                queried = reference;
                return reference == player ? playerPackage : world.CurrentPackage(reference);
            }
            var script = declarations.GetEffective(new("PackageQuery.esp", 0x802));
            void ScriptQuery(string expression, bool expected)
            {
                var effects = 0;
                var scripts = new FalloutReferenceScripts(declarations, scriptWorld, new(declarations), new((_, _) => false,
                    effect =>
                    {
                        if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate || effect.Target != speaker)
                            throw new InvalidDataException("Package query changed the calling actor.");
                        effects++;
                    }, CurrentPackage: Query));
                scripts.ExecuteProgram(declarations.GetEffective(speaker), script,
                    FalloutGameModeProgram.Read($"begin GameMode\nif {expression}\nActivate\nendif\nend"), 0);
                Check(effects == (expected ? 1 : 0), "Script current-package query lost its subject or live assignment.");
            }
            ScriptQuery("GetIsCurrentPackage FirstActorPackage", true);
            ScriptQuery("BoundCreature.GetIsCurrentPackage OtherActorPackage", false);
            ScriptQuery("( GetSelf ).GetIsCurrentPackage FirstActorPackage", true);
            ScriptQuery("player.GetIsCurrentPackage OtherActorPackage", true);
            ScriptQuery("player.GetIsCurrentPackage FirstActorPackage || GetIsCurrentPackage FirstActorPackage", true);
            speakerPackage = other;
            ScriptQuery("GetIsCurrentPackage FirstActorPackage", false);
            ScriptQuery("GetIsCurrentPackage OtherActorPackage", true);
            speakerPackage = null;
            ScriptQuery("GetIsCurrentPackage FirstActorPackage", false);
            queried = null;
            Reject(() => ScriptQuery("GetIsCurrentPackage BoundCreature", false));
            Check(queried is null, "An invalid package argument queried an actor owner.");
            speakerPackage = first;
            Console.WriteLine("OPENNV_SCRIPT_PACKAGE_QUERY_CONTRACT_PASS caller=true explicit=true player=true postfix=true sourceMasters=true liveReplacement=true invalidArgumentRefused=true");
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
            Check(!combat.PlayerInCombat(), "An unloaded actor acquired the resident player combat owner.");
            combat.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
            Check(combat.PlayerInCombat(), "Player combat required a Godot body instead of the resident engagement.");
            Check(!Combat(combatCondition) && Combat(combatCondition with { RunOn = 1 }) &&
                Combat(combatCondition with { RunOn = 2, Reference = 0x01000901 }),
                "Combat query borrowed self state or lost source target/master identity.");
            using var combatCold = new FalloutReferenceWorld(records); combatCold.Restore(combat.Capture());
            combatCold.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
            Check(combatCold.PlayerInCombat(), "Cold player combat lost the resident authoritative engagement.");
            Check(combatCold.IsInCombat(listener), "Cold combat query lost retained engagement state.");
            foreach (var action in new[] { "pursue", "attack", "reload", "flee", "idle" })
            {
                combat.Get(listener).Engagement = new(player, action);
                Check(Combat(combatCondition with { RunOn = 1 }) && combat.PlayerInCombat() &&
                    combat.HasSelectedCombatTarget(listener, player) &&
                    !combat.HasSelectedCombatTarget(listener, speaker),
                    $"Combat action {action} changed membership or borrowed a different selected target.");
                using var actionCold = new FalloutReferenceWorld(records);
                actionCold.Restore(combat.Capture());
                actionCold.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
                Check(actionCold.IsInCombat(listener) && actionCold.PlayerInCombat() &&
                    actionCold.HasSelectedCombatTarget(listener, player),
                    $"Cold combat action {action} lost retained membership or its actual selected target.");
            }
            combat.Get(listener).Engagement = null;
            Check(!Combat(combatCondition with { RunOn = 1 }) && !combat.PlayerInCombat() &&
                !combat.HasSelectedCombatTarget(listener, player), "Retired engagement retained combat membership or a selected target.");
            combat.Get(listener).Engagement = new(player); combat.Get(listener).Enabled = false;
            Check(!Combat(combatCondition with { RunOn = 1 }) && !combat.PlayerInCombat() &&
                !combat.HasSelectedCombatTarget(listener, player), "Disabled actor retained combat membership or its selected target.");
            combat.Get(listener).Enabled = true;
            combat.Get(listener).Injury = new(true, null, new Dictionary<byte, float>());
            Check(!Combat(combatCondition with { RunOn = 1 }) && !combat.PlayerInCombat() &&
                !combat.HasSelectedCombatTarget(listener, player), "Dead actor retained combat membership or its selected target.");
            var playerCombat = false;
            Check(!combat.IsInCombat(player, () => playerCombat), "Idle player acquired combat state.");
            playerCombat = true;
            Check(combat.IsInCombat(player, () => playerCombat), "Player query retained stale combat state.");
            Reject(() => combat.IsInCombat(player));
            Reject(() => combat.IsInCombat(Key(0x903)));
            Reject(() => Combat(combatCondition with { RunOn = 2 }));
            Reject(() => Combat(combatCondition with { RunOn = 4 }));
            Console.WriteLine("OPENNV_COMBAT_CONDITION_SUBJECT_PASS self=true sourceTarget=true explicitReference=true sourceMasters=true liveCombat=true actionIndependent=true exactSelectedTarget=true retiredDeadDisabledRefused=true cold=true playerOwner=true");
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
