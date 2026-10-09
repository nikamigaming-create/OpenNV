using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunSandboxActions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-sandbox-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            var acbs = new byte[24]; var inherited = (byte[])acbs.Clone(); inherited[22] = 16;
            var aidt = new byte[20]; aidt[2] = 73;
            var wrong = new byte[19]; wrong[2] = 73;
            var package = new byte[12]; package[4] = 12;
            var reference = Record("ACHR", 0xa00, Field("NAME", U32(0x83)), Field("DATA", new byte[24]));
            var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); reference.CopyTo(group, 24);
            File.WriteAllBytes(path, Join(Tes4(),
                Record("CELL", 0x800, Field("DATA", [1])),
                Record("NPC_", 0x83, Field("ACBS", acbs), Field("AIDT", aidt)),
                Record("NPC_", 0x84, Field("ACBS", inherited), Field("TPLT", U32(0x83))),
                Record("CREA", 0x85, Field("ACBS", acbs), Field("AIDT", wrong)),
                Record("PACK", 0x80, Field("EDID", Text("AuthoredSandboxActionOwner")), Field("PKDT", package),
                    Field("PLDT", Join(U32(1), U32(0x800), U32(0)))), group));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            Require(FalloutSandboxActionSource.Energy(records, records.GetEffective(Key(0x83))) == 73 &&
                FalloutSandboxActionSource.Energy(records, records.GetEffective(Key(0x84))) == 73,
                "Sandbox energy did not use winning AI-data group16/byte2.");
            try { FalloutSandboxActionSource.Energy(records, records.GetEffective(Key(0x85))); throw new Exception("Incomplete AIDT energy was accepted."); }
            catch (InvalidDataException) { }

            using var world = new FalloutReferenceWorld(records);
            var referenceRecord = records.GetEffective(Key(0xa00)); var actorRecord = records.GetEffective(Key(0x83));
            var candidate = new FalloutSandboxCandidate(Key(0xa00), 5, 1, Key(0x83),
                Hash(referenceRecord), Hash(actorRecord));
            world.RequireSandboxCandidateSource(candidate);
            try { world.RequireSandboxCandidateSource(candidate with { BaseSha256 = new string('0', 64) }); throw new Exception("Cached Sandbox base-byte drift was accepted."); }
            catch (InvalidDataException) { }
            try { world.RequireSandboxCandidateSource(candidate with { ReferenceSha256 = new string('0', 64) }); throw new Exception("Cached Sandbox reference-byte drift was accepted."); }
            catch (InvalidDataException) { }

            var durationCalls = 0;
            foreach (var (scalarStores, expectedUpperBits) in new[] { (false, 0x41700000), (true, 0x41700001) })
            {
                var owner = new FalloutSandboxActionSource(new string('a', 64), scalarStores);
                var duration = owner.Duration(new(5, 2, .25f, .5f, .2f, 2), (minimum, maximum) =>
                {
                    ++durationCalls;
                    Require(BitConverter.SingleToInt32Bits(minimum) == 0x41200000 && BitConverter.SingleToInt32Bits(maximum) == expectedUpperBits,
                        $"Sandbox affine duration interval differs from independently authored Float32 values: stores={scalarStores}, lower=0x{BitConverter.SingleToInt32Bits(minimum):X8}, upper=0x{BitConverter.SingleToInt32Bits(maximum):X8}.");
                    return 12.25f;
                });
                Require(duration == 12.25f, "Sandbox duration replaced the actual RNG result.");
            }
            Require(durationCalls == 2, "Sandbox duration did not consume each actual interval draw.");

            var availability = new FalloutSandboxAvailability(true, false, false);
            var history = new byte[] { 128, 128, 128, 128, 128, 128 };
            var normal = new FalloutSandboxActionContext(-1, true, 2).Weights(history, availability);
            Require(normal.SequenceEqual(new[] { 128, 0, 133, 128, 128, 0 }),
                "Sandbox independent normal/sleep/dialogue context weights changed.");
            var sleep = new FalloutSandboxActionContext(0, true, 0).Weights(history, new(false, false, true));
            Require(sleep.SequenceEqual(new[] { 0, 10, 0, 0, 0, 0 }), "Sandbox available source sleep window selected the wrong family.");
            var unavailableSleep = new FalloutSandboxActionContext(0, true, 0).Weights(history, availability);
            Require(unavailableSleep[1] == 0 && unavailableSleep[2] == 133, "Unavailable sleep fabricated a scheduled candidate.");

            var order = new FalloutSandboxDiscovery([candidate, new(null, 3, 1)], new(false, false, false));
            var registry = new FalloutSandboxActionRegistry(); var scans = 0; var draws = 0;
            FalloutSandboxDiscovery Discover() { ++scans; return order; }
            uint Draw(uint bound) { ++draws; Require(bound == 3, "Sandbox rescan lost its inclusive source interval."); return 2; }
            var clock = new FalloutSandboxTimerSample(new string('a', 64), 7, 100);
            registry.Observe(clock, (9, 11), Draw, Discover, 5);
            registry.RememberReturned(candidate);
            registry.Observe(clock, (9, 11), Draw, Discover, 5);
            Require(scans == 1 && draws == 1 && registry.RepeatedReference == candidate.Reference,
                "Sandbox selection return lost its deferred repeat prefix or redrew before the source deadline.");
            registry.Observe(clock with { Milliseconds = 105 }, (9, 11), Draw, Discover, 5);
            Require(registry.RepeatedReference == candidate.Reference, "Sandbox repeat suppression cleared at equality rather than strictly after.");
            registry.Observe(clock with { Milliseconds = 106 }, (9, 11), Draw, Discover, 5);
            Require(registry.RepeatedReference is null, "Sandbox repeat suppression outlived its exact source deadline.");
            registry.Observe(clock with { Milliseconds = 111 }, (9, 11), Draw, Discover, 5);
            Require(scans == 2 && draws == 2, "Sandbox rescan failed to run at equality.");
            var captured = registry.Capture()!;
            var cold = new FalloutSandboxActionRegistry(JsonSerializer.Deserialize<FalloutSandboxRegistrySnapshot>(JsonSerializer.Serialize(captured))!);
            cold.Observe(clock with { Milliseconds = 111 }, (9, 11), _ => throw new Exception("Cold redrew a retained interval."),
                () => throw new Exception("Cold rebuilt a retained registry."), 5);
            Require(JsonSerializer.Serialize(captured) == JsonSerializer.Serialize(cold.Capture()), "Cold registry lost its exact retained source clock/order.");
            try { cold.Observe(clock with { Epoch = 8 }, (9, 11), Draw, Discover, 5); throw new Exception("Cold registry relabelled an unrelated timer epoch."); }
            catch (NotSupportedException) { }

            var failedRegistry = new FalloutSandboxActionRegistry();
            try { failedRegistry.Observe(clock, (9, 11), _ => throw new IOException("authored-rescan-draw"), Discover, 5); }
            catch (IOException) { }
            var failedScan = failedRegistry.Capture()!;
            Require(failedScan.ScanEntered && failedScan.Registry is not null && failedScan.Failure == "authored-rescan-draw",
                "Failed rescan draw lost its genuinely rebuilt registry/entered suffix.");
            var failedCold = new FalloutSandboxActionRegistry(failedScan);
            try { failedCold.Observe(clock, (9, 11), Draw, Discover, 5); throw new Exception("Cold retried an entered failed rescan."); }
            catch (InvalidOperationException) { }

            var source = FalloutSandboxPackage.Read(records.GetEffective(Key(0x80)));
            var hash = Hash(records.GetEffective(Key(0x80)));
            var election = new FalloutFollowElection(.5, null, 0, null, false, new FalloutActorActivityState().Capture(), 0, null, null);
            var state = new FalloutSandboxState(source, hash, new(Key(0x800), null, 0), _ => 0);
            var entries = 0;
            state.ObserveLocation(true); state.Select([new(null, 3, 1)], _ => .25f); state.EnterAction(_ => ++entries);
            var requests = 0; var returns = 0; var retired = false;
            Require(!state.AdvanceNative(.25f, _ => ++requests, _ => retired, _ => ++returns) && entries == 1 && requests == 1 && returns == 0,
                "Native retirement request was mistaken for the actual child return.");
            Require(!state.AdvanceNative(0, _ => ++requests, _ => retired, _ => ++returns) && requests == 1,
                "Pending actual native retirement replayed its request callback.");
            retired = true;
            Require(state.AdvanceNative(0, _ => ++requests, _ => retired, _ => ++returns) && requests == 1 && returns == 1 && state.Selected is null,
                "Actual retired child failed to close only its consumed action.");
            var broken = new FalloutSandboxState(source, hash, new(Key(0x800), null, 0), _ => 0);
            broken.ObserveLocation(true); broken.Select([new(null, 3, 1)], _ => 0); broken.EnterAction(_ => ++entries);
            try { broken.AdvanceNative(0, _ => throw new IOException("authored-retire-request"), _ => true, _ => ++returns); }
            catch (IOException) { }
            var brokenState = broken.Capture(election);
            Require(brokenState.RetirementEntered && brokenState.ActionEntered && brokenState.SelectedIndex == 0 &&
                brokenState.Failure == "authored-retire-request", "Actual native retirement request failure lost its once-only committed prefix.");
            Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Sandbox full-reader authored inputs changed.");
            Console.WriteLine("OPENNV_SANDBOX_ACTION_CONTRACT_PASS sourceEnergyTemplate=true sourceByteDriftRefused=true sixContexts=true " +
                "rescanDeadline=true strictRepeatDeadline=true coldNoRedraw=true failedSuffixRetained=true nativeReturnDistinct=true " +
                "ordinaryNativeActionsUnexecuted=true originalRandomParityUnverified=true");
        }
        finally { Directory.Delete(directory, true); }

        static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    }
}
