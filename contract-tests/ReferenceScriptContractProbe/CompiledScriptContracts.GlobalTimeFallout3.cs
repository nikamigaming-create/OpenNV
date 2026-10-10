using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void RunGlobalTimeFallout3()
    {
        var source = FalloutGlobalTimeSource.Read(SandboxRuntimeReceipt(FalloutSourceMainFamily.Fallout3));
        var other = FalloutGlobalTimeSource.Read(SandboxRuntimeReceipt(FalloutSourceMainFamily.NewVegas));
        Require(source.Timer.Float32Intermediates && !source.HasSoundOverride && source.InitialKeepSoundRate is null &&
            source.ContractSha256 != other.ContractSha256 && source.Timer.ContractSha256 != other.Timer.ContractSha256,
            "Standalone timer/audio adopted a foreign command, byte or arithmetic declaration.");
        Reject(() => (source with { ContractSha256 = other.ContractSha256 }).Validate());
        Reject(() => (source with { InitialKeepSoundRate = 0 }).Validate());
        Reject(() => source.AudioRate(1, 0));
        Require(source.AudioRate(-8, null) == .25f && source.AudioRate(0, null) == .25f && source.AudioRate(2, null) == 2,
            "Source audio clamp discarded zero/negative simulation rates or invented an override byte.");
        foreach (var declaration in new[] { source, other })
        {
            var counter = new AuthoredSandboxTickCounter { Now = 10 };
            using var timer = new FalloutSourceFrameTimerState(declaration.Timer, counter, Guid.NewGuid());
            using var state = new FalloutGlobalTimeState(declaration, timer);
            var actual = new GlobalTimeAuthoredPresentation();
            using var native = state.BindPresentation(actual.Binding());
            state.Set(0);
            Require(timer.ReadRates().Current == 0 && timer.ReadRates().Target == 0 && actual.Simulation == 0 && actual.Audio == .25f,
                "Genuine zero-rate source command was classified as a malformed argument.");
            counter.Now = 30; timer.Update();
            var zeroClock = timer.Capture(); var zero = state.Capture(zeroClock);
            Require(zero.CurrentRate == 0 && zero.TargetRate == 0, "Zero source rate disappeared from current capture.");
            using (var mismatched = new FalloutSourceFrameTimerState(declaration.Timer,
                new AuthoredSandboxTickCounter { Now = 1000 }, Guid.NewGuid(), Copy(zeroClock)))
                Reject(() => new FalloutGlobalTimeState(declaration, mismatched,
                    zero with { TargetRate = BitConverter.UInt32BitsToSingle(0x80000000) }));
            state.Set(-2);
            // Changing away from a zero divisor has the original floating
            // intermediate. The actual returned next update overwrites it;
            // this never invents a finite value by replacing the divisor.
            counter.Now = 60; timer.Update();
            state.RefreshPresentation();
            Require(BitConverter.SingleToUInt32Bits(timer.ScaledSeconds) == 0xbd75c290 && actual.Simulation == -2 && actual.Audio == .25f,
                "Signed source rate lost its genuine scaled timer value or independent audio clamp.");
            var capturedTimer = timer.Capture(); var captured = state.Capture(capturedTimer);
            Require(captured.KeepSoundRate == (declaration.HasSoundOverride ? (byte?)0 : null),
                "The standalone current state manufactured a source audio override field.");
            using var coldTimer = new FalloutSourceFrameTimerState(declaration.Timer,
                new AuthoredSandboxTickCounter { Now = 4000 }, Guid.NewGuid(), Copy(capturedTimer));
            using var cold = new FalloutGlobalTimeState(declaration, coldTimer, Copy(captured));
            var fresh = new GlobalTimeAuthoredPresentation();
            using var rebound = cold.BindPresentation(fresh.Binding());
            Require(fresh.Simulation == -2 && fresh.Audio == .25f && cold.Capture(coldTimer.Capture()).Revision == captured.Revision,
                "Cold signed rates replayed a source setter or rebound the retired native owner.");
            if (!declaration.HasSoundOverride)
            {
                var before = JsonSerializer.Serialize(state.Capture(timer.Capture()));
                Reject(() => state.Set(1, 0));
                Require(JsonSerializer.Serialize(state.Capture(timer.Capture())) == before,
                    "A foreign optional argument entered the genuine one-Float command.");
            }
        }
        foreach (var declaration in new[] { source.Timer, other.Timer })
        {
            using var timer = new FalloutSourceFrameTimerState(declaration,
                new AuthoredSandboxTickCounter { Now = 0 }, Guid.NewGuid());
            timer.SetRate(.1f, true); timer.SetUseRateWrites(true); timer.SetRate(2, false); timer.AdjustRate();
            var expected = declaration.Float32Intermediates ? 0x3de147afu : 0x3de147aeu;
            Require(BitConverter.SingleToUInt32Bits(timer.ReadRates().Current) == expected,
                "Timer adjustment skipped the selected Float32 versus wider intermediate store.");
        }
        var immediate = FalloutImmediateScriptSource.Read(SandboxRuntimeReceipt(FalloutSourceMainFamily.Fallout3));
        var tail = FalloutMainCachedTailSource.Read(FalloutMainScriptCallerSource.Read(immediate));
        Require(tail.RequestCount == 2 && tail.ClearRequestBeforeDispatch(0) && !tail.ClearRequestAfterDispatch(0) &&
            !tail.ClearRequestBeforeDispatch(1) && tail.ClearRequestAfterDispatch(1),
            "Standalone Main adopted the foreign request count or moved its source clear across an entered child.");
        using (var current = new FalloutMainCachedTailState(tail, "authored-stack", Guid.NewGuid(), new AuthoredSandboxTickCounter()))
        {
            var snapshot = current.Capture();
            Require(snapshot.RequestCountdowns.Count == 2 && snapshot.FrameCounter == 1 && snapshot.HavokTauBits == 0x3f800000,
                "Standalone tail invented a third request field or replaced its original initialized cells.");
            Reject(() => FalloutMainCachedTailState.Validate(snapshot with { RequestCountdowns = [0, 0, 0] }));
        }
        GlobalTimeFallout3FullReader(source);
        Console.WriteLine("OPENNV_SOURCE_FO3_GLOBAL_TIME_PASS oneFloat=true absentAudioByte=true distinctArithmetic=true signedCurrentCold=true nativeSdk=unrun");
    }

    private static void GlobalTimeFallout3FullReader(FalloutGlobalTimeSource source)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-standalone-global-time-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var extra in new[] { false, true })
            {
                var argument = extra ? Join(U16(2), BoundQueryDouble(-.5), Integer(0)) : Join(U16(1), BoundQueryDouble(-.5));
                BoundQueryFixture(directory, Join(Call(0x1186, argument), Set('f', 1, " 7")), records =>
                {
                    using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
                    using var timer = new FalloutSourceFrameTimerState(source.Timer, new AuthoredSandboxTickCounter(), Guid.NewGuid());
                    using var owner = new FalloutGlobalTimeState(source, timer);
                    var actual = new GlobalTimeAuthoredPresentation(); using var native = owner.BindPresentation(actual.Binding());
                    var executor = GlobalTimeExecutor(records, world, quests, owner);
                    var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                    void Run() { foreach (var _ in executor.CompiledSteps(Key(0x20), program, program.Events[0])) { } }
                    if (extra)
                    {
                        Reject(Run);
                        Require(timer.ReadRates().Current == 1 && quests.Variable(Key(0x20), 1) == 0,
                            "Full-reader standalone command consumed a nonexistent second argument or committed its suffix.");
                    }
                    else
                    {
                        Run();
                        Require(timer.ReadRates().Current == -.5f && actual.Audio == .25f && quests.Variable(Key(0x20), 1) == 7,
                            "Full-reader standalone command failed to call the actual bound rate authority.");
                    }
                });
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
