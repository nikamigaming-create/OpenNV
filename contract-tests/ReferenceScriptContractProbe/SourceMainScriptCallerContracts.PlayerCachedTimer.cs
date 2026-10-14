using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerCachedTimerReadContracts()
    {
        using var fixture = new StandaloneFixture();
        var engine = fixture.Host.Source.EngineSha256;
        var source = FalloutSourceFrameTimer.Read(new(engine, new('a', 64), new('b', 64), new('c', 64),
            new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
        var configuration = new FalloutSourceFrameTimerConfiguration(source, "authored-source-timer-INI",
            new("bChangeTimeMultSlowly:General", FalloutIniCollection.Main, 1), true, "authored-current-timer-setting");
        fixture.Owner.ConfigureSourceCachedTimer(configuration);
        var before = fixture.Owner.CaptureSourceCachedTimer();
        FalloutMainPlayerCellInvocation? entered = null;
        fixture.BindPlayerCachedTimerRead(invocation =>
        {
            entered = invocation;
            Require(BitConverter.SingleToUInt32Bits(fixture.Owner.ReadStandalonePlayerScaledSeconds(invocation)) == 0,
                "First Player read required a later renderer publication or substituted delivered host time.");
            var after = fixture.Owner.CaptureSourceCachedTimer();
            Require(after.Sequence == before.Sequence && after.Updated == 0 && after.ScaledSeconds == 0,
                "Reading the source timer advanced its original tail writer.");
            throw new NotSupportedException("authored-later-Player-child-unbound");
        });
        using var binding = fixture.Bind();
        Exception? failure = null;
        try { fixture.Owner.ExecuteMainScriptCaller(1, .25f).GetAwaiter().GetResult(); }
        catch (Exception error) { failure = error; }
        Require(failure is NotSupportedException { Message: "authored-later-Player-child-unbound" } && entered is not null,
            "Actual Player/Main scope did not reach the independent constructor-zero timer read.");
        Reject(() => fixture.Owner.ReadStandalonePlayerScaledSeconds(entered!));
        Require(fixture.Owner.CaptureSourceCachedTimer().Sequence == before.Sequence,
            "An expired actual Player token changed the source timer.");
        Console.WriteLine("OPENNV_PLAYER_CACHED_TIMER_READ_PASS actualMain=true constructorZero=true noRendererReceipt=true noHostDelta=true expiredTokenRefused=true gameplay=UNEXECUTED");
    }
}
