using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class RestHostContracts
{
    internal static void Run()
    {
        FullReaderLocation(); TypedPreferenceSelection(); ActualQueueAndWriter(); EarlierWriterFailure();
        MenuOperationOrderAndFault(); SourceWorldTime(); SoundSourceAdmission();
        Console.WriteLine("OPENNV_REST_HOST_CONTRACT_PASS authored=true fullReaderLocation=true preferenceCollections=true " +
            "sharedQueue=true realWriterDigest=true earlierFailureVisible=true cancelBeforeHours=true afterHoursPrefix=true " +
            "elapsedFloat32=true coldNoReplay=true missingSoundSourceRefused=true native=unexecuted originalEffects=unexecuted");
    }

    private sealed class RestFixture
    {
        internal readonly FalloutGlobalState Globals;
        internal readonly FalloutGameTime Clock;
        internal readonly FalloutSleepWait Owner;
        internal bool Sleeping, FailAfterHours;
        internal bool ActionTarget = true, SliderTarget = true;
        internal Action<FalloutRestRequest>? BeforeHours;
        internal Action<float>? WorldSeconds;
        internal readonly List<string> Order = [];
        internal RestFixture(FalloutSleepWaitSnapshot? restore = null,
            FalloutGlobalStateSnapshot? globals = null, FalloutGameTimeSnapshot? clock = null)
        {
            var keys = Enumerable.Range(0, 6).Select(i => new FalloutFormKey("AuthoredRest.esm", (uint)(0x35 + i))).ToArray();
            float[] values = [2280, 3, 4, 12, 600.25f, 30];
            Globals = new(keys.Select((key, i) => new FalloutGlobal(key, "AuthoredRestTime" + i, (byte)'f', values[i], "authored")));
            Clock = new(Globals, new(keys[0], keys[1], keys[2], keys[3], keys[4], keys[5]),
                new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], new('a', 64), true, false));
            Clock.InitializeNewGame();
            if (globals is not null) Globals.Restore(globals);
            if (clock is not null) Clock.Restore(clock);
            Sleeping = restore?.Sleeping ?? false;
            var host = new FalloutSleepWaitHost(
                (_, _) => new(FalloutRestFactState.Satisfied, "authored-independent-rest-producer"),
                request =>
                {
                    Order.Add("before-hours"); BeforeHours?.Invoke(request);
                    Owner!.BeginSourceMenuCounting(target =>
                    { if (target == FalloutRestMenuTarget.Action) ActionTarget = false; else SliderTarget = false; });
                },
                (flag, write) => { Order.Add("write-hours"); write(); Sleeping = flag; },
                _ => { }, seconds => WorldSeconds?.Invoke(seconds), _ => { }, _ => { },
                (_, _) => Order.Add("close"), () => Sleeping)
            {
                AfterMenuPlayerHours = _ => { Order.Add("after-hours"); if (FailAfterHours) throw new IOException("authored-after-hours"); },
                BeforeCancelPlayerHours = _ => Order.Add("before-cancel-hours"),
            };
            Owner = new(Source(), Clock, host, restore);
        }
        internal void Open(FalloutRestKind kind)
        { Owner.Open(new(kind, FalloutRestOrigin.SourceCommand)); Owner.Publish(Owner.RequestOrdinal); }
        internal RestFixture Cold() => new(RoundTrip(Owner.Capture()), RoundTrip(Globals.Capture()), RoundTrip(Clock.Capture()));
    }

    private static FalloutSleepWaitSource Source() => new(
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
        new('1', 64), new('2', 64), FalloutSleepWaitSource.CurrentContractSha256, true, true, 24);
    private static FalloutRestAutoSavePolicy Policy(FalloutSleepWaitSource source, bool enabled = true) =>
        FalloutRestAutoSavePolicy.Read(source, new([
            new("bSaveOnRest:GamePlay", FalloutIniCollection.Prefs, enabled ? 1u : 0u),
            new("bSaveOnWait:GamePlay", FalloutIniCollection.Prefs, enabled ? 1u : 0u)], [], "authored-selected-preferences"));

    private static void FullReaderLocation()
    {
        var folder = Directory.CreateTempSubdirectory("opennv-rest-location-");
        var path = Path.Combine(folder.FullName, "AuthoredRest.esm");
        var bytes = Join(Header(), Record("WRLD", 0x800, FalloutSleepWaitSource.NoRestRecordFlag),
            Group(1, 0x800, Record("CELL", 0x801, 0, Field("DATA", [0]))),
            Record("CELL", 0x802, FalloutSleepWaitSource.NoRestRecordFlag, Field("DATA", [1])),
            Record("CELL", 0x803, 0, Field("DATA", [0])),
            Record("CELL", 0x804, 0), Record("CELL", 0x805, 0, Field("DATA", [1]), Field("DATA", [1])),
            Record("CELL", 0x806, 0, Field("DATA", [])), Record("CELL", 0x807, 0x20, Field("DATA", [1])));
        File.WriteAllBytes(path, bytes); var before = SHA256.HashData(bytes);
        try
        {
            using var records = FalloutPluginStack.Load(folder.FullName, ["AuthoredRest.esm"]);
            FalloutFormKey Key(uint id) => new("AuthoredRest.esm", id);
            var exterior = FalloutRestLocation.Read(records, Key(0x801));
            Require(exterior.Owner == FalloutRestLocationOwner.ExteriorWorldspace && exterior.Worldspace == Key(0x800) &&
                exterior.ProhibitsRest && exterior.Observe().State == FalloutRestFactState.Denied,
                "Rest ignored the actual parent WRLD/header flag.");
            var interior = FalloutRestLocation.Read(records, Key(0x802));
            Require(interior.Owner == FalloutRestLocationOwner.InteriorCell && interior.Worldspace is null && interior.ProhibitsRest,
                "Rest substituted a parent for the interior source CELL.");
            var unparented = FalloutRestLocation.Read(records, Key(0x803));
            Require(unparented.Owner == FalloutRestLocationOwner.ExteriorWithoutWorldspace && !unparented.ProhibitsRest,
                "Rest fabricated a WRLD for a genuinely unparented exterior.");
            Reject(() => (interior with { ProhibitsRest = false }).RequireCurrent(records, interior.Cell));
            Reject(() => (exterior with { WorldspaceSourceSha256 = new('0', 64) }).RequireCurrent(records, exterior.Cell));
            foreach (var id in new uint[] { 0x804, 0x805, 0x806, 0x807 }) Reject(() => FalloutRestLocation.Read(records, Key(id)));
            Require(SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(before), "Rest location changed its source input.");
        }
        finally { Directory.Delete(folder.FullName, true); }
    }

    private static void TypedPreferenceSelection()
    {
        var source = Source(); var policy = Policy(source);
        Require(policy.Enabled(FalloutRestKind.Sleep) && policy.Enabled(FalloutRestKind.Wait), "Selected preference defaults were lost.");
        var disabled = Policy(source, false);
        Require(!disabled.Enabled(FalloutRestKind.Sleep) && disabled.Identity != policy.Identity,
            "Rest autosave policy omitted its actual preference value.");
        Reject(() => FalloutRestAutoSavePolicy.Read(source, new([
            new("bSaveOnRest:GamePlay", FalloutIniCollection.Main, 1),
            new("bSaveOnWait:GamePlay", FalloutIniCollection.Main, 1)], [], "authored-other-collection")));
        Reject(() => (policy with { RestSourceSha256 = new('0', 64) }).RequireSource(source));
    }

    private static void ActualQueueAndWriter()
    {
        using var campaign = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once()); campaign.Bind(campaign.Write);
        var rest = new RestFixture(); var auto = new FalloutRestAutoSave(campaign.Records, rest.Owner, Policy(rest.Owner.Source),
            campaign.Owner, () => campaign.Site(1));
        rest.BeforeHours = auto.RequestBeforeCountdown;
        rest.Open(FalloutRestKind.Wait); rest.Owner.Select(2); rest.Owner.Begin();
        var queued = campaign.Owner.Order.Requests.Single();
        Require(queued.Origin == RuntimeSaveRequestOrigin.NativeRestStart && queued.Native?.Rest?.RequestOrdinal == rest.Owner.RequestOrdinal &&
            queued.Destination == RuntimeSaveRequestDestination.Continue && rest.Owner.RemainingHours == 2 &&
            auto.ObserveCompletion().State == FalloutAdvancementActivityState.Held,
            "Rest start skipped its genuine distinct pre-countdown native queue request.");
        Require(!campaign.Owner.Drain(() => throw new InvalidDataException("Same-phase writer was admitted.")),
            "Rest request authorized a writer in its own phase.");
        campaign.AdvancePhase(); Require(campaign.Owner.Drain(() => null), "Actual later head writer did not write rest autosave.");
        var written = campaign.Owner.Order.Requests.Single();
        Require(written.Disposition == RuntimeSaveRequestDisposition.Completed && written.CommittedSha256 ==
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(campaign.Binding.ContinuePath))).ToLowerInvariant() &&
            auto.ObserveCompletion().State == FalloutAdvancementActivityState.Satisfied,
            "Rest completion did not retain the actual written destination bytes/digest.");
        using var coldCampaign = campaign.Restore(campaign.Binding.ContinuePath);
        var coldRest = rest.Cold(); var reads = 0;
        var coldAuto = new FalloutRestAutoSave(campaign.Records, coldRest.Owner, Policy(coldRest.Owner.Source), coldCampaign.Owner,
            () => { reads++; throw new InvalidDataException("Cold native request replayed."); }, RoundTrip(auto.Capture()));
        Require(reads == 0 && coldAuto.ObserveCompletion().State == FalloutAdvancementActivityState.Satisfied &&
            coldCampaign.Owner.Order.Requests.Single().Request == written.Request, "Cold rest replayed its queue request or fabricated writer completion.");
        Reject(() => new FalloutRestAutoSave(campaign.Records, coldRest.Owner, Policy(coldRest.Owner.Source), coldCampaign.Owner,
            () => campaign.Site(2), auto.Capture() with { SaveOrder = null, Failure = null }));
        campaign.RequireSourceUnchanged();
    }

    private static void EarlierWriterFailure()
    {
        using var campaign = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once());
        campaign.Bind(_ => throw new IOException("authored-earlier-head"));
        Require(campaign.Activate().Error is null, "Earlier genuine compiled requester failed before its writer.");
        var rest = new RestFixture(); var auto = new FalloutRestAutoSave(campaign.Records, rest.Owner, Policy(rest.Owner.Source),
            campaign.Owner, () => campaign.Site(1));
        rest.BeforeHours = auto.RequestBeforeCountdown; rest.Open(FalloutRestKind.Sleep); rest.Owner.Begin();
        Require(campaign.Owner.Order.Requests.Count == 2, "Rest coalesced its native request with an earlier source request.");
        campaign.AdvancePhase(); Require(!campaign.Owner.Drain(() => null), "Failed earlier writer was treated as successful.");
        Require(auto.ObserveCompletion().State == FalloutAdvancementActivityState.Unowned &&
            campaign.Owner.Order.Requests[^1].Disposition == RuntimeSaveRequestDisposition.Pending,
            "Rest hid an earlier writer failure as ordinary pending/completed work.");
        campaign.RequireSourceUnchanged();
    }

    private static void MenuOperationOrderAndFault()
    {
        var rest = new RestFixture(); rest.Open(FalloutRestKind.Sleep); rest.Owner.Begin();
        Require(rest.Order.SequenceEqual(new[] { "before-hours", "write-hours", "after-hours" }) && rest.Sleeping,
            "Start source effects did not surround the actual player-hour write in order.");
        rest.Order.Clear(); rest.Owner.Cancel();
        Require(rest.Order.SequenceEqual(new[] { "before-cancel-hours", "write-hours", "close" }) && !rest.Sleeping,
            "Cancel cue occurred after the source counter/flag write.");
        var failed = new RestFixture { FailAfterHours = true }; failed.Open(FalloutRestKind.Sleep); failed.Owner.Select(3);
        Reject(failed.Owner.Begin); var prefix = failed.Owner.Capture();
        Require(prefix.Failure?.Step == FalloutRestStep.MenuAfterPlayerHours && prefix.RemainingHours == 3 && prefix.Sleeping &&
            prefix.Phase == FalloutRestPhase.Choosing, "After-hour IOException lost its actual hour/flag prefix.");
        var cold = failed.Cold(); Reject(cold.Owner.Begin);
        Require(cold.Order.Count == 0 && cold.Sleeping && cold.Owner.RemainingHours == 3, "Cold replayed a failed menu-start effect.");
    }

    private static void SourceWorldTime()
    {
        var rest = new RestFixture(); var declaration = FalloutRestWorldTimeSource.Read(rest.Owner.Source);
        var frameState = FalloutAdvancementActivityState.Held;
        var elapsed = new FalloutRestWorldTime(rest.Owner.Source, () => new(frameState, "authored-current-frame"),
            new(FalloutRestWorldTime.Schema, declaration.Identity, 0, 0, null));
        Reject(() => elapsed.AdvanceActualSourceFrame(1, .375f)); Require(elapsed.Value == 0, "Held source frame advanced world time.");
        frameState = FalloutAdvancementActivityState.Satisfied; elapsed.AdvanceActualSourceFrame(1, .375f);
        Reject(() => elapsed.AdvanceActualSourceFrame(1, 5)); Require(elapsed.Value == .375f, "Duplicate source frame changed world time.");
        rest.WorldSeconds = seconds => elapsed.AdvanceRestHour(rest.Owner, seconds);
        rest.Owner.SetScriptHours(2); rest.Owner.AdvancePlayerUpdate(1);
        Require(elapsed.Value == 120.375f && elapsed.Last is { Origin: FalloutRestWorldTimeOrigin.RestHour, RestHour: 1 } &&
            rest.Clock.Hour == 13, "Rest elapsed/calendar order lost its fractional source value.");
        var coldRest = rest.Cold(); var cold = new FalloutRestWorldTime(coldRest.Owner.Source,
            () => new(FalloutAdvancementActivityState.Satisfied, "authored-new-frame-epoch"), RoundTrip(elapsed.Capture()));
        coldRest.WorldSeconds = seconds => cold.AdvanceRestHour(coldRest.Owner, seconds);
        coldRest.Owner.AdvancePlayerUpdate(1); rest.Owner.AdvancePlayerUpdate(2);
        Require(cold.Value == elapsed.Value && cold.Value == 240.375f, "Cold elapsed time replayed or lost a rest-hour suffix.");
        Reject(() => cold.AdvanceRestHour(coldRest.Owner, 120));
        elapsed.AdvanceActualSourceFrame(2, 100000 - elapsed.Value);
        Require(elapsed.Value == 100000, "Source elapsed time reset at equality rather than above its limit.");
        elapsed.AdvanceActualSourceFrame(3, .125f); Require(elapsed.Value == 0, "Source elapsed time did not reset above its limit.");
        Reject(() => new FalloutRestWorldTime(rest.Owner.Source,
            () => new(FalloutAdvancementActivityState.Satisfied, "authored-frame"), elapsed.Capture() with { ValueBits = 1 }));
    }

    private static void SoundSourceAdmission()
    {
        var folder = Directory.CreateTempSubdirectory("opennv-rest-cues-"); var path = Path.Combine(folder.FullName, "AuthoredRest.esm");
        var bytes = Join(Header(), Sound(0x850, "UIMenuOK"), Sound(0x851, "UIMenuCancel")); File.WriteAllBytes(path, bytes);
        try
        {
            using var records = FalloutPluginStack.Load(folder.FullName, ["AuthoredRest.esm"]);
            var rest = new RestFixture(); rest.Open(FalloutRestKind.Wait);
            // Familiar SOUN names are not an executable cue catalogue. This
            // authored record fixture has neither an owned engine nor an
            // actual indexed caller/native voice; it must not manufacture them.
            foreach (var id in new uint[] { 0x850, 0x851 })
                _ = FalloutSoundRecordReader.Read(records, new("AuthoredRest.esm", id));
            MissingSource(() => new FalloutRestInterfaceSounds(records, rest.Owner));
            MissingSource(() => new FalloutRestInterfaceSounds(records, rest.Cold().Owner));
            var retained = new FalloutInterfaceSoundCatalogue(rest.Owner.Source.EngineSha256, 3,
                [new(-1, FalloutInterfaceSoundDisposition.SourceSilent, null, 0),
                 new(0, FalloutInterfaceSoundDisposition.SourceSilent, null, 0),
                 new(1, FalloutInterfaceSoundDisposition.NamedSound, "UIMenuOK", 0x121),
                 new(2, FalloutInterfaceSoundDisposition.NamedSound, "UIMenuCancel", 0x121)],
                FalloutInterfaceSoundDisposition.SourceSilent);
            retained.Validate();
            MissingSource(() => FalloutRestInterfaceSoundSource.Read(records, rest.Owner.Source, retained));
            Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Rest cue parsing changed its source bytes.");

            static void MissingSource(Action action)
            {
                try { action(); }
                catch (NotSupportedException error) when (error.Message == "Rest cues have no actual selected executable source.") { return; }
                throw new InvalidDataException("Authored SOUN records admitted rest audio without an actual engine/indexed caller.");
            }
        }
        finally { Directory.Delete(folder.FullName, true); }
    }

    private static byte[] Sound(uint id, string name)
    {
        var data = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)FalloutSoundFlags.MenuSound);
        for (var i = 0; i < 5; ++i) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + i * 2), 100);
        return Record("SOUN", id, 0, Field("EDID", Encoding.ASCII.GetBytes(name + '\0')),
            Field("FNAM", Encoding.ASCII.GetBytes("authored.wav\0")), Field("SNDD", data));
    }
    private static byte[] Header() => Record("TES4", 0, 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8])));
    private static byte[] Record(string type, uint id, uint flags, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id); return Join(header, data);
    }
    private static byte[] Group(int type, uint label, byte[] data)
    {
        var header = new byte[24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length + 24); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), type); return Join(header, data);
    }
    private static byte[] Field(string type, byte[] bytes) => Join(Encoding.ASCII.GetBytes(type), BitConverter.GetBytes(checked((ushort)bytes.Length)), bytes);
    private static byte[] Join(params byte[][] values) => values.SelectMany(bytes => bytes).ToArray();
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Rest host accepted an invalid source/callback/persistence operation.");
    }
}
