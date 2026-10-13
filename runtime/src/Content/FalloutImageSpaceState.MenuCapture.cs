namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceMenuCall(Guid Identity, Guid Process, string Source,
    FalloutFormKey? Modifier, FalloutFormKey? Auxiliary, Guid? Instance, uint Clock,
    IReadOnlyList<string> Steps, string? Entered, bool Returned, string? Failure,
    FalloutSourceImageSpaceOutput? Captured, FalloutSourceImageSpaceOutput? Restored, bool? AuxiliaryEnabled = null);

internal sealed partial class FalloutImageSpaceState
{
    private FalloutSourceImageSpaceMenuDeclaration? _sourceImageMenuDeclaration;
    private FalloutSourceImageSpaceMenuCall? _sourceImageMenuLast;
    private bool _sourceImageMenuBusy;
    private long _sourceImageMenuOrdinal;
    internal FalloutSourceImageSpaceMenuCall? SourceImageMenuLast => _sourceImageMenuLast;
    internal uint SourceImageMenuAuxiliaryForm => (_sourceImageMenuDeclaration ??
        throw new NotSupportedException("Source menu image declaration is unbound.")).AuxiliaryForm;

    internal void ConfigureSourceImageMenu(FalloutSourceImageSpaceMenuDeclaration source)
    {
        RequireImageThread(); source.Validate();
        if (_sourceImageMain is null || _sourceImageMenuDeclaration is not null ||
            _sourceImageMenuLast is { } saved && saved.Source != source.Source)
            throw new InvalidDataException("Source menu image caller changed its selected manager/declaration.");
        _sourceImageMenuDeclaration = source;
    }

    // The selected menu-world caller uses the normal duplicate-admitting
    // factory and SAME manager list. Its lifetime spans one capture, followed
    // by form-wide removal and a second update with the same unadvanced clock.
    // The capture adapter retains immutable fields; menu closure never holds
    // a gameplay modifier alive or performs another legacy composition.
    internal void CaptureSourceImageMenu(FalloutImageSpaceModifier? modifier, FalloutFormKey? auxiliary,
        Func<uint> readClock, Action<FalloutSourceImageSpaceOutput> capture, bool? auxiliaryEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(readClock); ArgumentNullException.ThrowIfNull(capture);
        RequireImageWriter();
        var source = _sourceImageMenuDeclaration ?? throw new NotSupportedException("Source menu image caller declaration is unbound.");
        if (_sourceImageMenuBusy) throw new InvalidOperationException("Source menu world capture is already entered.");
        var clock = readClock();
        _sourceImageMenuLast = new(Guid.NewGuid(), _sourceImageProcess, source.Source, modifier?.Form, auxiliary,
            null, clock, [], null, false, null, null, null, auxiliaryEnabled);
        _sourceImageMenuBusy = true;
        var steps = new List<string>();
        void Step(string name, Action action)
        {
            _sourceImageMenuLast = _sourceImageMenuLast with { Entered = name };
            action(); steps.Add(name);
            _sourceImageMenuLast = _sourceImageMenuLast with { Steps = steps.ToArray(), Entered = null };
        }
        void Update(FalloutSourceImageSpaceCaller caller)
        {
            if (readClock() != clock) throw new InvalidDataException("Menu image caller changed the shared context clock.");
            ExecuteSourceImageUpdate(Guid.NewGuid(), checked(++_sourceImageMenuOrdinal), caller, false, 0, clock, readClock,
                () => throw new InvalidOperationException("Menu image update cannot advance the shared clock."));
            if (_sourceImageLast!.ClockAfter != clock)
                throw new InvalidDataException("Menu image update advanced the shared context clock.");
        }
        try
        {
            Step("auxiliary-form-remove", () =>
            {
                if (auxiliary is not { } form || auxiliaryEnabled == false) return;
                // A missing interface gate cannot be inferred from instance
                // presence: scripts may Apply this form independently. With
                // no live instance both gate branches leave the exact same
                // manager state; retain the unknown predicate in the receipt.
                if (auxiliaryEnabled is null)
                {
                    if (_sourceImageList.Any(id => _sourceImageInstances[id].Form == form))
                        throw new NotSupportedException("source-menu-auxiliary-interface-gate-producer-unowned");
                    return;
                }
                RemoveSourceImageModifier(form);
            });
            if (modifier is not null)
            {
                Step("form-factory", () =>
                {
                    ApplySourceImageModifier(modifier, BitConverter.UInt32BitsToSingle(source.Strength));
                    _sourceImageMenuLast = _sourceImageMenuLast with { Instance = _sourceImageList[^1] };
                });
                Step("shared-capture-update", () => Update(FalloutSourceImageSpaceCaller.MenuCapture));
            }
            Step("capture-output", () =>
            {
                var output = _sourceImageOutput ?? throw new NotSupportedException("Menu world capture has no actual manager output.");
                if (output.Process != _sourceImageProcess)
                    throw new NotSupportedException("Menu world capture has no current-process manager publication.");
                _sourceImageMenuLast = _sourceImageMenuLast with { Captured = output };
                capture(output);
            });
            if (modifier is not null)
            {
                Step("menu-form-remove", () => RemoveSourceImageModifier(modifier.Form));
                Step("shared-restore-update", () => Update(FalloutSourceImageSpaceCaller.MenuRestore));
                _sourceImageMenuLast = _sourceImageMenuLast with { Restored = _sourceImageOutput };
            }
            _sourceImageMenuLast = _sourceImageMenuLast with { Returned = true };
        }
        catch (Exception error)
        {
            _sourceImageMenuLast = _sourceImageMenuLast with { Failure = error.GetType().Name + ": " + error.Message };
            throw;
        }
        finally { _sourceImageMenuBusy = false; }
    }

    private static void ValidateSourceImageMenu(FalloutSourceImageSpaceMainSnapshot saved)
    {
        if (saved.Menu is not { } menu) return;
        string[] order = menu.Modifier is null ? ["auxiliary-form-remove", "capture-output"] :
            ["auxiliary-form-remove", "form-factory", "shared-capture-update", "capture-output", "menu-form-remove", "shared-restore-update"];
        if (menu.Identity == Guid.Empty || !saved.Processes.Contains(menu.Process) || menu.Source.Length != 64 ||
            !menu.Source.All(Uri.IsHexDigit) || menu.Steps.Count > order.Length ||
            !menu.Steps.SequenceEqual(order.Take(menu.Steps.Count)) ||
            menu.Returned && (menu.Failure is not null || menu.Entered is not null || menu.Steps.Count != order.Length) ||
            !menu.Returned && (menu.Failure is null || menu.Steps.Count >= order.Length || menu.Entered != order[menu.Steps.Count]) ||
            menu.Instance is { } instance && !saved.Instances.Any(row => row.Identity == instance && row.Form == menu.Modifier &&
                (!menu.Returned || row.Retired is not null)) ||
            menu.Returned && menu.Modifier is not null && (menu.Captured is null || menu.Restored is null ||
                menu.Captured.Caller != FalloutSourceImageSpaceCaller.MenuCapture || menu.Restored.Caller != FalloutSourceImageSpaceCaller.MenuRestore))
            throw new InvalidDataException("Saved menu image call lost its shared manager lifetime or retained prefix.");
        foreach (var output in new[] { menu.Captured, menu.Restored }.OfType<FalloutSourceImageSpaceOutput>())
        {
            output.Profile.Validate();
            if (output.Process != menu.Process || output.Invocation == Guid.Empty || output.Mutation <= 0 ||
                output.Mutation > saved.Changed || !Enum.IsDefined(output.Caller) ||
                output.Caller != FalloutSourceImageSpaceCaller.Main && output.Delta != 0 ||
                output.Radial.Parameters.Length != 4 || output.Radial.Center.Length != 2)
                throw new InvalidDataException("Saved menu output changed its actual process/clock publication.");
            if (saved.Last is { } call && call.Invocation == output.Invocation &&
                (call.ClockBefore != menu.Clock || call.ClockAfter != menu.Clock || call.Caller != output.Caller ||
                    saved.Output is null || !SameSourceImagePublication(output, saved.Output)))
                throw new InvalidDataException("Saved menu receipt differs from its actual current manager call/output.");
        }
        if (menu.Returned && menu.Modifier is not null &&
            (menu.Captured!.Invocation == menu.Restored!.Invocation || menu.Captured.Mutation >= menu.Restored.Mutation ||
                saved.Last is { Caller: FalloutSourceImageSpaceCaller.MenuRestore } current && current.Invocation != menu.Restored.Invocation))
            throw new InvalidDataException("Saved returned menu lost its ordered capture/restoration publications.");
    }

    private static bool SameSourceImagePublication(FalloutSourceImageSpaceOutput left, FalloutSourceImageSpaceOutput right) =>
        left.Invocation == right.Invocation && left.Process == right.Process && left.Mutation == right.Mutation &&
        left.Delta == right.Delta && left.Caller == right.Caller && left.Blur == right.Blur && left.DoubleVision == right.DoubleVision &&
        left.Scalar == right.Scalar && left.Options == right.Options && left.Depth == right.Depth &&
        left.Profile.Form == right.Profile.Form && left.Profile.Source == right.Profile.Source &&
        left.Profile.FormVersion == right.Profile.FormVersion && left.Profile.DefinedWords == right.Profile.DefinedWords &&
        left.Profile.SourceByteLength == right.Profile.SourceByteLength && left.Profile.Words.SequenceEqual(right.Profile.Words) &&
        left.Radial.Strength == right.Radial.Strength && left.Radial.Parameters.SequenceEqual(right.Radial.Parameters) &&
        left.Radial.Center.SequenceEqual(right.Radial.Center);
}
