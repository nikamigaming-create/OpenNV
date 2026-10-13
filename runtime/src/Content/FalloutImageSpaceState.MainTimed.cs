using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceInstance(Guid Identity, Guid? Sky, int? SkyRegistration,
    FalloutFormKey? Form, string? Program, byte Enabled, uint Weight, uint Start,
    Guid? PriorProgram, uint PriorWeight, uint LerpDuration, long Constructed, long Changed, long? Retired);
internal sealed record FalloutSourceImageSpaceMainChild(string Name, Guid? Instance, long Entered, long? Returned,
    string? Failure, FalloutSourceImageSpaceAccumulatorSnapshot Fields);
internal sealed record FalloutSourceImageSpaceMainCall(Guid Invocation, Guid Process, Guid Native, long Ordinal,
    uint ClockBefore, uint ClockAfter, uint Delta, bool Advance, IReadOnlyList<FalloutSourceImageSpaceMainChild> Children,
    bool Returned, string? Failure, FalloutSourceImageSpaceCaller Caller = FalloutSourceImageSpaceCaller.Main);
internal sealed record FalloutSourceImageSpaceMainSnapshot(string Source, Guid Process, long Changed,
    FalloutSourceImageSpaceProfile Default, FalloutSourceImageSpaceProfile? Primary, FalloutSourceImageSpaceProfile? Override,
    IReadOnlyList<FalloutSourceImageSpaceInstance> Instances, IReadOnlyList<Guid> List, bool UpdatesEnabled,
    FalloutSourceImageSpaceAccumulatorSnapshot Fields, FalloutSourceImageSpaceMainCall? Last, FalloutSourceImageSpaceOutput? Output,
    IReadOnlyList<Guid> Processes, IReadOnlyList<FalloutSourceImageCurveCache> Curves, FalloutSourceImageSpaceDofConstructor DofConstructor,
    FalloutSourceImageSpaceMenuCall? Menu = null);

internal sealed partial class FalloutImageSpaceState
{
    private FalloutMainScriptCallerSource? _sourceImageMain;
    private Guid _sourceImageProcess;
    private long _sourceImageChanged;
    private readonly List<Guid> _sourceImageList = [];
    private readonly Dictionary<Guid, FalloutSourceImageSpaceInstance> _sourceImageInstances = [];
    private readonly List<Guid> _sourceImageProcesses = [];
    private readonly FalloutSourceImageSpaceAccumulator _sourceImageFields = new();
    private FalloutSourceImageSpaceProfile? _sourceImageDefault, _sourceImagePrimary, _sourceImageOverride;
    private FalloutSourceImageSpaceDofConstructor? _sourceImageDofConstructor;
    private IFalloutSourceImageSpaceNative? _sourceImageNative;
    private FalloutSourceImageSpaceMainCall? _sourceImageLast;
    private FalloutSourceImageSpaceOutput? _sourceImageOutput;
    private bool _sourceImageBusy, _sourceImageUpdates = true;
    private readonly int _sourceImageThread = Environment.CurrentManagedThreadId;
    internal bool HasSourceMainImages => _sourceImageMain is not null;
    internal string SourceMainImageIdentity => _sourceImageMain is { } main && _sourceImageDefault is { } defaults
        ? FalloutAdvancementRuntimeReceipt.Hash(main.Identity + "\0" + defaults.Source + "\0" + SourceMainImageDofConstructor.Source + "\0source-image-space-Main/v1")
        : throw new InvalidOperationException("Image manager has no actual selected Main/default constructor.");
    internal string? SourceMainImageSaveBlocker => _sourceImageBusy ? "source-image-space-Main-entered" :
        _sourceImageLast?.Failure is not null ? "source-image-space-Main-failed" :
        _sourceImageMenuBusy ? "source-image-space-menu-capture-entered" :
        _sourceImageMenuLast?.Failure is not null ? "source-image-space-menu-capture-failed" :
        _sourceImageMain is not null && _sourceImageNative is null ? "source-image-space-native-factory-unbound" : null;

    internal void ConfigureSourceMainImages(FalloutMainScriptCallerSource main, Guid process, FalloutSourceImageSpaceDefaults defaults,
        FalloutSourceImageSpaceDofConstructor dofConstructor, FalloutSourceImageSpaceMainSnapshot? saved = null)
    {
        main.Validate(); RequireImageThread();
        if (_sourceImageMain is not null || process == Guid.Empty || !FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new InvalidOperationException("Source image manager changed its source/process constructor.");
        dofConstructor.Validate(); _sourceImageDofConstructor = dofConstructor with { Parameters = (uint[])dofConstructor.Parameters.Clone() };
        _sourceImageMain = main; _sourceImageProcess = process; _sourceImageDefault = FalloutSourceImageSpaceProfile.Anonymous(defaults);
        _sourceImageProcesses.Add(process);
        if (saved is null) return;
        ValidateSourceMainImages(saved);
        if (saved.Source != SourceMainImageIdentity || saved.Processes.Contains(process))
            throw new InvalidDataException("Cold image manager changed source or reused a process.");
        if (_sourceImageInstances.Count != 0 || _sourceImageList.Count != 0)
            throw new InvalidOperationException("Cold image manager cannot replace actual constructed instances.");
        _sourceImageChanged = saved.Changed; _sourceImagePrimary = saved.Primary?.CloneProfile(); _sourceImageOverride = saved.Override?.CloneProfile();
        _sourceImageUpdates = saved.UpdatesEnabled; _sourceImageFields.Restore(saved.Fields);
        foreach (var row in saved.Instances) _sourceImageInstances.Add(row.Identity, row);
        _sourceImageList.AddRange(saved.List); _sourceImageLast = saved.Last; _sourceImageOutput = saved.Output;
        _sourceImageMenuLast = saved.Menu;
        _sourceImageProcesses.Clear(); _sourceImageProcesses.AddRange(saved.Processes); _sourceImageProcesses.Add(process);
        foreach (var curve in saved.Curves) _sourceImageCurves.Add((curve.Program, curve.Channel), curve with { Value = (uint[])curve.Value.Clone() });
        // Native association deliberately remains absent. A fresh current
        // factory must bind; neither a historical call nor a saved ID binds it.
    }

    internal void BindSourceMainImageNative(IFalloutSourceImageSpaceNative native)
    {
        RequireImageThread(); ArgumentNullException.ThrowIfNull(native); native.RequireCurrentFactory();
        if (_sourceImageBusy || _sourceImageNative is not null || native.NativeIdentity == Guid.Empty ||
            native.Process != _sourceImageProcess || native.SourceIdentity != SourceMainImageIdentity)
            throw new InvalidOperationException("Image output binding is not the actual current native factory.");
        _sourceImageNative = native; ++_sourceImageChanged;
    }
    internal void RetireSourceMainImageNative(IFalloutSourceImageSpaceNative native)
    {
        RequireImageThread();
        if (_sourceImageBusy || !ReferenceEquals(_sourceImageNative, native))
            throw new InvalidOperationException("Image native retirement changed its current owner or entered Main lifetime.");
        _sourceImageNative = null; ++_sourceImageChanged;
    }
    internal void PublishSourceImagePrimary(FalloutImageSpace? source)
    {
        RequireImageWriter();
        using var context = RequireImageNative().EnterSourceImageContext();
        _sourceImagePrimary = source is null ? null : FalloutSourceImageSpaceProfile.Winning(source); ++_sourceImageChanged;
    }
    internal void PublishSourceImageOverride(FalloutImageSpace? source)
    {
        RequireImageWriter();
        using var context = RequireImageNative().EnterSourceImageContext();
        _sourceImageOverride = source is null ? null : FalloutSourceImageSpaceProfile.Winning(source); ++_sourceImageChanged;
    }
    internal void StoreSourceImageUpdates(bool enabled)
    { RequireImageWriter(); _sourceImageUpdates = enabled; ++_sourceImageChanged; }

    // Called at the existing real Sky registration hook. The source's Sky
    // helper prepends, while the script/form factory appends to this SAME list.
    private void RegisterSourceImageSky(Guid sky, Guid instance, int ordinal)
    {
        RequireImageThread();
        if (_sourceImageInstances.TryGetValue(instance, out var existing))
        {
            if (existing.Sky == sky && existing.SkyRegistration == ordinal && existing.Retired is null && _sourceImageList.Contains(instance)) return;
            throw new InvalidDataException("Source image Sky rebound another historical instance.");
        }
        var changed = ++_sourceImageChanged;
        _sourceImageInstances.Add(instance, new(instance, sky, ordinal, null, null, 0, 0, 0, null, 0, 0, changed, changed, null));
        _sourceImageList.Insert(0, instance);
    }
    private void RetireSourceImageSky(Guid instance)
    {
        RequireImageThread();
        if (!_sourceImageInstances.TryGetValue(instance, out var row) || row.Retired is not null) return;
        _sourceImageList.Remove(instance); var changed = ++_sourceImageChanged;
        _sourceImageInstances[instance] = row with { Retired = changed, Changed = changed };
    }
    private void RebindSourceImageSky(Guid previous, Guid sky, Guid instance, int ordinal)
    {
        RequireImageThread();
        if (!HasSourceMainImages) { RegisterSourceImageSky(sky, instance, ordinal); return; }
        if (!_sourceImageInstances.TryGetValue(previous, out var row) || row.Sky is null || row.SkyRegistration != ordinal ||
            row.Retired is not null || _sourceImageInstances.ContainsKey(instance) || previous == instance)
            throw new InvalidDataException("Cold Sky image rebind has no actual captured manager reference.");
        var index = _sourceImageList.IndexOf(previous);
        if (index < 0) throw new InvalidDataException("Cold Sky image rebind lost its actual manager position.");
        var retired = ++_sourceImageChanged;
        _sourceImageInstances[previous] = row with { Retired = retired, Changed = retired };
        var constructed = ++_sourceImageChanged;
        _sourceImageInstances.Add(instance, row with { Identity = instance, Sky = sky, Constructed = constructed, Changed = constructed });
        _sourceImageList[index] = instance;
    }

    internal void ExecuteSourceImageMain(FalloutMainScriptInvocation invocation, bool advance, float delta,
        uint clockBefore, Func<uint> readClockAfter)
    {
        invocation.Require(FalloutMainScriptCallerStep.TimedContexts); RequireImageWriter();
        if (invocation.Process != _sourceImageProcess || _sourceImageMain is null || _sourceImageLast?.Invocation == invocation.Identity)
            throw new InvalidOperationException("Image Main entry changed its actual process or replayed a call.");
        ExecuteSourceImageUpdate(invocation.Identity, invocation.Ordinal, FalloutSourceImageSpaceCaller.Main,
            advance, delta, clockBefore, readClockAfter, () => invocation.AdvanceContextTime(delta));
    }

    private void ExecuteSourceImageUpdate(Guid callIdentity, long ordinal, FalloutSourceImageSpaceCaller caller,
        bool advance, float delta, uint clockBefore, Func<uint> readClockAfter, Action advanceClock)
    {
        RequireImageWriter();
        var native = RequireImageNative(); var children = new List<FalloutSourceImageSpaceMainChild>();
        _sourceImageBusy = true;
        _sourceImageLast = new(callIdentity, _sourceImageProcess, native.NativeIdentity, ordinal,
            clockBefore, clockBefore, BitConverter.SingleToUInt32Bits(delta), advance, [], false, null, caller);
        void Child(string name, Guid? instance, Action action)
        {
            var entered = ++_sourceImageChanged;
            children.Add(new(name, instance, entered, null, null, _sourceImageFields.Capture()));
            _sourceImageLast = _sourceImageLast with { Children = children.ToArray() };
            try
            {
                action(); children[^1] = children[^1] with { Returned = ++_sourceImageChanged, Fields = _sourceImageFields.Capture() };
            }
            catch (Exception error)
            { children[^1] = children[^1] with { Failure = error.GetType().Name + ": " + error.Message, Fields = _sourceImageFields.Capture() }; throw; }
            finally { _sourceImageLast = _sourceImageLast with { Children = children.ToArray() }; }
        }
        try
        {
            Child("profile-copy-and-output-reset", null, () =>
            {
                _sourceImageFields.CopyProfile(_sourceImageOverride ?? _sourceImagePrimary ?? _sourceImageDefault!);
                native.ResetOutputScalars();
            });
            Child("accumulator-reset", null, _sourceImageFields.Reset);
            if (advance) Child("context-clock-store", null, advanceClock);
            var clock = readClockAfter(); _sourceImageLast = _sourceImageLast with { ClockAfter = clock };
            var enabled = 0; var cumulativeActive = 0; var index = 0;
            while (index < _sourceImageList.Count)
            {
                var identity = _sourceImageList[index];
                var row = RefreshSourceImageSky(identity);
                if (row.Enabled != 0) enabled++;
                var retire = false;
                Child("instance-retirement-query", identity, () => retire = SourceImageRetirement(row, clock));
                if (retire)
                {
                    Child("instance-list-reference-retirement", identity, () => RetireSourceImageInstance(identity));
                    // Source removes the current node by moving its successor
                    // into that node. It revisits it, and does not batch here.
                    continue;
                }
                if (_sourceImageUpdates)
                {
                    Child("instance-update", identity, () => UpdateSourceImageInstance(identity, clock));
                    row = _sourceImageInstances[identity];
                    if (BitConverter.UInt32BitsToSingle(row.Weight) > 0 && row.Enabled != 0) cumulativeActive++;
                }
                index++;
                if (enabled == 4)
                {
                    if (cumulativeActive == 0) Child("no-active-batch-identities", null, _sourceImageFields.NoActiveBatch);
                    Child("four-enabled-batch", null, () =>
                    { _sourceImageFields.FinishBatch(native.EnterSourceImageContext); });
                    enabled = 0;
                }
            }
            Child("final-effect-fields", null, () =>
            {
                _sourceImageFields.FinishColors();
                _sourceImageOutput = ReadSourceImageOutput(callIdentity, caller);
                native.PublishEffects(_sourceImageOutput);
            });
            Child("final-profile-and-globals", null, () => native.PublishProfile(_sourceImageOutput!));
            _sourceImageLast = _sourceImageLast with { Returned = true };
        }
        catch (Exception error)
        { _sourceImageLast = _sourceImageLast with { Failure = error.GetType().Name + ": " + error.Message }; throw; }
        finally { _sourceImageBusy = false; }
    }
    private FalloutSourceImageSpaceInstance RefreshSourceImageSky(Guid identity)
    {
        var row = _sourceImageInstances[identity];
        if (row.Sky is not { } sky) return row;
        if (!_sourceSkyInstances.TryGetValue(identity, out var bound) || bound.Sky.Identity != sky || bound.Ordinal != row.SkyRegistration)
            throw new InvalidDataException("Image Main lost the actual current Sky instance registration.");
        var actual = bound.Sky.ImageContributions().Single(value => value.Instance.Identity == identity).Instance;
        row = row with { Enabled = actual.EnabledByte, Weight = actual.WeightBits, Form = actual.Form, Program = actual.ProgramSha256, Changed = ++_sourceImageChanged };
        _sourceImageInstances[identity] = row; return row;
    }
    private bool SourceImageRetirement(FalloutSourceImageSpaceInstance row, uint now)
    {
        if (row.Form is null) return false;
        var source = RequireSourceImageProgram(row);
        return FalloutSourceImageSpaceFloat.Retire(source.Animated, BitConverter.UInt32BitsToSingle(row.Start),
            BitConverter.UInt32BitsToSingle(now), source.Duration, FalloutSourceImageSpaceFloat.UnsetStartBoundary);
    }
    private void UpdateSourceImageInstance(Guid identity, uint now)
    {
        var row = _sourceImageInstances[identity]; var weight = BitConverter.UInt32BitsToSingle(row.Weight);
        if (!(weight > 0) && !(BitConverter.UInt32BitsToSingle(row.PriorWeight) > 0)) return;
        if (row.Program is null && row.Form is null) return;
        if (FalloutSourceImageSpaceFloat.UnsetStartBoundary > BitConverter.UInt32BitsToSingle(row.Start))
        { row = row with { Start = now, Changed = ++_sourceImageChanged }; _sourceImageInstances[identity] = row; }
        if (row.PriorProgram is not null) throw new NotSupportedException("source-image-space-form-crossfade-program-consumer-unowned");
        if (row.Form is not null)
        {
            // The source form method, including actual curve objects, target
            // selection and scalar writers, is a separate admitted producer.
            UpdateWinningSourceImageProgram(row, RequireSourceImageProgram(row), now);
            return;
        }
        if (row.Sky is not { } sky || !_sourceSkyInstances.TryGetValue(identity, out var owner) || owner.Sky.Identity != sky ||
            row.Program != FalloutSourceSkyImageProgram.DefaultIdentity(owner.Sky.Source))
            throw new InvalidDataException("Anonymous image program is not its actual selected Sky constructor.");
        var program = "anonymous/" + row.Program; ConstructSourceImageCurves(program);
        float Scalar(string channel, float value) => SourceFloatCurve(program, channel,
            [new(0, value), new(1, value)], 0);
        for (var channel = 0; channel < 21; channel++)
        {
            var index = channel < 14 ? channel : channel < 17 ? channel + 1 : channel + 8;
            var multiply = Scalar(channel + "IAD", 1); var add = Scalar((channel + 64) + "IAD", 0);
            using var context = RequireImageNative().EnterSourceImageContext();
            _sourceImageFields.ApplyScalar(index, multiply, add, weight, row.Enabled != 0);
        }
        // The real anonymous program uses two linear keys and the same cache
        // writers as a record-backed program, including its zero-valued lanes.
        var colors = new FalloutImageColorKey[] { new(0, new(1, 1, 1, 0)), new(1, new(1, 1, 1, 0)) };
        _sourceImageFields.AccumulateTint(SourceColorCurve(program, "TNAM", colors, 0), weight, row.Enabled != 0);
        _sourceImageFields.AccumulateFade(SourceColorCurve(program, "NAM3", colors, 0), weight, row.Enabled != 0);
        var blur = Scalar("BNAM", 0); var vision = Scalar("VNAM", 0); var radial = Scalar("RNAM", 0);
        var parameters = new System.Numerics.Vector4(Scalar("SNAM", 0), Scalar("UNAM", 0), Scalar("NAM1", 0), Scalar("NAM2", 0));
        var option = Scalar("WNAM", 0); var first = Scalar("XNAM", 0); var second = Scalar("YNAM", 0); var scalar = Scalar("NAM4", 0);
        _sourceImageFields.AccumulateSimpleEffects(blur, vision, weight, row.Enabled != 0);
        _sourceImageFields.AccumulateRadial(radial, parameters, new(0.5f, 0.5f), weight, row.Enabled != 0);
        _sourceImageFields.AccumulateOptions(option, first, second, 0, weight, row.Enabled != 0);
        if (row.Enabled != 0) _sourceImageFields.ScalarBatch = FalloutSourceImageSpaceFloat.Add(
            FalloutSourceImageSpaceFloat.Multiply(scalar, weight), _sourceImageFields.ScalarBatch);
        else if (scalar > _sourceImageFields.Scalar) _sourceImageFields.Scalar = scalar;
    }
    private FalloutSourceImageSpaceOutput ReadSourceImageOutput(Guid identity, FalloutSourceImageSpaceCaller caller)
    {
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        static uint[] Vector(System.Numerics.Vector4 value) => [Bits(value.X), Bits(value.Y), Bits(value.Z), Bits(value.W)];
        var source = (_sourceImageOverride ?? _sourceImagePrimary ?? _sourceImageDefault)!;
        return new(identity, _sourceImageProcess, ++_sourceImageChanged, _sourceImageLast!.Delta,
            new(source.Form, source.Source, (uint[])_sourceImageFields.Profile.Clone(), source.FormVersion,
                _sourceImageFields.DefinedWords, source.SourceByteLength),
            Bits(_sourceImageFields.Blur), Bits(_sourceImageFields.DoubleVision),
            new(Bits(_sourceImageFields.RadialStrength), Vector(_sourceImageFields.RadialParameters),
                [Bits(_sourceImageFields.RadialCenter.X), Bits(_sourceImageFields.RadialCenter.Y)]),
            new(Bits(_sourceImageFields.OptionStrength), Bits(_sourceImageFields.OptionFirst), Bits(_sourceImageFields.OptionSecond),
                Bits(_sourceImageFields.OptionThird), _sourceImageFields.OptionMode, _sourceImageFields.OptionAlternate),
            new(Bits(_sourceImageFields.DofStrength), null, null, null, null, null), Bits(_sourceImageFields.Scalar), caller);
    }
    private IFalloutSourceImageSpaceNative RequireImageNative()
    {
        var native = _sourceImageNative ?? throw new NotSupportedException("Source image manager lost its actual native factory.");
        native.RequireCurrentFactory();
        if (native.Process != _sourceImageProcess || native.SourceIdentity != SourceMainImageIdentity)
            throw new InvalidDataException("Source image manager native factory changed source/current process.");
        return native;
    }
    private void RequireImageThread()
    { if (Environment.CurrentManagedThreadId != _sourceImageThread) throw new InvalidOperationException("Image manager changed its actual native/Main thread."); }
    private void RequireImageWriter()
    {
        RequireImageThread();
        if (_sourceImageBusy || _sourceImageLast?.Failure is not null || _sourceImageMenuLast?.Failure is not null)
            throw new InvalidOperationException("Image manager retains an entered or failed source child.");
    }
}
