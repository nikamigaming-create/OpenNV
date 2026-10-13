using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourceImageMenuContracts()
    {
        MenuImageDeclaration(); MenuImageSharedLifetimeAndCold(); MenuImageGateAndFailure(); MenuImageStartBoundary(); MenuImageOrderedFormRemoval();
        Console.WriteLine("OPENNV_SOURCE_IMAGE_MENU_PASS authored=true sharedList=true clockPreserved=true " +
            "formWideRemoval=true immutableFields=true constructorAndSentinel=true gateUnknownRetained=true " +
            "failurePrefix=true coldNoReplay=true graphics=UNEXECUTED retailParity=UNMEASURED");
    }

    private static FalloutSourceImageSpaceMenuDeclaration ImageMenuDeclaration() =>
        new(ImageBits(1), ImageBits(-10001), ImageBits(-10000), 0x162, new('e', 64));

    private static (FalloutImageSpaceState State, FalloutHeadlessImageSpaceOutput Port,
        List<SourceImageNativeFields> Publications) BindHeadlessImages(StandaloneFixture fixture,
        FalloutSourceImageSpaceMainSnapshot? saved = null)
    {
        var state = new FalloutImageSpaceState();
        state.ConfigureSourceMainImages(fixture.Host.Source, fixture.Owner.ReadMainNativeDeliveryBindings().Process,
            ImageDefaults(), ImageDof(), saved);
        state.ConfigureSourceImageMenu(ImageMenuDeclaration());
        var publications = new List<SourceImageNativeFields>();
        FalloutHeadlessImageSpaceOutput? port = null;
        port = new(state.SourceMainImageIdentity, state.SourceMainImageProcess, ImageDof(),
            _ => publications.Add(port!.Fields!));
        state.BindSourceMainImageNative(port);
        fixture.Host.DuringContexts = (invocation, advance) => state.ExecuteSourceImageMain(invocation, advance, .25f,
            fixture.Owner.ReadSourceImageContextClock(invocation), () => fixture.Owner.ReadSourceImageContextClock(invocation));
        return (state, port, publications);
    }

    private static void MenuImageSharedLifetimeAndCold()
    {
        using var warm = new StandaloneFixture(); var (images, port, publications) = BindHeadlessImages(warm);
        using var outputLifetime = port;
        using var clocks = warm.Owner.BindSourceMainImages(images);
        Require(port.Fields is null && publications.Count == 0, "Headless construction invented a historical output publication.");
        using (var telemetry = JsonDocument.Parse(JsonSerializer.Serialize(port.State)))
            Require(telemetry.RootElement.GetProperty("cpuAllocations").GetInt32() == 5 &&
                !telemetry.RootElement.GetProperty("nativeResources").GetBoolean() &&
                telemetry.RootElement.GetProperty("captureCount").GetInt32() == 0 &&
                telemetry.RootElement.GetProperty("drawCount").GetInt32() == 0,
                "Headless receiver constructor invented render resources or omitted its real C# allocations.");
        var menu = ImageProgram();
        var other = menu with { Form = new("Authored.esm", 0x506), SourceSha256 = new('d', 64),
            Multiply = menu.Multiply.Select((curve, index) => index == 0 ? new FalloutImageFloatKey[] { new(0, .8f), new(1, .8f) } : curve).ToArray() };
        images.Apply(menu); images.Apply(menu); images.Apply(other);
        var initial = images.CaptureSourceMainImages();
        Require(initial.Instances.All(row => row.Start == ImageBits(-10001) && row.Enabled == 0 && row.Weight == ImageBits(1)),
            "The real form factory lost its source constructor start, enabled byte or strength.");
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
        var before = images.CaptureSourceMainImages();
        Reject(() => port.PublishEffects(before.Output! with { Process = Guid.NewGuid() }));
        Reject(() => port.PublishProfile(before.Output! with { Blur = ImageBits(6) }));
        var clock = warm.Owner.ReadSourceImageMenuClock(images);
        SourceImageNativeFields? retained = null;
        images.CaptureSourceImageMenu(menu, new("Authored.esm", 0x162), () => warm.Owner.ReadSourceImageMenuClock(images), output =>
        {
            retained = port.Fields;
            Require(ReferenceEquals(retained!.Output, output) && publications.Count == 2,
                "Capture did not follow the real source effect/profile publication.");
            Reject(() => images.CaptureSourceImageMenu(menu, null, () => clock, _ => { }));
        });
        var saved = images.CaptureSourceMainImages();
        var call = saved.Menu!;
        Require(call.Returned && call.AuxiliaryEnabled is null && call.Clock == clock &&
            call.Steps.SequenceEqual(new[] { "auxiliary-form-remove", "form-factory", "shared-capture-update", "capture-output", "menu-form-remove", "shared-restore-update" }) &&
            saved.Last is { Caller: FalloutSourceImageSpaceCaller.MenuRestore, Advance: false, Returned: true } &&
            saved.Last.ClockBefore == clock && saved.Last.ClockAfter == clock && saved.Last.Delta == 0 &&
            saved.List.Count == 1 && saved.Instances.Single(row => row.Identity == saved.List[0]).Form == other.Form &&
            saved.Instances.Where(row => row.Form == menu.Form).All(row => row.Retired is not null) &&
            saved.Output!.Profile.Words[0] == ImageBits(.8f) && before.List.Count == 3 && publications.Count == 3,
            "Menu held a separate list, advanced time, removed only its temporary instance or restored a stale copied output.");
        Require(call.Captured!.Profile.Words[0] == ImageBits(.1f) && retained!.Output.Caller == FalloutSourceImageSpaceCaller.MenuCapture &&
            retained.Output.Profile.Words[0] == ImageBits(.1f) && retained.Globals.Length == 5 &&
            !ReferenceEquals(retained, port.Fields) && retained.Writes.Take(2).Select(write => write.Field).SequenceEqual(new[] { "double-vision", "blur" }) &&
            retained.Depth.Focus is null && retained.Depth.Parameters.Length == 12 && retained.Options.Count == 3,
            "Shared restoration mutated the retained capture fields or manufactured unknown source DOF cells.");
        Require(warm.Owner.ReadSourceImageMenuClock(images) == clock, "The two menu updates changed the actual process clock.");
        var roundTrip = JsonSerializer.Deserialize<FalloutSourceImageSpaceMainSnapshot>(JsonSerializer.Serialize(saved))!;
        FalloutImageSpaceState.ValidateSourceMainImages(roundTrip);
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Menu = call with { Steps = call.Steps.Reverse().ToArray() } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Last = saved.Last! with { Advance = true } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Last = saved.Last! with { ClockAfter = ImageBits(.5f) } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with
        { Last = saved.Last! with { Delta = ImageBits(.1f) }, Output = saved.Output! with { Delta = ImageBits(.1f) } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Menu = call with { Clock = ImageBits(.5f) } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with
        { Menu = call with { Restored = call.Restored! with { Invocation = Guid.NewGuid() } } }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with
        { Menu = call with { Restored = call.Restored! with { Blur = ImageBits(7) } } }));
        using var cold = new StandaloneFixture(warm.Owner.Capture(), warm.Fade.Capture());
        var (restored, coldPort, coldPublications) = BindHeadlessImages(cold, roundTrip); using var coldLifetime = coldPort;
        restored.RebindSourceImagePrograms(_ => other);
        Require(coldPublications.Count == 0 && coldPort.Fields is null && restored.SourceImageMenuLast!.AuxiliaryEnabled is null &&
            restored.SourceImageMenuLast.Identity == call.Identity && coldPort.NativeIdentity != port.NativeIdentity,
            "Cold menu construction replayed capture/restoration, promoted a historical output factory or dropped the unknown gate.");
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
        Require(coldPublications.Count == 1 && restored.CaptureSourceMainImages().List.Count == 1,
            "Cold normal Main did not consume the real surviving shared list.");
        images.RetireSourceMainImageNative(port); restored.RetireSourceMainImageNative(coldPort);
    }

    private static void MenuImageGateAndFailure()
    {
        var auxiliary = ImageProgram() with { Form = new("Authored.esm", 0x162), SourceSha256 = new('f', 64) };
        foreach (var enabled in new bool?[] { false, true, null })
        {
            using var fixture = new StandaloneFixture(); var (images, port, publications) = BindHeadlessImages(fixture); using var lifetime = port;
            images.Apply(auxiliary); var before = images.CaptureSourceMainImages();
            if (enabled is null)
            {
                Reject(() => images.CaptureSourceImageMenu(ImageProgram(), auxiliary.Form, () => 0, _ => { }, enabled));
                var failed = images.CaptureSourceMainImages();
                Require(failed.List.SequenceEqual(before.List) && failed.Changed == before.Changed && publications.Count == 0 &&
                    failed.Menu is { Entered: "auxiliary-form-remove", Failure: not null, Returned: false, AuxiliaryEnabled: null } &&
                    failed.Menu.Steps.Count == 0 && images.SourceMainImageSaveBlocker is not null,
                    "Unknown auxiliary gate inferred a branch or mutated a live auxiliary instance.");
            }
            else
            {
                images.CaptureSourceImageMenu(ImageProgram(), auxiliary.Form, () => 0, _ => { }, enabled);
                Require(images.CaptureSourceMainImages().List.Count == (enabled.Value ? 0 : 1),
                    "Actual false auxiliary gate removed a script-created instance or true gate skipped form removal.");
            }
            images.RetireSourceMainImageNative(port);
        }
        using var warm = new StandaloneFixture(); var (state, output, writes) = BindHeadlessImages(warm); using var outputLifetime = output;
        Reject(() => state.CaptureSourceImageMenu(ImageProgram(), null, () => 0, _ => throw new IOException("Authored capture consumer fails.")));
        var saved = state.CaptureSourceMainImages();
        Require(saved.Menu is { Entered: "capture-output", Failure: not null, Returned: false, Restored: null } &&
            saved.Menu.Steps.Count == 3 && saved.List.Count == 1 && writes.Count == 1 && saved.Last!.Caller == FalloutSourceImageSpaceCaller.MenuCapture,
            "A failed capture erased its applied prefix or replayed removal/restoration as invented cleanup.");
        using var cold = new StandaloneFixture(); var (restored, nextPort, nextWrites) = BindHeadlessImages(cold, saved); using var nextLifetime = nextPort;
        Require(restored.SourceMainImageSaveBlocker is not null && nextWrites.Count == 0 && restored.SourceImageMenuLast!.Identity == saved.Menu!.Identity,
            "Cold menu failure was erased or replayed.");
        Reject(() => restored.CaptureSourceImageMenu(ImageProgram(), null, () => 0, _ => { }));
        Require(nextWrites.Count == 0, "Cold failed menu reached the current output adapter.");
        state.RetireSourceMainImageNative(output); restored.RetireSourceMainImageNative(nextPort);
        output.Dispose(); Reject(output.RequireCurrentFactory);
        using var retired = JsonDocument.Parse(JsonSerializer.Serialize(output.State));
        Require(retired.RootElement.GetProperty("cpuAllocations").GetInt32() == 0,
            "Headless receiver retirement leaked its actual source parameter allocations.");
    }

    private static void MenuImageStartBoundary()
    {
        foreach (var start in new[] { MathF.BitDecrement(-10000), -10000f, MathF.BitIncrement(-10000), -9999f, float.NaN })
        {
            using var warm = new StandaloneFixture(); var (images, native) = BindImageFixture(warm); images.Apply(ImageProgram());
            var saved = images.CaptureSourceMainImages();
            saved = saved with { Instances = saved.Instances.Select(row => row with { Start = ImageBits(start) }).ToArray() };
            using var cold = new StandaloneFixture(); var (restored, coldNative) = BindImageFixture(cold, saved);
            restored.RebindSourceImagePrograms(_ => ImageProgram());
            using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
            var actual = restored.CaptureSourceMainImages().Instances.Single().Start;
            Require(actual == (start < -10000 ? ImageBits(.25f) : ImageBits(start)),
                "Instance start initialization used the old -9999 threshold, inclusive comparison or a NaN replacement.");
            Require(FalloutSourceImageSpaceFloat.Retire(true, start, 0, .25f, -10000) == (start >= -10000),
                "Animated retirement lost the same -10000 boundary or unordered behavior.");
            images.RetireSourceMainImageNative(native); restored.RetireSourceMainImageNative(coldNative);
        }
    }

    private static void MenuImageOrderedFormRemoval()
    {
        using var fixture = new StandaloneFixture(); var (images, native) = BindImageFixture(fixture);
        var removed = ImageProgram();
        var first = removed with { Form = new("Authored.esm", 0x508), SourceSha256 = new('d', 64) };
        var second = removed with { Form = new("Authored.esm", 0x509), SourceSha256 = new('f', 64) };
        for (var index = 0; index < 192; index++) images.Apply(index % 3 == 0 ? removed : index % 3 == 1 ? first : second);
        var before = images.CaptureSourceMainImages();
        images.RemoveSourceImageModifier(new("Authored.esm", 0x599));
        Require(images.CaptureSourceMainImages().Changed == before.Changed, "No-match form removal performed a store or released another form.");
        images.RemoveSourceImageModifier(removed.Form);
        var after = images.CaptureSourceMainImages();
        var sourceOrder = before.Instances.ToDictionary(row => row.Identity);
        var expectedSurvivors = before.List.Where(id => sourceOrder[id].Form != removed.Form).ToArray();
        var expectedRetired = before.List.Where(id => sourceOrder[id].Form == removed.Form).ToArray();
        var retired = after.Instances.Where(row => row.Form == removed.Form).ToDictionary(row => row.Identity);
        Require(after.List.SequenceEqual(expectedSurvivors) && after.Changed == before.Changed + expectedRetired.Length &&
            expectedRetired.Select((id, index) => retired[id].Retired == before.Changed + index + 1).All(value => value) &&
            after.Instances.Where(row => row.Form != removed.Form).All(row => row == sourceOrder[row.Identity]) &&
            after.Curves.Count == before.Curves.Count && after.Curves.Zip(before.Curves).All(pair =>
                (pair.First with { Value = pair.Second.Value }) == pair.Second && pair.First.Value.SequenceEqual(pair.Second.Value)),
            "Form-wide compaction reordered survivors/retirement stores or mutated independent instances/shared curve caches.");
        images.RemoveSourceImageModifier(removed.Form);
        Require(images.CaptureSourceMainImages().Changed == after.Changed, "Repeated form removal replayed a historical reference retirement.");
        images.RetireSourceMainImageNative(native);
    }

    private static void MenuImageDeclaration()
    {
        var fixture = new AuthoredMenuDeclaration();
        var declaration = FalloutExecutableStringTable.ReadStandaloneImageSpaceMenu(fixture.Code, fixture.Base, fixture.Read);
        Require(declaration.Strength == ImageBits(1) && declaration.InitialStart == ImageBits(-10001) &&
            declaration.UnsetBoundary == ImageBits(-10000) && declaration.AuxiliaryForm == 0x162,
            "Source menu reader lost the real factory/update/removal declaration fields.");
        foreach (var corrupt in new Action<AuthoredMenuDeclaration>[]
        {
            row => row.Code[96] = 1, // target is no longer null
            row => row.Word(102, ImageBits(.5f)),
            row => row.Word(632, ImageBits(-9999)),
            row => row.Word(1500, ImageBits(-9999)),
            row => row.Code[725] = 0, // factory no longer registers the shared list
            row => row.Call(333, 1804), // restoration updates a different manager
            row => row.Call(321, 1804), // restoration removes through a different owner
            row => row.Word(1413, 1504), // retirement and initialization compare different source cells
        })
        {
            var changed = new AuthoredMenuDeclaration(); corrupt(changed);
            Reject(() => FalloutExecutableStringTable.ReadStandaloneImageSpaceMenu(changed.Code, changed.Base, changed.Read));
        }
        var ambiguous = new AuthoredMenuDeclaration();
        Array.Copy(ambiguous.Code, 320, ambiguous.Code, 360, 18);
        ambiguous.Call(361, 1820); ambiguous.Call(373, 1800);
        Reject(() => FalloutExecutableStringTable.ReadStandaloneImageSpaceMenu(ambiguous.Code, ambiguous.Base, ambiguous.Read));
        Reject(() => (ImageMenuDeclaration() with { InitialStart = ImageBits(-10000) }).Validate());
    }

    // Authored PE-like declaration map; no retail bytes or addresses are used.
    private sealed class AuthoredMenuDeclaration
    {
        internal uint Base => 0x100000;
        internal byte[] Code { get; } = new byte[2048];
        internal AuthoredMenuDeclaration()
        {
            void Bytes(int at, byte[] bytes) => bytes.CopyTo(Code, at);
            Code[50] = 0x68; Word(51, 0x162);
            Code[77] = 0x50; Call(78, 1820);
            Bytes(96, [0x6a, 0, 0x51, 0xc7, 4, 0x24]); Word(102, ImageBits(1)); Code[106] = 0x50; Call(107, 512);
            Bytes(112, [0x83, 0xc4, 0x0c, 0x8b, 0xcf, 0x6a, 0]); Call(119, 1800);
            Code[320] = 0x50; Call(321, 1820); Bytes(326, [0x83, 0xc4, 4, 0x8b, 0xcf, 0x6a, 0]); Call(333, 1800);
            Bytes(512, [0x55, 0x8b, 0xec]); Bytes(629, [0xc7, 0x46, 0x14]); Word(632, ImageBits(-10001));
            Bytes(638, [0xc7, 6]); Word(640, Base + 800); Bytes(672, [0xc6, 0x46, 8, 0]);
            Bytes(722, [0x8d, 0x49, 0x6c]); Call(725, 1840);
            Word(944, Base + 1100); Word(940, Base + 1200);
            Bytes(1143, [0xf3, 0x0f, 0x10, 5]); Word(1147, Base + 1500); Bytes(1151, [0x0f, 0x2f, 0xc1]);
            Call(1220, 1400); Bytes(1409, [0xf3, 0x0f, 0x10, 5]); Word(1413, Base + 1500); Word(1500, ImageBits(-10000));
        }
        internal void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(at), value);
        internal void Call(int at, int target)
        { Code[at] = 0xe8; Word(at + 1, unchecked((uint)(target - at - 5))); }
        internal byte[] Read(uint address, int count)
        {
            var offset = checked((int)(address - Base));
            if (offset < 0 || count < 0 || offset > Code.Length - count) throw new InvalidDataException("Authored declaration address escaped its source map.");
            return Code.AsSpan(offset, count).ToArray();
        }
    }
}
