using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void GlobalTimeCurrentCaptureJoins(FalloutGlobalTimeSource source)
    {
        var player = new FalloutFormKey("Authored.esm", 0x14);
        FalloutCombatActorIdentity Identity(FalloutFormKey form) => new(form, new("Authored.esm", 7),
            "ENGINE_PLAYER", 0, source.Timer.EngineSha256, "NPC_", 0, new('4', 64), true);
        using var process = new FalloutActorProcessRuntimeState(
            FalloutActorProcessRuntimeDeclaration.ForExecutable(source.Timer.EngineSha256), "authored-global-time-join", player, Identity);
        var constructor = process.Capture();
        Reject(() => FalloutActorProcessRuntimeState.RequireCurrentGlobalTime(constructor));
        using var timer = new FalloutSourceFrameTimerState(source.Timer,
            new AuthoredSandboxTickCounter { Now = 100 }, constructor.CapturedProcess);
        using var global = new FalloutGlobalTimeState(source, timer);
        var values = new GlobalTimeAuthoredPresentation();
        using var binding = global.BindPresentation(values.Binding());
        global.Set(.5f, -1);
        var clock = timer.Capture();
        var joined = constructor with { CachedTimer = clock, GlobalTime = global.Capture(clock) };
        FalloutActorProcessRuntimeState.Validate(joined);
        FalloutActorProcessRuntimeState.RequireCurrentGlobalTime(joined);
        foreach (var invalid in new[]
        {
            joined with { GlobalTime = null }, joined with { CachedTimer = null },
            joined with { GlobalTime = joined.GlobalTime! with { CapturedProcess = Guid.NewGuid() } },
            joined with { GlobalTime = joined.GlobalTime! with { TimerEpoch = clock.Epoch + 1 } },
            joined with { GlobalTime = joined.GlobalTime! with { TimerSequence = clock.Sequence + 1 } },
            joined with { GlobalTime = joined.GlobalTime! with { TargetRate = 1 } },
            joined with { GlobalTime = joined.GlobalTime! with { NativePublications = 0 } },
        }) Reject(() => FalloutActorProcessRuntimeState.Validate(invalid));
        Console.WriteLine("OPENNV_SOURCE_GLOBAL_TIME_CURRENT_JOIN_PASS sameTimer=true mandatory=true missingOwnerRefused=true");
    }

    private static void GlobalTimeFailedNativeBindingRetirement(FalloutGlobalTimeSource source)
    {
        using var timer = new FalloutSourceFrameTimerState(source.Timer,
            new AuthoredSandboxTickCounter { Now = 1 }, Guid.NewGuid());
        using var global = new FalloutGlobalTimeState(source, timer);
        var actualOwner = new object();
        var values = new FalloutGlobalTimeNativeValues(1, 1);
        var retirements = 0;
        var permitRetirement = false;
        var binding = new FalloutGlobalTimeNativeBinding(actualOwner, "authored-failed-native-rate-factory",
            () => values, publication =>
            {
                values = new(publication.CurrentRate, publication.AudioRate);
                throw new InvalidOperationException("authored-entered-rate-publication-failure");
            }, () =>
            {
                retirements++;
                if (!permitRetirement) throw new InvalidOperationException("authored-native-rate-retirement-failure");
                values = new(1, 1);
            });
        try
        {
            _ = global.BindPresentation(binding);
            throw new InvalidOperationException("Expected failed native publication and retirement.");
        }
        catch (AggregateException failure) when (failure.InnerExceptions.Count == 2 &&
            failure.InnerExceptions[0].Message == "authored-entered-rate-publication-failure" &&
            failure.InnerExceptions[1].Message == "authored-native-rate-retirement-failure") { }
        Require(retirements == 1, "Failed global factory did not enter its real retained native cleanup.");
        Reject(global.Dispose);
        Reject(() => global.RetirePresentation(new object()));
        Require(retirements == 1, "Foreign cleanup entered the retained actual native owner.");
        permitRetirement = true;
        global.RetirePresentation(actualOwner);
        global.Dispose();
        Require(retirements == 2 && values == new FalloutGlobalTimeNativeValues(1, 1),
            "A binding whose factory threw before returning a scope lost its actual retryable native cleanup.");
        Console.WriteLine("OPENNV_SOURCE_GLOBAL_TIME_FAILED_FACTORY_RETIRE_PASS genuineOwner=true failedPrefix=true");
    }
}
