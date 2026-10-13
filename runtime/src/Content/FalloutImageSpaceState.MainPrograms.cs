using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutImageSpaceState
{
    private readonly Dictionary<Guid, FalloutImageSpaceModifier> _sourceImagePrograms = [];
    internal void ApplySourceImageModifier(FalloutImageSpaceModifier source, float strength = 1)
    {
        RequireImageWriter();
        if (_sourceImageMain is null) throw new InvalidOperationException("Image-space script factory has no actual Main association.");
        var identity = Guid.NewGuid(); var changed = ++_sourceImageChanged;
        // Actual ApplyImageSpaceModifier uses the duplicate-admitting form
        // factory with a null target. Each invocation appends a real instance.
        _sourceImageInstances.Add(identity, new(identity, null, null, source.Form, source.SourceSha256, 0,
            BitConverter.SingleToUInt32Bits(strength), BitConverter.SingleToUInt32Bits(FalloutSourceImageSpaceFloat.InitialStart), null, 0, 0, changed, changed, null));
        _sourceImagePrograms.Add(identity, source); _sourceImageList.Add(identity); ConstructSourceImageCurves(ImageProgram(source));
    }
    internal void RemoveSourceImageModifier(FalloutFormKey form)
    {
        RequireImageWriter();
        // RemoveAll performs one stable compaction. Retire metadata in the
        // original list order; no callback can observe a partly compacted
        // list because these stores are synchronous on this owner thread.
        _sourceImageList.RemoveAll(identity =>
        {
            var row = _sourceImageInstances[identity];
            if (row.Form != form) return false;
            RetireSourceImageInstanceFields(row);
            return true;
        });
    }
    private void RetireSourceImageInstance(Guid identity)
    {
        var row = _sourceImageInstances[identity];
        if (row.Retired is not null || !_sourceImageList.Remove(identity))
            throw new InvalidOperationException("Source image list released another or already retired instance.");
        RetireSourceImageInstanceFields(row);
    }
    private void RetireSourceImageInstanceFields(FalloutSourceImageSpaceInstance row)
    {
        if (row.Retired is not null)
            throw new InvalidOperationException("Source image list released an already retired instance.");
        var changed = ++_sourceImageChanged;
        _sourceImageInstances[row.Identity] = row with { Retired = changed, Changed = changed };
        _sourceImagePrograms.Remove(row.Identity);
        // A Sky field is a separate retained reference. Its lifetime is not
        // erased by removing the manager's reference or by cold reattachment.
    }
    private FalloutImageSpaceModifier RequireSourceImageProgram(FalloutSourceImageSpaceInstance row)
    {
        if (_sourceImagePrograms.TryGetValue(row.Identity, out var owned))
        {
            if (owned.Form != row.Form || owned.SourceSha256 != row.Program) throw new InvalidDataException("Source image instance changed winning program.");
            return owned;
        }
        if (_sourceSkyInstances.TryGetValue(row.Identity, out var sky))
        {
            var source = sky.Sky.ImageContributions().Single(value => value.Instance.Identity == row.Identity).Source;
            if (source is not null && source.Form == row.Form && source.SourceSha256 == row.Program) return source;
        }
        throw new NotSupportedException("Source image instance has no bound current winning curve program.");
    }
    internal void RebindSourceImagePrograms(FalloutPluginStack records) =>
        RebindSourceImagePrograms(form => FalloutImageSpaceModifierReader.Read(records.GetEffective(form)));
    internal void RebindSourceImagePrograms(Func<FalloutFormKey, FalloutImageSpaceModifier> read)
    {
        RequireImageWriter();
        foreach (var row in _sourceImageInstances.Values.Where(row => row.Retired is null && row.Sky is null && row.Form is not null))
        {
            var source = read(row.Form!.Value);
            if (source.SourceSha256 != row.Program) throw new InvalidDataException("Cold image instance changed its winning IMAD.");
            _sourceImagePrograms.Add(row.Identity, source); ConstructSourceImageCurves(ImageProgram(source));
        }
    }
    private void UpdateWinningSourceImageProgram(FalloutSourceImageSpaceInstance row, FalloutImageSpaceModifier program, uint now)
    {
        var weight = BitConverter.UInt32BitsToSingle(row.Weight); var batch = row.Enabled != 0;
        var curveOwner = ImageProgram(program); ConstructSourceImageCurves(curveOwner);
        var time = program.Animated ? FalloutSourceImageSpaceFloat.Divide(
            FalloutSourceImageSpaceFloat.Subtract(BitConverter.UInt32BitsToSingle(now), BitConverter.UInt32BitsToSingle(row.Start)), program.Duration) : 0;
        if (!batch)
        {
            // MAXSS/MINSS take their second operand when unordered. The
            // source direct arm clamps; the weighted Sky arm does not.
            time = time > 0 ? time : float.IsNaN(time) ? time : 0;
            time = time < 1 ? time : float.IsNaN(time) ? time : 1;
        }
        for (var channel = 0; channel < 21; channel++)
        {
            var native = channel < 14 ? channel : channel < 17 ? channel + 1 : channel + 8;
            var multiply = SourceFloatCurve(curveOwner, channel + "IAD", program.Multiply[channel], time);
            var add = SourceFloatCurve(curveOwner, (channel + 64) + "IAD", program.Add[channel], time);
            using var context = RequireImageNative().EnterSourceImageContext();
            _sourceImageFields.ApplyScalar(native, multiply, add, weight, batch);
        }
        _sourceImageFields.AccumulateTint(SourceColorCurve(curveOwner, "TNAM", program.Tint, time), weight, batch);
        _sourceImageFields.AccumulateFade(SourceColorCurve(curveOwner, "NAM3", program.Fade, time), weight, batch);
        float Curve(string name) => SourceFloatCurve(curveOwner, name, program.Effects[name], time);
        var blur = Curve("BNAM"); var vision = Curve("VNAM");
        var radial = Curve("RNAM"); var parameters = new Vector4(Curve("SNAM"), Curve("UNAM"), Curve("NAM1"), Curve("NAM2"));
        var option = Curve("WNAM"); var first = Curve("XNAM"); var second = Curve("YNAM"); var scalar = Curve("NAM4");
        if (!batch)
        {
            blur = FalloutSourceImageSpaceFloat.Multiply(blur, weight);
            if (blur > 7) blur = 7; else if (0 > blur) blur = 0;
            _sourceImageFields.AccumulateSimpleEffects(blur, FalloutSourceImageSpaceFloat.Multiply(vision, weight), 1, false);
        }
        else _sourceImageFields.AccumulateSimpleEffects(blur, vision, weight, true);
        var center = program.RadialFlags != 0 && !batch ? new Vector2(0.5f, 0.5f) : program.RadialCenter;
        _sourceImageFields.AccumulateRadial(radial, parameters, center, weight, batch);
        // Native header byte following the target selector is independent;
        // the existing reader must preserve it before this call is admitted.
        var mode = program.DepthMode;
        if (program.DepthUsesTarget && !batch) { first = 0; second = 1; }
        _sourceImageFields.AccumulateOptions(option, first, second, mode, weight, batch);
        if (batch) _sourceImageFields.ScalarBatch = FalloutSourceImageSpaceFloat.Add(
            FalloutSourceImageSpaceFloat.Multiply(scalar, weight), _sourceImageFields.ScalarBatch);
        else if (scalar > _sourceImageFields.Scalar) _sourceImageFields.Scalar = scalar;
    }

}

internal sealed partial class FalloutSourceImageSpaceAccumulator
{
    internal void AccumulateRadial(float strength, Vector4 parameters, Vector2 center, float weight, bool batch)
    {
        var weighted = FalloutSourceImageSpaceFloat.Multiply(strength, weight);
        if (batch)
        {
            RadialBatchStrength = FalloutSourceImageSpaceFloat.Add(weighted, RadialBatchStrength);
            float At(float before, float next) => FalloutSourceImageSpaceFloat.Add(FalloutSourceImageSpaceFloat.Multiply(next, weight), before);
            RadialBatchParameters = new(At(RadialBatchParameters.X, parameters.X), At(RadialBatchParameters.Y, parameters.Y),
                At(RadialBatchParameters.Z, parameters.Z), At(RadialBatchParameters.W, parameters.W));
            RadialBatchCenter = new(At(RadialBatchCenter.X, center.X), At(RadialBatchCenter.Y, center.Y));
        }
        else if (weighted > RadialStrength) { RadialStrength = weighted; RadialParameters = parameters; RadialCenter = center; }
    }
    internal void AccumulateOptions(float strength, float first, float second, uint mode, float weight, bool batch)
    {
        var weighted = FalloutSourceImageSpaceFloat.Multiply(strength, weight);
        if (batch)
        {
            OptionBatchStrength = FalloutSourceImageSpaceFloat.Add(weighted, OptionBatchStrength); OptionBatchMode = mode; OptionBatchAlternate = false;
            OptionBatchFirst = FalloutSourceImageSpaceFloat.Add(FalloutSourceImageSpaceFloat.Multiply(first, weight), OptionBatchFirst);
            OptionBatchSecond = FalloutSourceImageSpaceFloat.Add(FalloutSourceImageSpaceFloat.Multiply(second, weight), OptionBatchSecond);
            OptionBatchThird = FalloutSourceImageSpaceFloat.Add(0, OptionBatchThird);
        }
        else if (weighted > OptionStrength)
        {
            OptionStrength = weighted; OptionMode = mode; OptionAlternate = false;
            if (OptionMode > 2) { OptionAlternate = true; OptionMode -= 3; }
            OptionFirst = first; OptionSecond = second; OptionThird = 0;
        }
    }
}
