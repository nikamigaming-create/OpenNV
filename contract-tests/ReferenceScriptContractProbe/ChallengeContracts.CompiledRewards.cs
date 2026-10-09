using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ChallengeContracts
{
    private static void CompiledRewards(string directory)
    {
        foreach (var diagnostic in new[] { "absent", "malformed", "contradictory", "duplicate" })
        {
            var diagnosticFields = diagnostic == "absent" ? Array.Empty<byte[]>() :
                diagnostic == "duplicate" ? new[] { Field("SCTX", Text("an unowned source body")), Field("SCTX", Text("another body")) } :
                new[] { Field("SCTX", Text(diagnostic == "contradictory" ? "begin GameMode\nset count to 999\nend" : "Endif malformed")) };
            var body = Join(Set(7), ModStatistic(2, 5), Set(9));
            Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500, Program(0, body), diagnostics: diagnosticFields));
            var input = SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Plugin)));
            using var records = Load(directory); using var warm = new Fixture(records);
            warm.Statistics.Mod(1, 1, "authored-real-reward");
            var saved = Copy(warm.Challenges.Capture()); var counters = Copy(warm.Statistics.Capture());
            var dispatch = saved.LastDispatch!;
            var reward = dispatch.Attempts.Single().Reward!; var row = reward.Events.Single();
            var receipt = row.Receipt ?? throw new InvalidDataException("Authored completed reward has no real retired lease.");
            Require(reward.Disposition == "completed" && row is { Attempted: true, Filtered: false, Cursor.Completed: true,
                Receipt: { Disposition: "completed", Invocation: > 0 } } && receipt.Session != Guid.Empty &&
                row.Cursor.CommittedInstructions == 3 && BitConverter.UInt64BitsToDouble(reward.Locals.Single().Payload) == 9 &&
                warm.Statistics.Read(2) == 5 && counters.Operations == 3 && counters.LastOperation!.Children!.Count == 2 &&
                warm.World.InstanceCount == 0 && warm.World.ScriptManualSaves.EnteredInvocations == 0,
                "Challenge reward ignored SCDA, reused attached actor locals, lost genuine nested commands, or fabricated retirement.");
            // A real retirement has already been consumed once by the source
            // owner. Replaying it or minting another typed receipt must fail.
            Reject(() => warm.World.ScriptManualSaves.RequireCurrentCompiledReceipt(receipt));
            Reject(() => warm.World.ScriptManualSaves.RequireCurrentCompiledReceipt(receipt with { Invocation = receipt.Invocation + 1 }));
            using var cold = new Fixture(records, statistics: counters, challenges: saved);
            Require(cold.MenuCalls == 0 && JsonSerializer.Serialize(cold.Challenges.Capture()) == JsonSerializer.Serialize(saved) &&
                cold.World.InstanceCount == 0, "Cold challenge reward reentered a script, reconstructed a fake player, or dropped its raw cells.");
            var drift = saved with { LastDispatch = dispatch with
            {
                Attempts = [dispatch.Attempts.Single() with { Reward = reward with { ProgramSha256 = new('0', 64) } }]
            } };
            using var refused = new Fixture(records, statistics: counters); Reject(() => refused.Challenges.Restore(drift));
            Require(input.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Plugin)))),
                "Challenge execution/cold restore mutated the authored winning input.");
        }

        Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500,
            Program(0, Join(Set(7), Instruction(0x2f03)))));
        using (var records = Load(directory))
        using (var warm = new Fixture(records))
        {
            Reject(() => warm.Statistics.Mod(1, 1, "authored-unknown-reward-opcode"));
            var saved = Copy(warm.Challenges.Capture()); var counters = Copy(warm.Statistics.Capture());
            var attempt = saved.LastDispatch!.Attempts.Single(); var reward = attempt.Reward!; var row = reward.Events.Single();
            Require(attempt.Prefix == FalloutChallengePrefix.RewardEntered && reward.Disposition == "closed-failure" &&
                row is { Cursor.CommittedInstructions: 1, Cursor.Completed: false, Receipt: { Disposition: "closed-failure", Invocation: > 0 } } &&
                BitConverter.UInt64BitsToDouble(reward.Locals.Single().Payload) == 7 && warm.Statistics.Read(27) == 0 &&
                counters.Operations == 1 && warm.Challenges.State(Key(0x100)) is { Progress: 1, Completed: false, Error: not null },
                "Unknown reward instruction lost a genuine local prefix, invented a completion award, or skipped the source refusal.");
            var before = JsonSerializer.Serialize(saved); Reject(() => warm.Statistics.Mod(1, 1, "authored-warm-no-replay"));
            using var cold = new Fixture(records, statistics: counters, challenges: saved);
            Reject(() => cold.Statistics.Mod(1, 1, "authored-cold-no-replay"));
            Require(cold.MenuCalls == 0 && before == JsonSerializer.Serialize(cold.Challenges.Capture()),
                "Failed cold reward replayed its committed instruction or removed the actual source error.");
            Reject(() => (reward with { Disposition = "completed", FailureType = null, Error = null }).Validate());
        }

        // Native/menu callback faults inside a real compiled command still
        // retain its committed counter, before the VM can commit that command.
        Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500,
            Program(0, Join(Set(7), ModStatistic(2, 5), Set(9)))));
        using (var records = Load(directory))
        using (var warm = new Fixture(records))
        {
            warm.DuringMenu = _ => throw new IOException("Authored callback failed after a genuine nested counter commit.");
            Reject(() => warm.Statistics.Mod(1, 1, "authored-callback-fault"));
            var saved = Copy(warm.Challenges.Capture()); var counters = Copy(warm.Statistics.Capture());
            Require(counters.Operations == 2 && warm.Statistics.Read(2) == 5 && warm.Statistics.Read(27) == 0 &&
                counters.LastOperation!.Children!.Single() is { FailureType: "System.IO.IOException", Mutation.After: 5 } &&
                saved.LastDispatch!.Attempts.Single().Reward!.Events.Single() is
                    { Cursor.CommittedInstructions: 1, Receipt.Disposition: "closed-failure" },
                "An ordinary callback fault after mutation was treated as an unattempted command or a successful compiled prefix.");
            using var cold = new Fixture(records, statistics: counters, challenges: saved);
            Reject(() => cold.Statistics.Mod(1, 1, "authored-cold-callback-fault"));
            Require(cold.MenuCalls == 0 && cold.Statistics.Read(2) == 5, "Cold callback fault replayed its nested value commit.");
        }
    }

    private static void UnknownRewardFamilies(string directory)
    {
        // Empty SCDA is its own authored disposition; missing SCDA never
        // falls back to a diagnostic source body, even with a zero extent.
        foreach (var empty in new[] { true, false })
        {
            Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500, empty ? [] : null,
                diagnostics: [Field("SCTX", Text("begin GameMode\nset count to 17\nend"))]));
            using var records = Load(directory); using var fixture = new Fixture(records);
            if (empty)
            {
                fixture.Statistics.Mod(1, 1, "authored-empty-byte-program");
                var reward = fixture.Challenges.Capture().LastDispatch!.Attempts.Single().Reward!;
                Require(reward is { Disposition: "authored-empty", Events.Count: 0 } && fixture.Statistics.Read(27) == 1 &&
                    BitConverter.UInt64BitsToDouble(reward.Locals.Single().Payload) == 0,
                    "Authored empty SCDA executed text or invented a compiled invocation receipt.");
            }
            else
            {
                Reject(() => fixture.Statistics.Mod(1, 1, "authored-missing-byte-program"));
                Require(fixture.Statistics.Read(27) == 0 && fixture.Challenges.Capture().LastDispatch!.Attempts.Single() is
                    { Prefix: FalloutChallengePrefix.RewardEntered, Reward: null, Error: not null },
                    "Missing SCDA was admitted as empty or diagnostic execution.");
            }
        }
        foreach (var kind in new ushort[] { 1, 0x0100, 2 })
        {
            Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500, Program(0, Set(7)), kind));
            using var records = Load(directory); using var fixture = new Fixture(records);
            Reject(() => fixture.Statistics.Mod(1, 1, "authored-unrelated-script-kind"));
            Require(fixture.Statistics.Read(27) == 0 && fixture.World.InstanceCount == 0,
                "Immediate object completion widened another script type or manufactured a placed player.");
        }
        foreach (var eventKind in new ushort[] { 2, 17, 21 })
        {
            Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500, Program(eventKind, Set(7))));
            using var records = Load(directory); using var fixture = new Fixture(records);
            Reject(() => fixture.Statistics.Mod(1, 1, "authored-unowned-event-filter"));
            Require(fixture.Statistics.Read(27) == 0, "Unowned immediate event filter silently skipped or executed its body.");
        }
        Write(directory, Challenge(0x100, 11, 1, 0, 1, script: 0x500), Script(0x500, Program(0, Set(7))));
        using (var records = Load(directory))
        {
            using var missing = new Fixture(records, bindGameMode: false);
            Reject(() => missing.Statistics.Mod(1, 1, "authored-independent-predicate-unowned"));
            Require(missing.Challenges.Capture().LastDispatch!.Attempts.Single().Reward!.Events.Single() is
                { Receipt.Disposition: "admission-refusal", Cursor.CommittedInstructions: 0 },
                "An absent independent GameMode predicate became default ready or an executed receipt.");
            using var menuFiltered = new Fixture(records, bindGameMode: false); menuFiltered.World.Menus.Publish(false, [1003]);
            menuFiltered.Statistics.Mod(1, 1, "authored-real-menu-short-circuit");
            var filtered = menuFiltered.Challenges.Capture().LastDispatch!.Attempts.Single().Reward!;
            Require(filtered.Disposition == "completed" && filtered.Events.Single() is { Filtered: true, Receipt: null, Cursor.CommittedInstructions: 0 } &&
                BitConverter.UInt64BitsToDouble(filtered.Locals.Single().Payload) == 0,
                "A genuine menu-filtered call minted execution, required an unreached scalar, or changed fresh local cells.");
            using var scalarFiltered = new Fixture(records, allows: false);
            scalarFiltered.Statistics.Mod(1, 1, "authored-independent-predicate-false");
            Require(scalarFiltered.Challenges.Capture().LastDispatch!.Attempts.Single().Reward!.Events.Single() is
                { Filtered: true, Receipt: null }, "The source independent predicate was ignored or conflated with menu mode.");
        }
        // A nested nonrecurring completion commits its flags before the
        // original linked-node retirement boundary. That boundary stays open.
        Write(directory, Challenge(0x100, 11, 1, 0, 1), Challenge(0x101, 11, 1, 0, 27));
        using var nestedRecords = Load(directory); using var nested = new Fixture(nestedRecords);
        Reject(() => nested.Statistics.Mod(1, 1, "authored-nested-bucket-retirement"));
        var saved = nested.Challenges.Capture();
        Require(saved.LastDispatch!.Children.Single() is { RebuildEntered: true, Complete: false, Error: not null } &&
            nested.Challenges.State(Key(0x101)).Completed && !nested.Challenges.State(Key(0x100)).Completed &&
            nested.Statistics.Read(27) == 1 && saved.LastDispatch!.Attempts.Single().CompletionStatisticOrdinal == 2,
            "Unowned linked-node retirement was guessed, or genuine completed child flags/counter prefixes were rolled back.");
        using var coldNested = new Fixture(nestedRecords, statistics: Copy(nested.Statistics.Capture()), challenges: Copy(saved));
        Reject(() => coldNested.Statistics.Mod(1, 1, "authored-no-nested-prefix-replay"));
        Require(coldNested.MenuCalls == 0 && coldNested.Statistics.Read(27) == 1, "Cold linked-bucket refusal replayed or discarded actual child effects.");
    }

    private static byte[] Script(uint id, byte[]? code, ushort kind = 0, params byte[][] diagnostics)
    {
        var header = new byte[20]; U32((uint)(code?.Length ?? 0)).CopyTo(header, 8); U32(1).CopyTo(header, 12);
        U16(kind).CopyTo(header, 16); header[18] = 1;
        var cell = new byte[24]; U32(1).CopyTo(cell, 0); cell[16] = 1;
        return Record("SCPT", id, fields: [Field("SCHR", header), code is null ? [] : Field("SCDA", code),
            Field("SLSD", cell), Field("SCVR", Text("count")), Join(diagnostics)]);
    }
    private static byte[] Instruction(ushort opcode, byte[]? payload = null) =>
        Join(U16(opcode), U16(checked((ushort)(payload?.Length ?? 0))), payload ?? []);
    private static byte[] Program(ushort kind, byte[] body) => Join(Instruction(0x1d),
        Instruction(0x10, Join(U16(kind), U32(checked((uint)body.Length + 4)))), body, Instruction(0x11));
    private static byte[] Set(int value)
    {
        var expression = Encoding.ASCII.GetBytes(" " + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Instruction(0x15, Join([(byte)'s'], U16(1), U16((ushort)expression.Length), expression));
    }
    private static byte[] ModStatistic(ushort index, int amount) => Instruction(0x1137,
        Join(U16(2), U16(index), [(byte)'n'], BitConverter.GetBytes(amount)));
}
