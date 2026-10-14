namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutImageSpaceState
{
    internal FalloutImageSpaceFrame ReadSourceMainImageFrame(FalloutSourceImageSpaceOutput output)
    {
        RequireImageThread();
        if (!ReferenceEquals(output, _sourceImageOutput) || output.Process != _sourceImageProcess || _sourceImageLast?.Invocation != output.Invocation)
            throw new InvalidDataException("Image frame changed its real current manager publication.");
        float Read(int index) => BitConverter.UInt32BitsToSingle(output.Profile.ReadWord(index));
        System.Numerics.Vector4 Vector(int at) => new(Read(at), Read(at + 1), Read(at + 2), Read(at + 3));
        var sky = ReadSourceSkyContributions().Where(row => _sourceImageList.Contains(row.Instance.Identity))
            .Select(row => row with { Failure = null }).ToArray();
        return new(Read(4), Vector(25), Vector(29), Vector(33), Active.ToArray(), [],
            BitConverter.UInt32BitsToSingle(output.Blur), ReadSourceMainDoubleVisionOffset(output.DoubleVision), sky, new(output.Profile.ReadWord(8), null));
    }
    internal FalloutSkyHdrImageInput ReadSourceMainSkyHdrInput(FalloutSourceImageSpaceOutput output,
        OpenNV.Runtime.Gameplay.State.FalloutSkyHdrState hdr, bool alternate)
    {
        _ = ReadSourceMainImageFrame(output);
        if (hdr.Process != output.Process)
            throw new InvalidDataException("Source Main HDR input changed its actual current process.");
        return new(_skyHdrImageOwner, hdr.Process, output.Invocation, checked(++_skyHdrImagePublication), output.Profile.Form,
            output.Profile.FormVersion, output.Profile.Source, output.Profile.ReadWord(8), hdr.Source.EngineSha256, hdr.Source.Contract,
            alternate, null, output.Profile.Form is null ? SourceMainImageDefault.Source : null);
    }
    internal Guid SourceMainImageProcess => HasSourceMainImages ? _sourceImageProcess :
        throw new InvalidOperationException("Source image manager has no current process.");
    internal FalloutSourceImageSpaceDofConstructor SourceMainImageDofConstructor => _sourceImageDofConstructor is { } constructor ?
        constructor with { Parameters = (uint[])constructor.Parameters.Clone() } : throw new InvalidOperationException("Source DOF constructor is absent.");
    internal FalloutSourceImageSpaceMainCall? SourceMainImageLast => _sourceImageLast;
    internal FalloutSourceImageSpaceProfile SourceMainImageDefault => (_sourceImageDefault ??
        throw new InvalidOperationException("Source image default constructor is absent.")).CloneProfile();

    internal System.Numerics.Vector2 ReadSourceMainDoubleVisionOffset(uint strength)
    {
        RequireImageThread();
        var clock = _phaseClock ?? throw new NotSupportedException("Source image output has no actual phase-clock constructor.");
        if (clock.Process != _sourceImageProcess) throw new InvalidDataException("Source image phase belongs to another process.");
        return clock.Source.Offset(clock.Source.AngleSeconds(clock.Seconds), BitConverter.UInt32BitsToSingle(strength));
    }

    internal FalloutSourceImageSpaceMainSnapshot CaptureSourceMainImages()
    {
        RequireImageThread();
        var result = new FalloutSourceImageSpaceMainSnapshot(SourceMainImageIdentity, _sourceImageProcess, _sourceImageChanged,
            SourceMainImageDefault, _sourceImagePrimary?.CloneProfile(), _sourceImageOverride?.CloneProfile(),
            _sourceImageInstances.Values.OrderBy(row => row.Constructed).ToArray(), _sourceImageList.ToArray(),
            _sourceImageUpdates, _sourceImageFields.Capture(), _sourceImageLast, _sourceImageOutput,
            _sourceImageProcesses.ToArray(), _sourceImageCurves.Values.Select(curve => curve with { Value = (uint[])curve.Value.Clone() }).ToArray(), SourceMainImageDofConstructor,
            _sourceImageMenuLast, _sourceMenuStack is null ? null : CaptureSourceMenuStackEffect());
        ValidateSourceMainImages(result); return result;
    }

    internal static void ValidateSourceMainImages(FalloutSourceImageSpaceMainSnapshot saved)
    {
        if (saved.Source.Length != 64 || !saved.Source.All(Uri.IsHexDigit) || saved.Process == Guid.Empty || saved.Changed < 0 ||
            saved.Processes.Count == 0 || saved.Processes.Any(process => process == Guid.Empty) ||
            saved.Processes.Distinct().Count() != saved.Processes.Count || saved.Processes[^1] != saved.Process ||
            saved.Instances.Select(row => row.Identity).Distinct().Count() != saved.Instances.Count ||
            saved.Instances.Select(row => row.Constructed).Distinct().Count() != saved.Instances.Count ||
            saved.List.Distinct().Count() != saved.List.Count)
            throw new InvalidDataException("Saved image manager lost its source/process/list identity.");
        saved.Default.Validate(); saved.Primary?.Validate(); saved.Override?.Validate();
        saved.DofConstructor.Validate();
        if (saved.Default.Form is not null) throw new InvalidDataException("Anonymous source default became a winning record.");
        FalloutSourceImageSpaceAccumulator.Validate(saved.Fields);
        foreach (var row in saved.Instances)
        {
            if (row.Identity == Guid.Empty || row.Constructed <= 0 || row.Changed < row.Constructed || row.Changed > saved.Changed ||
                row.Retired is { } retired && (retired < row.Constructed || retired != row.Changed) ||
                (row.Sky is null) != (row.SkyRegistration is null) || row.Sky == Guid.Empty || row.SkyRegistration is < 0 or > 3 ||
                row.Sky is null && row.Form is null || row.Form is not null && row.Program is null ||
                row.Program is { } program && (program.Length != 64 || !program.All(Uri.IsHexDigit)) ||
                (row.Retired is null) != saved.List.Contains(row.Identity))
                throw new InvalidDataException("Saved image instance changed its actual program, field or manager-reference lifetime.");
        }
        if (saved.List.Any(identity => !saved.Instances.Any(row => row.Identity == identity)))
            throw new InvalidDataException("Saved image manager list invented an unconstructed instance.");
        if (saved.Curves.Select(curve => (curve.Program, curve.Channel)).Distinct().Count() != saved.Curves.Count ||
            saved.Curves.Any(curve => string.IsNullOrWhiteSpace(curve.Program) || string.IsNullOrWhiteSpace(curve.Channel) ||
                curve.Value.Length != (curve.Channel is "TNAM" or "NAM3" ? 4 : 1) || curve.LastKey < 0 || curve.Changed <= 0 || curve.Changed > saved.Changed))
            throw new InvalidDataException("Saved image interpolation caches lost their real program/field writes.");
        if (saved.Last is { } call)
        {
            if (call.Invocation == Guid.Empty || call.Native == Guid.Empty || !saved.Processes.Contains(call.Process) || call.Ordinal <= 0 ||
                call.Children.Count == 0 || call.Children[0].Name != "profile-copy-and-output-reset" ||
                call.Returned && call.Failure is not null || !call.Returned && call.Failure is null ||
                !Enum.IsDefined(call.Caller) || call.Caller != FalloutSourceImageSpaceCaller.Main &&
                (call.Advance || call.Delta != 0 || call.ClockAfter != call.ClockBefore) ||
                call.Children.Count(child => child.Returned is null) != (call.Returned ? 0 : 1))
                throw new InvalidDataException("Saved Main image call omitted its actual successful or failed child prefix.");
            long previous = 0;
            foreach (var child in call.Children)
            {
                if (child.Entered <= previous || child.Entered > saved.Changed ||
                    child.Returned is { } returned && (returned <= child.Entered || returned > saved.Changed || child.Failure is not null) ||
                    child.Returned is null && (child != call.Children[^1] || child.Failure is null) ||
                    child.Instance is { } instance && !saved.Instances.Any(row => row.Identity == instance))
                    throw new InvalidDataException("Saved Main image child order or retained failure is invalid.");
                FalloutSourceImageSpaceAccumulator.Validate(child.Fields);
                previous = child.Returned ?? child.Entered;
            }
            if (call.Returned && (call.Children.Count < 4 || call.Children[1].Name != "accumulator-reset" ||
                call.Children[^2].Name != "final-effect-fields" || call.Children[^1].Name != "final-profile-and-globals" ||
                call.Advance != call.Children.Any(child => child.Name == "context-clock-store")))
                throw new InvalidDataException("Saved returned Main image call omitted an unconditional source child.");
        }
        if (saved.Output is { } output)
        {
            output.Profile.Validate();
            if (output.Invocation == Guid.Empty || !saved.Processes.Contains(output.Process) || output.Mutation <= 0 ||
                output.Mutation > saved.Changed || output.Radial.Parameters.Length != 4 || output.Radial.Center.Length != 2 ||
                saved.Last is null || !Enum.IsDefined(output.Caller) || saved.Last.Returned &&
                (output.Invocation != saved.Last.Invocation || output.Process != saved.Last.Process || output.Delta != saved.Last.Delta || output.Caller != saved.Last.Caller))
                throw new InvalidDataException("Saved immutable Main output changed its actual publication call.");
        }
        if (saved.Last?.Returned == true && saved.Output is null)
            throw new InvalidDataException("Returned Main image call lost its actual final output.");
        if (saved.Last?.Returned == true && (!saved.Fields.ResetCompleted || !saved.Output!.Profile.Words.SequenceEqual(saved.Fields.Profile) ||
            saved.Output.Profile.DefinedWords != saved.Fields.DefinedWords))
            throw new InvalidDataException("Returned Main image output differs from its actual complete native profile stores.");
        ValidateSourceImageMenu(saved);
        if (saved.MenuStack is { } stack)
        {
            // The menu declaration carries the selected Interface identity;
            // its validator checks it independently of the effect profile.
            ValidateSourceMenuStackEffect(stack);
            if (stack.Process != saved.Process || stack.Constructors.Any(row => !saved.Processes.Contains(row.Process)) ||
                stack.Constructors.Any(row => row.Changed > saved.Changed) || stack.Writes.Any(row => row.Changed > saved.Changed))
                throw new InvalidDataException("Menu effect changed its actual shared image-manager process/history.");
        }
    }
}
