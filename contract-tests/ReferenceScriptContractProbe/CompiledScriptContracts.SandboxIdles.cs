using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunSandboxIdles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-sandbox-idles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            var packageData = new byte[12]; packageData[4] = 12;
            var vetoData = (byte[])packageData.Clone(); vetoData[8] = 23;
            var location = Join(U32(1), U32(0x800), U32(0));
            var idleData = new byte[8]; idleData[4] = 2;
            var placed = Record("REFR", 0xa00, Field("NAME", U32(0x90)), Field("DATA", new byte[24]));
            var group = new byte[24 + placed.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); placed.CopyTo(group, 24);
            File.WriteAllBytes(path, Join(Tes4(),
                Record("CELL", 0x800, Field("DATA", [1])),
                Record("IDLE", 0x70, Field("MODL", Text("authored/selected.kf")), Field("DATA", idleData)),
                Record("IDLE", 0x71, Field("MODL", Text("authored/second.kf")), Field("DATA", idleData)),
                Record("IDLM", 0x90, Field("IDLF", [0]), Field("IDLC", [1]), Field("IDLT", new byte[4]), Field("IDLA", U32(0x71))),
                Record("PACK", 0x80, Field("EDID", Text("AuthoredContinuousSandbox")), Field("PKDT", packageData),
                    Field("PLDT", location), Field("IDLF", [0]), Field("IDLC", [2]), Field("IDLT", BitConverter.GetBytes(.375f)),
                    Field("IDLA", Join(U32(0x70), U32(0x71)))),
                Record("PACK", 0x81, Field("EDID", Text("AuthoredVetoedSandbox")), Field("PKDT", vetoData), Field("PLDT", location)), group));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            var record = records.GetEffective(Key(0x80));
            var declaration = FalloutScriptPackage.Read(record);
            var source = FalloutSandboxPackage.Read(record);
            var scene = FalloutCellSceneReader.Read(records, Key(0x800));
            Require(scene.References.Single(reference => reference.FormKey == Key(0xa00)).Base == Key(0x90) &&
                FalloutIdleCollection.Read(records.GetEffective(Key(0x90))).Idles.Single() == Key(0x71),
                "Authored Sandbox marker lost its genuine placed/source IDLM declarations.");
            Require(source.LocationType == 1 && source.Location == Key(0x800) && source.Radius == 0 && source.Vetoes == 0,
                "Sandbox full-reader source area/independent vetoes changed.");
            var vetoed = FalloutSandboxPackage.Read(records.GetEffective(Key(0x81)));
            Require(vetoed.NoEating && vetoed.NoSleeping && vetoed.NoConversation && vetoed.NoFurniture &&
                !vetoed.NoIdleMarkers && !vetoed.NoWandering, "Sandbox independent source vetoes were conflated.");

            var replay = new FalloutIdleReplayState(); var draws = 0; var predicates = new List<FalloutFormKey>();
            var collection = new FalloutIdleCollectionPlayback(declaration, replay,
                idle => { predicates.Add(idle); return idle != Key(0x70); }, bound => { ++draws; Require(bound == 1, "Idle filtering did not precede bounded selection."); return 0; });
            Require(collection.Select() == Key(0x71) && predicates.SequenceEqual(declaration.Idles) && draws == 1,
                "Creature/NPC idle collection source filtering/order differs.");
            replay.Started(Key(0x71), FalloutIdleAnimationData.Read(records.GetEffective(Key(0x71))).ReplayDelaySeconds);
            collection.Finish(); collection.AdvanceWait(.125);
            var saved = collection.Capture();
            var cold = new FalloutIdleCollectionPlayback(declaration, new(), _ => throw new IOException("Cold predicates must not run."),
                _ => throw new IOException("Cold selection must not draw."));
            cold.Restore(saved);
            Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()) && saved.WaitSeconds == .25,
                "Cold idle collection lost its exact fractional wait/cursor.");
            var invalidRandom = new FalloutIdleCollectionPlayback(declaration, new(), _ => true, bound => bound);
            try { invalidRandom.Select(); throw new Exception("Out-of-bound idle random was admitted."); }
            catch (InvalidDataException) { }

            var packageHash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
            var election = new FalloutFollowElection(.125, null, 0, null, false, new FalloutActorActivityState().Capture(), 0, null, null);
            var state = new FalloutSandboxState(source, packageHash, new(Key(0x800), null, 0), bound =>
            { Require(bound == 384, "Sandbox candidate weights changed."); return 128; });
            state.ObserveLocation(true);
            var candidates = new[] { new FalloutSandboxCandidate(Key(0xa00), 4, 1), new FalloutSandboxCandidate(null, 3, 2) };
            state.Select(candidates, _ => 2.5f);
            var entered = 0; state.EnterAction(_ => ++entered);
            var retired = 0;
            Require(!state.Advance(1.25f, actualActionReturned: true, _ => ++retired) && entered == 1 && retired == 0,
                "Sandbox timer fabricated early action retirement.");
            var prefix = state.Capture(election);
            try { state.Advance(1.25f, actualActionReturned: true, _ => throw new IOException("authored-native-retire")); }
            catch (IOException) { }
            var failed = state.Capture(election);
            Require(failed.SelectedIndex == prefix.SelectedIndex && failed.ActionEntered && failed.Remaining == 0 &&
                failed.Failure == "authored-native-retire" && failed.ActionWeights.SequenceEqual(prefix.ActionWeights),
                "Sandbox failed native retirement lost its consumed action/clock/draw prefix.");
            var restored = new FalloutSandboxState(source, packageHash, failed.Area, _ => throw new IOException("Cold must not redraw."), failed);
            try { restored.EnterAction(_ => throw new Exception("Cold replayed a committed action.")); throw new Exception("Cold failure was admitted."); }
            catch (NotSupportedException) { }

            var durationFailure = new FalloutSandboxState(source, packageHash, new(Key(0x800), null, 0), _ => 0);
            durationFailure.ObserveLocation(true);
            try { durationFailure.Select(candidates, _ => throw new IOException("authored-duration")); }
            catch (IOException) { }
            var failedDuration = durationFailure.Capture(election);
            Require(failedDuration.SelectedIndex == 0 && !failedDuration.ActionEntered && failedDuration.ActionWeights.All(value => value == 128) &&
                failedDuration.Failure == "authored-duration", "Sandbox duration failure forgot its genuinely consumed choice.");
            var vetoBits = new ushort[] { 16, 2, 1, 32, 8, 4 };
            for (var action = 0; action < 6; ++action)
            {
                var refused = new FalloutSandboxState(source with { Vetoes = vetoBits[action] }, packageHash,
                    new(Key(0x800), null, 0), _ => throw new Exception("A vetoed family must not consume a draw."));
                refused.ObserveLocation(true);
                try
                {
                    refused.Select([new(action is 2 or 3 ? null : Key(0xa00), action, 1)],
                        _ => throw new Exception("A vetoed family must not enter duration."));
                    throw new Exception("An independent Sandbox family veto was ignored.");
                }
                catch (InvalidDataException) { }
            }
            Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Sandbox full-reader fixture source changed.");
            Console.WriteLine("sandbox-idles: full-reader area/vetoes; source candidate order; bounded random; fractional cold wait; duration/native-retire prefixes; no callback replay PASS");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
