using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReputationConditionContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-conditions-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Repu.esm"), Join(Header(),
                Record("REPU", 1, Field("DATA", BitConverter.GetBytes(100f))),
                Record("FACT", 2), Record("ACTI", 3), Record("CELL", 0x80, Field("DATA", [1])),
                Group(0x80, Record("REFR", 0x30, Field("NAME", BitConverter.GetBytes(3u)), Field("DATA", new byte[24])))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            File.WriteAllBytes(Path.Combine(directory.FullName, "Query.esp"), Join(Header("Other.esm", "Repu.esm"),
                Record("INFO", 0x02000010, Field("CTDA", Condition(1, 0))),
                Record("MESG", 0x02000011, Field("DNAM", BitConverter.GetBytes(1u)),
                    Field("INAM", new byte[4]), Field("DESC", Text("Question")),
                    Field("ITXT", Text("Positive")), Field("CTDA", Condition(1, 3, 5)),
                    Field("ITXT", Text("Negative")), Field("CTDA", Condition(0, 3, 8)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Repu.esm", "Other.esm", "Query.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var form = new FalloutFormKey("Repu.esm", 1);
            var owner = records.GetEffective(new("Query.esp", 0x10));
            var condition = FalloutCondition.Read(owner).Single();
            Check(condition.Function == 573 && condition.FormArgument1 == form, "CTDA lost master-adjusted REPU identity.");
            float Query(FalloutCondition value) => world.EvaluateReputationCondition(value) ??
                throw new NotSupportedException("Unowned condition.");
            var identity = new FalloutDialogueSpeaker(new("Repu.esm", 0x20), new("Repu.esm", 0x20),
                new("Repu.esm", 0x21), "fixture", null, false);
            var dialogue = new FalloutDialogueConditions(records, new(records), new("Repu.esm", 0x30), identity,
                Query, listener: new("Repu.esm", 0x31), listenerIdentity: identity);
            Check(dialogue.Evaluate(condition) == 0, "Fresh condition did not use initialized zero.");
            world.SetReputation(form, 1, 5.25); world.SetReputation(form, 0, 8.5);
            foreach (var selector in new uint[] { 0, 1, 2, uint.MaxValue })
                foreach (var runOn in new uint[] { 0, 1, 2 })
                    Check(dialogue.Evaluate(condition with { Argument2 = selector, RunOn = runOn, Reference = 0x01000030 }) ==
                        (selector == 1 ? 5.25f : 8.5f), "Reputation depended on subject or validated the raw selector as a script command.");
            foreach (var (operation, comparison, expected) in new (byte, float, bool)[]
                { (0,5.25f,true),(1,5.25f,false),(2,5,true),(3,5.25f,true),(4,6,true),(5,5.25f,true) })
                Check(FalloutCondition.AllPass([condition with { Flags = (byte)(operation << 5), Comparison = comparison }], dialogue.Evaluate) == expected,
                    "Reputation changed shared CTDA comparison semantics.");
            var message = FalloutSourceMessage.Read(records.GetEffective(new("Query.esp", 0x11)));
            Check(message.ResolveButtons(records, Query).ButtonIndices!.SequenceEqual(new[] { 0, 1 }),
                "Actual conditional message buttons did not consume shared reputation.");
            world.SetReputation(form, 1, -2);
            Check(dialogue.Evaluate(condition) == -2 && message.ResolveButtons(records, Query).ButtonIndices!.SequenceEqual(new[] { 1 }),
                "Condition retained stale reputation between evaluations.");
            var before = JsonSerializer.Serialize(world.CaptureReputations());
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
            Check(cold.EvaluateReputationCondition(condition) == -2 && cold.EvaluateReputationCondition(condition with { Argument2 = 0 }) == 8.5f,
                "Cold condition lost known axes.");
            Reject(() => dialogue.Evaluate(condition with { Argument1 = 0x01000002 }));
            Check(dialogue.Evaluate(condition with { RunOn = 2, Reference = 0 }) == -2,
                "Subject-independent query rejected source-permitted null explicit context.");
            Reject(() => dialogue.Evaluate(condition with { RunOn = 2, Reference = 0x0100ffff }));
            Reject(() => dialogue.Evaluate(condition with { RunOn = 2, Reference = 0x01000002 }));
            Reject(() => dialogue.Evaluate(condition with { RunOn = 3 }));
            Reject(() => new FalloutDialogueConditions(records, new(records), new("Repu.esm", 0x30), identity).Evaluate(condition));
            Reject(() => FalloutCondition.AllPass([condition with { Flags = 4 }], Query));
            Check(world.EvaluateReputationCondition(condition with { Function = 576 }) is null &&
                JsonSerializer.Serialize(world.CaptureReputations()) == before, "Refusal/query mutated state or admitted another reputation query.");
            cold.RestoreReputations(null);
            Reject(() => cold.EvaluateReputationCondition(condition));
            cold.SetReputation(form, 1, 3);
            Check(cold.EvaluateReputationCondition(condition) == 3, "Explicit legacy axis was not available to CTDA.");
            Reject(() => cold.EvaluateReputationCondition(condition with { Argument2 = 0 }));
            Console.WriteLine("OPENNV_REPUTATION_CONDITION_PASS sourceMasters=true rawSelector=true sharedDialogueAndMessage=true comparisons=true live=true cold=true legacyUnknown=true refusals=true noMutation=true ordinaryAndRetail=unverified");
        }
        finally
        {
            foreach (var name in new[] { "Repu.esm", "Other.esm", "Query.esp" }) File.Delete(Path.Combine(directory.FullName, name));
            directory.Delete();
        }
    }
    internal static void RunPercentage()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-percentage-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Repu.esm"), Join(Header(),
                Record("REPU", 1, Field("DATA", BitConverter.GetBytes(10f))),
                Record("REPU", 2, Field("DATA", BitConverter.GetBytes(0f))),
                Record("REPU", 3, Field("DATA", BitConverter.GetBytes(-10f))),
                Record("REPU", 4, Field("DATA", BitConverter.GetBytes(float.Epsilon))),
                Record("FACT", 5), Record("REPU", 6, Field("DATA", BitConverter.GetBytes(float.NaN)))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            File.WriteAllBytes(Path.Combine(directory.FullName, "Query.esp"), Join(Header("Other.esm", "Repu.esm"),
                Record("INFO", 0x02000010, Field("CTDA", Condition(1, 0, function: 574)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Repu.esm", "Other.esm", "Query.esp"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Form(uint id) => new("Repu.esm", id);
            var condition = FalloutCondition.Read(records.GetEffective(new("Query.esp", 0x10))).Single();
            float Query(FalloutCondition value) => world.EvaluateReputationCondition(value) ?? throw new NotSupportedException("Unowned query.");
            Check(Query(condition) == 0, "Fresh percentage invented nonzero reputation.");
            world.SetReputation(Form(1), 1, 125); world.SetReputation(Form(1), 0, -20);
            foreach (var runOn in new uint[] { 0, 1, 2 })
                foreach (var selector in new uint[] { 0, 1, 2, uint.MaxValue })
                    Check(Query(condition with { RunOn = runOn, Argument2 = selector }) == (selector == 1 ? 12.5f : -2f),
                        "Percentage changed raw selector, subject independence, fraction scale or unclamped range.");
            world.SetReputation(Form(1), 1, 1.1);
            Check(BitConverter.SingleToInt32Bits(Query(condition)) == BitConverter.SingleToInt32Bits(.11f),
                "Percentage did not retain source float32 quotient precision.");
            Check(FalloutCondition.AllPass([condition with { Flags = 0x60, Comparison = .11f }], Query),
                "Percentage did not use shared CTDA comparison.");
            world.SetReputation(Form(3), 1, 1.5);
            Check(Query(condition with { Argument1 = 0x01000003 }) == -.15f, "Finite signed capacity was replaced or clamped.");
            world.SetReputation(Form(4), 1, float.MaxValue);
            var before = JsonSerializer.Serialize(world.CaptureReputations());
            Reject(() => Query(condition with { Argument1 = 0x01000002 })); // 0/0 must not become fresh zero.
            Reject(() => Query(condition with { Argument1 = 0x01000004 }));
            Reject(() => Query(condition with { Argument1 = 0x01000005 }));
            Reject(() => Query(condition with { Argument1 = 0x01000006 }));
            Reject(() => Query(condition with { RunOn = 3 }));
            Reject(() => Query(condition with { RunOn = 2, Reference = 0x0100ffff }));
            Reject(() => FalloutCondition.AllPass([condition with { Flags = 4 }], Query));
            Check(JsonSerializer.Serialize(world.CaptureReputations()) == before, "Percentage refusal mutated reputation.");
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
            Check(cold.EvaluateReputationCondition(condition) == .11f, "Cold percentage lost its source quotient.");
            cold.RestoreReputations(null);
            Reject(() => cold.EvaluateReputationCondition(condition));
            cold.SetReputation(Form(1), 1, 12.5);
            Check(cold.EvaluateReputationCondition(condition) == 1.25f, "Explicit legacy axis was unavailable to percentage.");
            Reject(() => cold.EvaluateReputationCondition(condition with { Argument2 = 2 }));
            Check(world.EvaluateReputationCondition(condition with { Function = 576 }) is null, "Percentage admitted unqualified rank semantics.");
            Console.WriteLine("OPENNV_REPUTATION_PERCENTAGE_PASS sourceMasters=true fractionScale=true noClamp=true rawSelector=true float32=true " +
                "signedCapacity=true finiteResultRequired=true knownUnknown=true cold=true contextRefusals=true noMutation=true ordinaryAndRetail=unverified");
        }
        finally
        {
            foreach (var name in new[] { "Repu.esm", "Other.esm", "Query.esp" }) File.Delete(Path.Combine(directory.FullName, name));
            directory.Delete();
        }
    }

    internal static void RunThreshold()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-threshold-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Repu.esm"), Join(Header(),
                Record("REPU", 1, Field("DATA", BitConverter.GetBytes(4f))),
                Record("REPU", 2, Field("DATA", BitConverter.GetBytes(0f))),
                Setting(10, "fReputationThresholdOne", .25f), Setting(11, "fReputationThresholdTwo", .5f),
                Setting(12, "fReputationThresholdThree", .75f)));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            File.WriteAllBytes(Path.Combine(directory.FullName, "Query.esp"), Join(Header("Other.esm", "Repu.esm"),
                Record("INFO", 0x02000010, Field("CTDA", Condition(0, 0, function: 575)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Repu.esm", "Other.esm", "Query.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var form = new FalloutFormKey("Repu.esm", 1);
            var condition = FalloutCondition.Read(records.GetEffective(new("Query.esp", 0x10))).Single();
            float Query(FalloutCondition value) => world.EvaluateReputationCondition(value) ?? throw new NotSupportedException("Unowned query.");
            void Axes(float positive, float negative)
            { world.SetReputation(form, 1, positive * 4); world.SetReputation(form, 0, negative * 4); }
            // Independent literal oracle reduced from the qualified numeric decision contract.
            int[][] tables = [
                [1,0,0,0, 0,3,0,0, 0,0,4,2, 0,0,2,5],
                [1,0,0,0, 4,0,0,0, 5,2,0,0, 6,3,0,0],
                [1,4,5,6, 0,0,2,3, 0,0,0,0, 0,0,0,0]];
            float[] ratios = [0, .375f, .625f, .875f];
            foreach (var p in Enumerable.Range(0, 4)) foreach (var n in Enumerable.Range(0, 4))
            {
                Axes(ratios[p], ratios[n]);
                var before = JsonSerializer.Serialize(world.CaptureReputations());
                foreach (var selector in new uint[] { 0, 1, 2, 3, 255, 256, 257, 258, 259, uint.MaxValue })
                {
                    var low = selector & 255;
                    var expected = low <= 2 ? tables[low][p * 4 + n] : p == 0 && n == 0 ? 1 : 0;
                    Check(Query(condition with { Argument2 = selector }) == expected, "Threshold matrix or low-byte selector changed.");
                }
                Check(JsonSerializer.Serialize(world.CaptureReputations()) == before, "Threshold query mutated axes.");
            }
            foreach (var (ratio, expected) in new (float, int)[] {
                (MathF.BitDecrement(.25f),1),(.25f,4),(MathF.BitDecrement(.5f),4),(.5f,5),
                (MathF.BitDecrement(.75f),5),(.75f,6) })
            { Axes(ratio, 0); Check(Query(condition with { Argument2 = 1 }) == expected, "Threshold inclusivity or float boundary changed."); }
            Check(records.NumericSettings.Set("fReputationThresholdOne", .75) &&
                records.NumericSettings.Set("fReputationThresholdTwo", .25) &&
                records.NumericSettings.Set("fReputationThresholdThree", .5), "Live threshold setting was not admitted.");
            Axes(.6f, 0); var revision = records.NumericSettings.Revision;
            Check(Query(condition with { Argument2 = 1 }) == 6 && records.NumericSettings.Revision == revision,
                "Thresholds were sorted, cached or mutated while queried.");
            records.NumericSettings.Set("fReputationThresholdOne", -.1);
            records.NumericSettings.Set("fReputationThresholdTwo", -.05);
            records.NumericSettings.Set("fReputationThresholdThree", 0);
            using (var fresh = new FalloutReferenceWorld(records))
                Check(fresh.EvaluateReputationCondition(condition) == 5, "Fresh axes bypassed live threshold classification.");
            records.NumericSettings.Set("fReputationThresholdOne", .5);
            records.NumericSettings.Set("fReputationThresholdTwo", .5);
            records.NumericSettings.Set("fReputationThresholdThree", .5);
            Axes(.5f, 0); Check(Query(condition with { Argument2 = 1 }) == 6, "Equal thresholds acquired an invented ordering refusal.");
            records.NumericSettings.Set("fReputationThresholdOne", .25);
            records.NumericSettings.Set("fReputationThresholdThree", .75);
            Axes(.875f, .375f);
            var capture = JsonSerializer.Serialize(world.CaptureReputations());
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(capture));
            Check(cold.EvaluateReputationCondition(condition with { Argument2 = 1 }) == 3, "Cold threshold changed its source inputs.");
            cold.RestoreReputations(null); cold.SetReputation(form, 1, 0);
            Reject(() => cold.EvaluateReputationCondition(condition with { Argument2 = 1 }));
            cold.SetReputation(form, 0, 0);
            Check(cold.EvaluateReputationCondition(condition with { Argument2 = uint.MaxValue }) == 1,
                "Both-zero result was not applied before selector dispatch.");
            Reject(() => Query(condition with { Argument1 = 0x01000002 }));
            Reject(() => Query(condition with { RunOn = 3 }));
            Reject(() => Query(condition with { RunOn = 2, Reference = 0x0100ffff }));
            Reject(() => FalloutCondition.AllPass([condition with { Flags = 4 }], Query));
            Console.WriteLine("OPENNV_REPUTATION_THRESHOLD_PASS matrix48=true rawLowByte=true bothAxesRequired=true sourceOrder=true boundaries=true " +
                "liveSettings=true noSorting=true freshClassified=true cold=true unknownRefused=true noMutation=true ordinaryAndRetail=unverified");
        }
        finally
        {
            foreach (var name in new[] { "Repu.esm", "Other.esm", "Query.esp" }) File.Delete(Path.Combine(directory.FullName, name));
            directory.Delete();
        }
    }

    private static byte[] Setting(uint id, string name, float value) =>
        Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));

    private static byte[] Condition(uint selector, byte comparison, float value = 0, ushort function = 573)
    {
        var bytes = new byte[28]; bytes[0] = (byte)(comparison << 5);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), value);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x01000001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), selector); return bytes;
    }
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        Join(new[] { Field("HEDR", new byte[12]) }.Concat(masters.Select(name => Join(Field("MAST", Text(name)), Field("DATA", new byte[8])))).ToArray()));
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unqualified reputation condition was admitted.");
    }
}
