using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourceMainImageContracts()
    {
        ImageDuplicateFactoryAndCold(); ImageStrictRetirement(); ImageFailurePrefixAndCold(); ImageMissingCurvePrefix(); ImageShortProfileAndCold();
        Console.WriteLine("OPENNV_SOURCE_MAIN_IMAGES_PASS authored=true sourceOrder=true duplicateFactories=true strictRetirement=true " +
            "coldNoReplay=true missingCurveFails=true immutablePublication=true shortProfileAndCold=true nativeGameplay=UNEXECUTED");
    }
    private static uint ImageBits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static FalloutSourceImageSpaceDefaults ImageDefaults()
    {
        var words = Enumerable.Repeat(ImageBits(1), 38).ToArray(); words[32] = words[36] = words[37] = 0;
        return new(words, new('a', 64));
    }
    private static FalloutSourceImageSpaceDofConstructor ImageDof() => new(0, ImageBits(600),
        [0, ImageBits(1), ImageBits(1), 0, ImageBits(1), ImageBits(1000), 0, ImageBits(10000), 0, 0, 0, 0],
        "fRenderDepthMaxDepth:Imagespace", ImageBits(10000), new('b', 64));
    private static FalloutImageSpaceModifier ImageProgram(bool animated = false)
    {
        FalloutImageFloatKey[] Curve(float value) => [new(0, value), new(1, value)];
        return new(new("Authored.esm", 0x505), "AuthoredImageProgram", new('c', 64), animated, .25f,
            Enumerable.Range(0, 21).Select(index => Curve(index == 0 ? .5f : 1)).ToArray(),
            Enumerable.Range(0, 21).Select(_ => Curve(0)).ToArray(),
            FalloutImageSpaceModifierReader.EffectCounts.Keys.ToDictionary(name => name, name => Curve(name == "BNAM" ? 2 : 0)),
            [new(0, new(1, 1, 1, 0)), new(1, new(1, 1, 1, 0))],
            [new(0, new(1, 1, 1, 0)), new(1, new(1, 1, 1, 0))], 0, new(.5f, .5f), false, null, null);
    }
    private static (FalloutImageSpaceState State, AuthoredImageNative Native) BindImageFixture(StandaloneFixture fixture,
        FalloutSourceImageSpaceMainSnapshot? saved = null)
    {
        var state = new FalloutImageSpaceState();
        state.ConfigureSourceMainImages(fixture.Host.Source, fixture.Owner.ReadMainNativeDeliveryBindings().Process, ImageDefaults(), ImageDof(), saved);
        var native = new AuthoredImageNative(state.SourceMainImageIdentity, state.SourceMainImageProcess);
        state.BindSourceMainImageNative(native);
        fixture.Host.DuringContexts = (invocation, advance) => state.ExecuteSourceImageMain(invocation, advance, .25f,
            fixture.Owner.ReadSourceImageContextClock(invocation), () => fixture.Owner.ReadSourceImageContextClock(invocation));
        return (state, native);
    }
    private static void ImageDuplicateFactoryAndCold()
    {
        using var warm = new StandaloneFixture(); var (images, native) = BindImageFixture(warm); var program = ImageProgram();
        images.Apply(program); images.Apply(program);
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(1, .9f).GetAwaiter().GetResult();
        var saved = images.CaptureSourceMainImages(); var runtime = warm.Owner.Capture();
        Require(saved.List.Count == 2 && saved.List.Distinct().Count() == 2 && saved.Last is { Returned: true } &&
            saved.Output!.Profile.Words[0] == ImageBits(.25f) && saved.Output.Blur == ImageBits(2) && saved.Output.Delta == ImageBits(.25f) &&
            saved.Output.Depth.FocusDistance is null && saved.Last.ClockAfter == ImageBits(.25f) &&
            native.Effects == 1 && native.Profiles == 1 && native.Contexts == 42 && native.Depth == 0,
            "Source image Main lost duplicate instances, authored timer, actual scalar stores, unknown DOF cells or balanced allocation scopes.");
        Require(saved.Curves.Count == 55 && saved.Curves.All(curve => curve.Time == 0), "Shared source program caches were omitted or duplicated per instance.");
        Reject(() => images.Advance(.1));
        var changedOutput = saved.Output! with { Profile = saved.Output!.Profile with { Words = new uint[38] } };
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Output = changedOutput }));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { List = [saved.List[0], saved.List[0]] }));
        using var cold = new StandaloneFixture(runtime, warm.Fade.Capture());
        var (restored, newNative) = BindImageFixture(cold, saved); restored.RebindSourceImagePrograms(_ => program);
        Require(newNative.Effects == 0 && restored.CaptureSourceMainImages().Last == saved.Last && newNative.Process != native.Process,
            "Cold image construction published a historical native frame or replayed a source callback.");
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, .1f).GetAwaiter().GetResult();
        var next = restored.CaptureSourceMainImages();
        Require(next.Last!.Ordinal == 2 && next.Last.Native == newNative.NativeIdentity && next.Last.Native != saved.Last!.Native &&
            next.Output!.Profile.Words[0] == ImageBits(.25f), "Cold image Main reused a native factory or lost real program/list state.");
        restored.Remove(program.Form);
        var removed = restored.CaptureSourceMainImages();
        Require(removed.List.Count == 0 && removed.Instances.All(row => row.Retired is not null), "Remove did not release every duplicate manager reference.");
        images.RetireSourceMainImageNative(native); restored.RetireSourceMainImageNative(newNative);
    }
    private static void ImageStrictRetirement()
    {
        using var fixture = new StandaloneFixture(); var (images, native) = BindImageFixture(fixture); images.Apply(ImageProgram(animated: true));
        using var delivery = fixture.Bind();
        fixture.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
        fixture.Owner.ExecuteMainScriptCaller(2, 0).GetAwaiter().GetResult();
        Require(images.CaptureSourceMainImages().List.Count == 1, "Image retirement used >= duration instead of the actual strict comparison.");
        fixture.Owner.ExecuteMainScriptCaller(3, 0).GetAwaiter().GetResult();
        var saved = images.CaptureSourceMainImages();
        Require(saved.List.Count == 0 && saved.Last!.Children.Any(child => child.Name == "instance-list-reference-retirement") &&
            !saved.Last.Children.Any(child => child.Name == "instance-update"), "Retired current image node was updated or kept in the actual manager list.");
        images.RetireSourceMainImageNative(native);
    }
    private static void ImageFailurePrefixAndCold()
    {
        using var warm = new StandaloneFixture(); var (images, native) = BindImageFixture(warm); images.Apply(ImageProgram());
        native.FailEffects = true;
        using (warm.Bind()) Reject(() => warm.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult());
        var saved = images.CaptureSourceMainImages(); var runtime = warm.Owner.Capture();
        Require(saved.Last is { Returned: false, Failure: not null } && saved.Last.Children[^1] is { Name: "final-effect-fields", Returned: null } &&
            saved.Output!.Profile.Words[0] == ImageBits(.5f) && native.Effects == 1 && native.Profiles == 0 && native.Depth == 0,
            "Native publication failure erased scalar/clock prefix or reached the later profile setter.");
        using var cold = new StandaloneFixture(runtime, warm.Fade.Capture()); var (restored, nextNative) = BindImageFixture(cold, saved);
        Require(restored.SourceMainImageSaveBlocker is not null && nextNative.Effects == 0 && restored.CaptureSourceMainImages().Last == saved.Last,
            "Cold image failure was cleared or replayed.");
        using (cold.Bind()) Reject(() => cold.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult());
        Require(nextNative.Effects == 0, "Cold failed Main reached a new image native callback.");
        images.RetireSourceMainImageNative(native); restored.RetireSourceMainImageNative(nextNative);
    }
    private static void ImageMissingCurvePrefix()
    {
        using var fixture = new StandaloneFixture(); var (images, native) = BindImageFixture(fixture);
        var program = ImageProgram(); var curves = program.Effects.ToDictionary(pair => pair.Key, pair => pair.Value); curves["NAM1"] = [];
        images.Apply(program with { Effects = curves });
        using (fixture.Bind()) Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult());
        var saved = images.CaptureSourceMainImages();
        Require(saved.Last!.Children[^1] is { Name: "instance-update", Returned: null } && saved.Fields.Profile[0] == ImageBits(.5f) &&
            saved.Fields.Scalars[2] == 0 && native.Effects == 0 && saved.Curves.Single(curve => curve.Channel == "BNAM").Time == 0,
            "A missing native curve became neutral or committed blur before all scalar effect queries returned.");
        images.RetireSourceMainImageNative(native);
    }
    private sealed class AuthoredImageNative(string source, Guid process) : IFalloutSourceImageSpaceNative
    {
        public Guid NativeIdentity { get; } = Guid.NewGuid();
        public Guid Process => process;
        public string SourceIdentity => source;
        internal int Effects, Profiles, Contexts, Depth;
        internal bool FailEffects;
        private FalloutSourceImageSpaceOutput? _fields;
        public void RequireCurrentFactory() { }
        public IDisposable EnterSourceImageContext() { Contexts++; Depth++; return new Scope(this); }
        private sealed class Scope(AuthoredImageNative owner) : IDisposable
        { public void Dispose() { owner.Depth--; if (owner.Depth < 0) throw new InvalidOperationException("Authored native scope retired twice."); } }
        public void ResetOutputScalars() { _fields = null; }
        public void PublishEffects(FalloutSourceImageSpaceOutput value)
        { _fields = value; Effects++; if (FailEffects) throw new IOException("Authored actual receiver write fails after its real store."); }
        public void PublishProfile(FalloutSourceImageSpaceOutput value)
        { Require(ReferenceEquals(_fields, value), "Native final profile lost the same call's real fields."); Profiles++; }
    }
}
