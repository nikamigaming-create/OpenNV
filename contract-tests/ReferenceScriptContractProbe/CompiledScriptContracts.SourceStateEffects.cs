using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunSourceStateEffects()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-state-effects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var select = Call(0x11fc, Join(U16(1), Form(1)));
            var tag = Call(0x11cd, Join(U16(2), U16(40), Integer(3)));
            SourceStateEffectFixture(directory, Join(select, tag, Set('f', 1, " 7")), records =>
            {
                var tags = new FalloutPlayerTagSkills(records, FalloutNativeTagSkillResolver.ResolveSkills(records));
                var quests = new FalloutQuestState(records); using var world = new FalloutReferenceWorld(records);
                var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                var scripts = SourceStateEffectExecutor(records, world, quests, tags);
                foreach (var _ in scripts.CompiledSteps(Key(0x20), program, program.Events[0])) { }
                var selected = world.NoActivationSound.Capture();
                Require(tags.Capture().Slots[3]?.EditorId == "RenamedScience" && tags.IsTagged("Science") &&
                    tags.Capture().Slots.Take(3).All(value => value is null) && quests.Variable(Key(0x20), 1) == 7 &&
                    selected is { Sound: var soundKey } && soundKey == Key(0x300) && selected.Sha256 == Convert.ToHexString(
                        SHA256.HashData(records.GetEffective(Key(0x300)).ReadData())) && world.Sounds.LastRequest is null,
                    "Compiled state effects did not use the winning tag/SOUN owners or unexpectedly played audio.");
                using var coldWorld = new FalloutReferenceWorld(records);
                var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
                var coldTags = new FalloutPlayerTagSkills(records, FalloutNativeTagSkillResolver.ResolveSkills(records),
                    snapshot: Copy(tags.Capture()));
                coldWorld.NoActivationSound.Restore(Copy(selected!));
                Require(coldTags.IsTagged("Science") && coldWorld.NoActivationSound.Capture() == selected &&
                    coldWorld.Sounds.LastRequest is null, "Cold state lost the real source owners or replayed feedback.");
                foreach (var _ in SourceStateEffectExecutor(records, coldWorld, coldQuests, coldTags)
                    .CompiledSteps(Key(0x20), program, program.Events[1])) { }
                Require(coldWorld.NoActivationSound.Capture() is null && world.NoActivationSound.Capture() == selected &&
                    coldTags.IsTagged("Science") && coldWorld.Sounds.LastRequest is null,
                    "Compiled Clear changed independent warm/tag state or requested the absent default audio producer.");
                Reject(() => coldWorld.NoActivationSound.Restore(selected! with { Sha256 = new string('0', 64) }));
            });
            SourceStateEffectFixture(directory, Join(select, tag, Set('f', 1, " 99")), records =>
            {
                using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
                var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                var cursor = new FalloutCompiledExecutionCursor(FalloutCompiledControlFlow.Read(
                    program.EventInstructions(program.Events[0])).InitialCursor);
                Reject(() => { foreach (var _ in SourceStateEffectExecutor(records, world, quests, null)
                    .CompiledSteps(Key(0x20), program, program.Events[0], cursor: cursor)) { } });
                Require(world.NoActivationSound.Capture()?.Sound == Key(0x300) && quests.Variable(Key(0x20), 1) == 0 &&
                    cursor.State.CommittedInstructions == 1 && !cursor.State.Completed,
                    "Missing tag owner erased a committed sound-selection prefix or committed its suffix.");
            });
            foreach (var invalid in new byte[][]
            {
                Call(0x11fc, Join(U16(1), Form(2))),
                Call(0x11fd, Join(U16(1), Integer(1))),
                Call(0x11cd, Join(U16(2), U16(40), Integer(-1))),
                Call(0x11cd, Join(U16(2), U16(40), Integer(4)))
            })
                SourceStateEffectFixture(directory, Join(invalid, Set('f', 1, " 99")), records =>
                {
                    using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
                    var tags = new FalloutPlayerTagSkills(records, FalloutNativeTagSkillResolver.ResolveSkills(records));
                    var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                    Reject(() => { foreach (var _ in SourceStateEffectExecutor(records, world, quests, tags)
                        .CompiledSteps(Key(0x20), program, program.Events[0])) { } });
                    Require(tags.Selection.Count == 0 && world.NoActivationSound.Capture() is null &&
                        quests.Variable(Key(0x20), 1) == 0 && world.Sounds.LastRequest is null,
                        "Malformed state effect mutated its owner, played feedback or committed its suffix.");
                });
            Require(!FalloutCompiledSemanticOwners.IsEffect("SetScreenSplatterFade") &&
                !FalloutCompiledSemanticOwners.IsEffect("TriggerScreenSplatter"),
                "State-effect admission widened unrelated splatter consumers.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_SOURCE_STATE_EFFECTS_PASS winningSound=true winningTagSlots=true cold=true prefix=true noPlayback=true");
    }

    private static FalloutReferenceScripts SourceStateEffectExecutor(FalloutPluginStack records,
        FalloutReferenceWorld world, FalloutQuestState quests, FalloutPlayerTagSkills? tags) => new(records, world, quests,
            new((_, _) => throw new InvalidDataException("State effect requested unrelated furniture state."),
                _ => throw new InvalidDataException("State effect fabricated a presentation consumer."), TagSkills: tags));

    private static void SourceStateEffectFixture(string directory, byte[] body, Action<FalloutPluginStack> run)
    {
        var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
        string[] names = ["Barter", "EnergyWeapons", "Explosives", "Lockpick", "Medicine", "MeleeWeapons",
            "Repair", "Science", "SmallGuns", "Sneak", "Speech", "Throwing", "Unarmed"];
        var original = Path.Combine(selected, "Bytecode.esm"); var patch = Path.Combine(selected, "Patch.esp");
        var code = StructuralStandalone(Block(0, body), Block(0, Call(0x11fd, [])));
        var sound = new byte[36]; BinaryPrimitives.WriteInt16LittleEndian(sound.AsSpan(22), 100);
        File.WriteAllBytes(original, Join(RecurrenceFixture("Diagnostic text cannot execute state effects.", code),
            Join(names.Select((name, index) => Record("AVIF", (uint)(0x100 + index),
                Field("EDID", Text("AV" + name)), Field("FULL", Text("Authored " + name)))).ToArray()),
            Record("SOUN", 0x300, Field("EDID", Text("OriginalSound")), Field("FNAM", Text("original.wav")), Field("SNDD", sound))));
        File.WriteAllBytes(patch, Join(Tes4("Bytecode.esm"),
            Record("AVIF", 0x107, Field("EDID", Text("RenamedScience")), Field("FULL", Text("Winning science"))),
            Record("SOUN", 0x300, Field("EDID", Text("WinningSound")), Field("FNAM", Text("winning.wav")), Field("SNDD", sound)),
            Record("SCPT", 0x30, Field("SCHR", Header(code, 2, 4, 1)), Field("SCDA", code),
                Local(1, "value"), Local(2, "suffix"), Local(3, "stage"), Local(4, "integral", true),
                Field("SCRO", U32(0x300)), Field("SCRO", U32(0x107)))));
        var hashes = new[] { original, patch }.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        using (var records = FalloutPluginStack.Load(selected, ["Bytecode.esm", "Patch.esp"])) run(records);
        Require(hashes[0].SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))) &&
            hashes[1].SequenceEqual(SHA256.HashData(File.ReadAllBytes(patch))), "Compiled state effects changed fixture inputs.");
    }
}
