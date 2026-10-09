using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class RestWorldConsumerContracts
{
    private const string WideEngine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string SingleEngine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    private sealed class NativeReceiptFixture
    {
        internal readonly Dictionary<int, FalloutInterfaceFadeChannel> Views = [];
        internal int Published, Written, Retired;
        internal bool FailAfterPublish, FailAfterWrite, FailAfterRetire;
        internal FalloutInterfaceFadeNativeHost Host => new(channel =>
        {
            if (!Views.TryAdd(channel.Channel, channel)) throw new InvalidOperationException("authored native duplicate");
            ++Published;
            if (FailAfterPublish) throw new IOException("authored committed native allocation");
        }, channel =>
        {
            if (!Views.TryGetValue(channel.Channel, out var existing) || existing.Generation != channel.Generation)
                throw new InvalidOperationException("authored original native generation missing");
            Views[channel.Channel] = channel; ++Written;
            if (FailAfterWrite) throw new IOException("authored committed material write");
        }, channel =>
        {
            if (!Views.Remove(channel.Channel)) throw new InvalidOperationException("authored original native retirement missing");
            ++Retired;
            if (FailAfterRetire) throw new IOException("authored committed native retirement");
        });
    }
    internal static void Run()
    {
        foreach (var engine in new[] { WideEngine, SingleEngine })
        {
            ColdFractionalClock(engine); SharedReleaseHold(engine); FaultPrefixes(engine); ConsolePredicate(engine);
        }
        CatalogAdmission();
        Console.WriteLine("OPENNV_REST_INTERFACE_CONSUMERS_PASS sourceCatalog=true threeChannels=true sharedHold=true " +
            "fractionalCold=true noStartReplay=true nextUpdateRetirement=true callbackPrefix=true " +
            "actualConsoleCounterContract=true unavailableProducersRefused=true originalNativeExecution=unverified nativePixels=unverified");
    }
    private static FalloutInterfaceFadeSource Source(string engine)
    {
        string[] paths = ["textures/interface/faders/authored-cover.dds", "textures/interface/faders/authored-cover.dds",
            "textures/interface/faders/authored-reveal.dds"];
        var declaration = new FalloutInterfaceFadeDeclaration(engine, engine == WideEngine ?
            FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32 : FalloutInterfaceFadeArithmetic.Float32EachOperation,
            Enumerable.Range(0, 3).Select(channel => new FalloutInterfaceFadeCatalogRow(channel,
                channel == 0 ? FalloutInterfaceFadeRoot.Primary : FalloutInterfaceFadeRoot.Secondary, paths[channel])).ToArray());
        return new(declaration, new('a', 64), Enumerable.Range(0, 3).Select(channel =>
            new FalloutInterfaceFadeTexture(channel, paths[channel], new(channel == 2 ? 'c' : 'b', 64))).ToArray(),
            FalloutInterfaceFadeSource.CurrentContractSha256);
    }
    private static FalloutInterfaceFadeClock Frame(ulong frame, float delta, float multiplier = 1) =>
        new(frame, delta, multiplier, "authored-living-ui-timer-and-global-rate");
    private static void ColdFractionalClock(string engine)
    {
        var source = Source(engine); var warmViews = new NativeReceiptFixture(); var warm = new FalloutInterfaceFade(source);
        warm.BindNative(warmViews.Host); warm.Start(0, .5f, false); warm.Advance(Frame(1, .25f, 2)); warm.Advance(Frame(2, .25f, 2));
        Check(warm.Channels[0].Opacity == .5f && warmViews.Published == 1 && warm.ReleaseHold == 0,
            "Actual normalized UI time did not produce the authored fractional source opacity.");
        var snapshot = RoundTrip(warm.Capture()); var coldViews = new NativeReceiptFixture();
        var cold = new FalloutInterfaceFade(source, snapshot); cold.BindNative(coldViews.Host);
        Check(coldViews.Published == 1 && cold.Channels[0].Generation == 1 && cold.Channels[0].Opacity == .5f && cold.ReleaseHold == 0,
            "Cold publication replayed Start or reset the source opacity/hold.");
        warm.Advance(Frame(3, .25f, 2)); cold.Advance(Frame(0, .25f, 2));
        Check(warm.Channels[0] == cold.Channels[0] && cold.Channels[0].Opacity == .75f,
            "Warm/cold normalized fractional source clocks diverged.");
        cold.End(0, false, () => new(false, "authored-actual-force-retirement-state"));
        Check(cold.Channels[0].Opacity == .75f, "Normal release changed a partly opaque source value.");
        cold.Advance(Frame(1, .375f));
        Check(cold.Channels[0].Opacity == 0 && coldViews.Views.ContainsKey(0) && coldViews.Retired == 0,
            "Fade geometry retired in the update that first reached zero.");
        cold.Advance(Frame(2, 0)); Check(coldViews.Retired == 1 && !coldViews.Views.ContainsKey(0),
            "The genuine next UI update did not retire zero-opacity geometry.");
        var drift = source with { Textures = source.Textures.Select(texture => texture.Channel == 0 ?
            texture with { BytesSha256 = new('d', 64) } : texture).ToArray() };
        Reject(() => _ = new FalloutInterfaceFade(drift, snapshot));
        Reject(() => _ = new FalloutInterfaceFade(source, snapshot with { ReleaseHold = 4 }));
        var missing = new FalloutInterfaceFade(source); Reject(() => missing.Start(0, 1, false));
        Check(missing.Failure is not null && missing.Channels[0].Generation == 1,
            "Missing native publication became a source success or erased the attempted start.");
    }
    private static void SharedReleaseHold(string engine)
    {
        var views = new NativeReceiptFixture(); var owner = new FalloutInterfaceFade(Source(engine)); owner.BindNative(views.Host);
        owner.Start(1, 1, true); owner.Start(2, 1, true); owner.Advance(Frame(0, .25f));
        Check(owner.ReleaseHold == 1 && owner.Channels[1].Opacity == 1 && owner.Channels[2].Opacity == 1,
            "Source channels did not share the ordered release hold.");
        owner.Advance(Frame(1, .25f));
        Check(owner.ReleaseHold == 0 && owner.Channels[1].Opacity == 1 && owner.Channels[2].Opacity == .75f,
            "The last shared hold update was replaced by independent per-channel timers.");
        owner.Start(0, .25f, false); owner.Advance(Frame(2, .25f));
        var before = owner.Channels[0]; Check(before.Opacity == 1, "Opaque source clamp was absent.");
        Check(!owner.Start(0, 8, false) && owner.Channels[0] == before, "An increasing source channel restarted unexpectedly.");
        owner.End(0, false, () => new(false, "authored-actual-force-retirement-state"));
        Check(BitConverter.SingleToInt32Bits(owner.Channels[0].Opacity) == 0x3f7ff972,
            "Normal opaque release lost the selected source epsilon.");
        owner.Start(0, 1, false);
        Check(owner.Channels[0].Generation == 2 && owner.Channels[0].Opacity == 0 && views.Retired == 1,
            "Downward replacement did not retire the original geometry before creating its successor.");
        Reject(() => owner.Advance(Frame(2, .1f)));
        Check(owner.Failure is { Operation: FalloutInterfaceFadeOperation.Frame }, "A repeated actual UI frame was silently consumed.");
    }
    private static void FaultPrefixes(string engine)
    {
        var source = Source(engine); var views = new NativeReceiptFixture { FailAfterPublish = true };
        var failed = new FalloutInterfaceFade(source); failed.BindNative(views.Host);
        Reject(() => failed.Start(0, .5f, false)); var prefix = RoundTrip(failed.Capture());
        Check(prefix.Failure is not null && prefix.Channels[0].Generation == 1 && views.Published == 1,
            "Ordinary I/O after native allocation erased its actual attempted source prefix.");
        var coldViews = new NativeReceiptFixture(); var cold = new FalloutInterfaceFade(source, prefix); cold.BindNative(coldViews.Host);
        Reject(() => cold.Start(0, .5f, false));
        Check(coldViews.Published == 0, "Cold loading retried a failed native/start operation.");
        views = new(); var owner = new FalloutInterfaceFade(source); owner.BindNative(views.Host); owner.Start(0, .5f, false);
        views.FailAfterWrite = true; Reject(() => owner.Advance(Frame(1, .125f)));
        Check(owner.Channels[0].Opacity == .25f && views.Views[0].Opacity == .25f && owner.Capture().Failure is not null,
            "Committed source/material opacity disappeared after an ordinary callback fault.");
        var writes = views.Written; Reject(() => owner.Advance(Frame(2, .125f)));
        Check(views.Written == writes, "A failed source material callback replayed.");
        views = new(); owner = new(source); owner.BindNative(views.Host); owner.Start(0, 1, false);
        views.FailAfterRetire = true; Reject(() => owner.End(0, true, () => throw new InvalidOperationException("must short circuit")));
        Check(views.Retired == 1 && owner.Failure is not null && owner.Channels[0].Generation == 1,
            "A failed actual native retirement became completion.");
        Reject(() => owner.End(0, true, () => new(false, "authored")));
        Check(views.Retired == 1, "Source retried a retained retirement fault.");
    }
    private static void ConsolePredicate(string engine)
    {
        var source = new FalloutConsoleActivitySource(engine, new('a', 64), FalloutConsoleActivitySource.CurrentContractSha256);
        var activity = new FalloutConsoleActivity(source);
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Unowned,
            "Callback absence was treated as closed console/source readiness.");
        var current = new FalloutConsoleActivitySample(source.Identity, "authored-actual-ui-and-console-factory",
            FalloutConsolePresence.Present, true, FalloutConsolePresence.Unowned, null);
        activity.Bind(() => current);
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Unowned, "Unknown source console became absent.");
        current = current with { Console = FalloutConsolePresence.Present, OpenCounter = 1 };
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Denied, "Open source counter did not hold rest closure.");
        current = current with { OpenCounter = -1 };
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Satisfied, "Signed negative source counter was treated as an unsigned/job count.");
        current = current with { Console = FalloutConsolePresence.Unowned, OpenCounter = null, InterfaceEnabled = false };
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Satisfied, "Original disabled-interface short circuit was lost.");
        activity.Retire(); Check(activity.ObserveRestClose().State == FalloutRestFactState.Unowned,
            "Retired native console lifetime defaulted to source readiness.");
        activity = new(source); var reads = 0;
        activity.Bind(() => { ++reads; throw new IOException("authored source controller fault"); });
        Check(activity.ObserveRestClose().State == FalloutRestFactState.Unowned && activity.Failure is not null,
            "Source console ordinary I/O failure was not retained.");
        _ = activity.ObserveRestClose(); Check(reads == 1, "A failed console observation callback replayed.");
    }
    private static void CatalogAdmission()
    {
        var code = new List<byte>(); var calls = new List<int>();
        void Word(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); code.AddRange(bytes); }
        for (var channel = 0; channel < 3; ++channel)
        {
            var target = 0x2000u + (uint)channel * 28;
            code.AddRange([0xc6, 0x05]); Word(target); code.Add(channel == 0 ? (byte)1 : (byte)0);
            code.AddRange([0xc7, 0x05]); Word(target + 4); Word(channel == 2 ? 0x6000u : 0x5000u);
            code.AddRange([0x33, 0xc0]);
            for (uint offset = 8; offset <= 20; offset += 4) { code.Add(0xa3); Word(target + offset); }
            code.AddRange([0x6a, 0, 0xb9]); Word(target + 24); code.Add(0xe8); calls.Add(code.Count); Word(0);
        }
        while (code.Count < 0x200) code.Add(0xcc); code.Add(0xc3);
        var bytes = code.ToArray();
        foreach (var at in calls) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), 0x200 - at - 4);
        string? Literal(uint address) => address switch { 0x5000 => "AuthoredCover.dds", 0x6000 => "AuthoredReveal.dds", _ => null };
        bool Writable(uint address, int count) => address is >= 0x2000 and < 0x2054 && count == 28;
        var rows = FalloutExecutableStringTable.ReadConstructedFadeCatalog(bytes, Literal, Writable, 0x1000);
        Check(rows.Count == 3 && rows[0].Root == FalloutInterfaceFadeRoot.Primary && rows[1].TexturePath == rows[0].TexturePath &&
            rows[2].TexturePath.EndsWith("authoredreveal.dds", StringComparison.Ordinal),
            "Authored complete physical constructors lost their source catalog association.");
        var truncated = bytes[..(calls[2] - 1)]; Reject(() => FalloutExecutableStringTable.ReadConstructedFadeCatalog(truncated, Literal, Writable, 0x1000));
        var invalid = bytes.ToArray(); invalid[6] = 2; Reject(() => FalloutExecutableStringTable.ReadConstructedFadeCatalog(invalid, Literal, Writable, 0x1000));
        Reject(() => FalloutExecutableStringTable.ReadConstructedFadeCatalog(bytes, _ => "../outside.dds", Writable, 0x1000));
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException) { return; }
        throw new InvalidOperationException("An authored unsupported/faulted rest consumer was admitted.");
    }
}
