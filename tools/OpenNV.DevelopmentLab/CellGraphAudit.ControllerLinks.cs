using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    // Internal declarations never become external filenames. The containing
    // ResourceRow owns winning file identity, original bytes and reader errors.
    internal sealed class ControllerLinkDeclaration
    {
        public string Kind { get; } = "nif-controller-link";
        public string SourceSha256 { get; init; } = "";
        public int Block { get; init; }
        public string Type { get; init; } = "";
        public int Offset { get; init; }
        public int Bytes { get; init; }
        public string Field { get; init; } = "";
        public int? Ordinal { get; init; }
        public string? DeclaredName { get; init; }
        public int TargetBlock { get; init; }
        public string? TargetType { get; init; }
        public int? TargetOffset { get; init; }
        public int? TargetBytes { get; init; }
        public string DecodeDisposition { get; set; } = "uninspected";
        public string TypedDisposition { get; set; } = "uninspected";
        public string? TypedOwner { get; init; }
        public string? Error { get; set; }
        public string Activation { get; } = "uninspected";
        public string Interval { get; } = "uninspected";
        public string Timeline { get; } = "uninspected";
        public string NativeAdmission { get; } = "unverified";
    }

    internal sealed record ControllerChannelDeclaration(
        string SourceSha256, int Block, string Type, int Offset, int Bytes,
        int Ordinal, string NodeName, string PropertyType, string ControllerType,
        string Variable1, string Variable2, int Interpolator, int Controller,
        byte Priority)
    {
        public string Kind { get; } = "nif-controller-channel";
        public string TargetBinding { get; } = "uninspected; requires the actual source skeleton/object/material palette";
        public string ExternalResource { get; } = "none inferred from names";
        public string NativeAdmission { get; } = "unverified";
        public string Timeline { get; } = "uninspected";
    }

    internal sealed record ControllerInputReceipt(
        string SourceSha256, int Block, string Type, int Offset, int Bytes,
        string Owner, string Disposition, string? Error)
    {
        public string Kind { get; } = "nif-controller-input-owner";
        public string Sampling { get; } = "not performed";
        public string Activation { get; } = "uninspected";
        public string Interval { get; } = "uninspected";
        public string Timeline { get; } = "uninspected";
        public string NativeAdmission { get; } = "unverified";
    }

    internal static class SourceControllerLinks
    {
        // Typed checks below are the exact existing consumers' link contracts,
        // not a general NIF class hierarchy or a new binary grammar guard.
        // Constructors validate original inputs; no Sample/clock/binding runs.
        internal static void Inspect(FalloutNifFile source, FalloutNifObject value, ResourceRow row)
        {
            var block = value.Block;
            if (block.Index < 0 || block.Index >= source.Blocks.Count || source.Blocks[block.Index] != block)
                throw new InvalidDataException("Controller declaration does not belong to the decoded source block table.");

            switch (value)
            {
                case FalloutNifControllerSequence sequence:
                    row.DependencyDeclarations.Add(new { kind = "nif-controller-sequence", sourceSha256 = source.Sha256,
                        block = block.Index, type = block.TypeName, offset = block.Offset, bytes = block.Size,
                        name = sequence.Name, targetName = sequence.TargetName, weight = sequence.Weight,
                        cycleType = sequence.CycleType, frequency = sequence.Frequency,
                        startTime = sequence.StartTime, stopTime = sequence.StopTime,
                        activation = "uninspected", interval = "uninspected", timeline = "uninspected", nativeAdmission = "unverified" });
                    Link("TextKeys", sequence.TextKeys,
                        "RuntimeNativeNifAnimation constructor: optional source text-key cast",
                        target => Require<FalloutNifTextKeyExtraData>(target, "Sequence text-key link is not text-key data."), optional: true);
                    Link("Manager", sequence.Manager);
                    // The decoded record carries -1 when this reference is not
                    // encoded by its header branch. Do not invent an absent
                    // source field from that reader default.
                    if (source.UserVersion2 is >= 24 and <= 28) Link("AnimationNotes", sequence.AnimationNotes);
                    for (var ordinal = 0; ordinal < sequence.ControlledBlocks.Length; ordinal++)
                    {
                        var channel = sequence.ControlledBlocks[ordinal];
                        row.DependencyDeclarations.Add(new ControllerChannelDeclaration(source.Sha256,
                            block.Index, block.TypeName, block.Offset, block.Size, ordinal,
                            channel.NodeName, channel.PropertyType, channel.ControllerType,
                            channel.Variable1, channel.Variable2, channel.Interpolator, channel.Controller, channel.Priority));
                        // Null blend slots and names depend on actual palettes.
                        // Reading an interpolator is not admitting this channel.
                        Link("ControlledBlocks.Interpolator", channel.Interpolator, ordinal: ordinal);
                        Link("ControlledBlocks.Controller", channel.Controller, ordinal: ordinal);
                    }
                    break;
                case FalloutNifTransformInterpolator input:
                    Link("Data", input.Data, "FalloutNifAnimationSampler constructor",
                        target => Require<FalloutNifTransformData>(target, "Transform interpolator has non-transform data."), optional: true);
                    Constructor("FalloutNifAnimationSampler", () => _ = new FalloutNifAnimationSampler(source, block.Index));
                    break;
                case FalloutNifSplineTransformInterpolator input:
                    var transformHandles = input.TranslationHandle != ushort.MaxValue ||
                        input.RotationHandle != ushort.MaxValue || input.ScaleHandle != ushort.MaxValue;
                    SplineLinks(input.Data, input.BasisData, transformHandles, "FalloutNifAnimationSampler constructor",
                        "Spline interpolator has non-spline data.", "Spline interpolator has non-basis data.");
                    Constructor("FalloutNifAnimationSampler", () => _ = new FalloutNifAnimationSampler(source, block.Index));
                    break;
                case FalloutNifFloatInterpolator input:
                    Link("Data", input.Data, "FalloutNifFloatAnimation constructor",
                        target => Require<FalloutNifFloatData>(target, "The float interpolator has non-float data."), optional: true);
                    Constructor("FalloutNifFloatAnimation", () => _ = new FalloutNifFloatAnimation(source, block.Index));
                    break;
                case FalloutNifSplineFloatInterpolator input:
                    SplineLinks(input.Data, input.BasisData, input.Handle != ushort.MaxValue,
                        "FalloutNifFloatAnimation constructor", "Float spline data is absent.", "Float spline basis is absent.");
                    Constructor("FalloutNifFloatAnimation", () => _ = new FalloutNifFloatAnimation(source, block.Index));
                    break;
                case FalloutNifPoint3Interpolator input:
                    Link("Data", input.Data, "FalloutNifPoint3Animation constructor",
                        target => Require<FalloutNifPositionData>(target, "Point3 interpolator has non-vector keys."), optional: true);
                    Constructor("FalloutNifPoint3Animation", () => _ = new FalloutNifPoint3Animation(source, block.Index));
                    break;
                case FalloutNifSplinePoint3Interpolator input:
                    SplineLinks(input.Data, input.BasisData, input.Handle != ushort.MaxValue,
                        "FalloutNifPoint3Animation constructor", "Point3 spline data is absent.", "Point3 spline basis is absent.");
                    Constructor("FalloutNifPoint3Animation", () => _ = new FalloutNifPoint3Animation(source, block.Index));
                    break;
                case FalloutNifBoolInterpolator input:
                    Link("Data", input.Data, "FalloutNifBoolAnimation constructor",
                        target => Require<FalloutNifBoolData>(target, "Boolean interpolator has non-boolean data."), optional: true);
                    Constructor("FalloutNifBoolAnimation", () => _ = new FalloutNifBoolAnimation(source, block.Index));
                    break;
                case FalloutNifPathInterpolator input:
                    Link("PathData", input.PathData, "FalloutNifAnimationSampler.PathSampler constructor",
                        target => Require<FalloutNifPositionData>(target, "Path has no position data."));
                    Link("PercentData", input.PercentData, "FalloutNifAnimationSampler.PathSampler constructor",
                        target => Require<FalloutNifFloatData>(target, "Path has no percentage data."));
                    Constructor("FalloutNifAnimationSampler", () => _ = new FalloutNifAnimationSampler(source, block.Index));
                    break;
                case FalloutNifControllerManager manager:
                    // Current managed-controller construction casts these two
                    // references. Only those typed casts are inspected here;
                    // dormant flags, target reachability and playback are not.
                    Time(manager.Time,
                        "NativeNifMeshBuilder.BuildState.BuildControllerPlayers: typed multi-target cast",
                        target => _ = (FalloutNifMultiTargetTransformController)target!);
                    Link("ObjectPalette", manager.ObjectPalette,
                        "NativeNifMeshBuilder.BuildState.BuildControllerPlayers: typed palette cast",
                        target => _ = (FalloutNifDefaultAvObjectPalette)target!);
                    for (var ordinal = 0; ordinal < manager.Sequences.Length; ordinal++)
                    {
                        var target = manager.Sequences[ordinal];
                        Link("Sequences", target, "FalloutNifFile.ReadControllerSequence",
                            ignored => { _ = source.ReadControllerSequence(target); }, ordinal: ordinal);
                    }
                    break;
                case FalloutNifMultiTargetTransformController multi:
                    Time(multi.Time);
                    for (var ordinal = 0; ordinal < multi.ExtraTargets.Length; ordinal++)
                        Link("ExtraTargets", multi.ExtraTargets[ordinal], ordinal: ordinal);
                    break;
                case FalloutNifDefaultAvObjectPalette palette:
                    for (var ordinal = 0; ordinal < palette.Objects.Length; ordinal++)
                        Link("Objects.Object", palette.Objects[ordinal].Object, ordinal: ordinal,
                            declaredName: palette.Objects[ordinal].Name);
                    break;
                case FalloutNifTransformController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifVisibilityController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifFloatExtraDataController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator, declaredName: controller.ExtraDataName); break;
                case FalloutNifMaterialColorController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifTextureTransformController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifAlphaController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifEmittanceController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifRefractionController controller:
                    Time(controller.Time); Link("Interpolator", controller.Interpolator); break;
                case FalloutNifParticleController controller:
                    Time(controller.Time);
                    if (block.TypeName != "NiPSysUpdateCtlr")
                        Link("Interpolator", controller.Interpolator, declaredName: controller.Modifier);
                    if (block.TypeName is "NiPSysEmitterCtlr" or "BSPSysMultiTargetEmitterCtlr")
                        Link("ActiveInterpolator", controller.ActiveInterpolator);
                    if (block.TypeName == "BSPSysMultiTargetEmitterCtlr") Link("Master", controller.Master);
                    break;
                case FalloutNifMorphController controller:
                    Time(controller.Time); Link("Data", controller.Data);
                    for (var ordinal = 0; ordinal < controller.Weights.Length; ordinal++)
                        Link("Weights.Interpolator", controller.Weights[ordinal].Interpolator, ordinal: ordinal);
                    break;
                case FalloutNifBoneLodController controller:
                    Time(controller.Time);
                    for (var group = 0; group < controller.NodeGroups.Length; group++)
                        for (var ordinal = 0; ordinal < controller.NodeGroups[group].Length; ordinal++)
                            Link($"NodeGroups[{group}]", controller.NodeGroups[group][ordinal], ordinal: ordinal);
                    break;
                case FalloutNifNode node:
                    Link("Controller", node.Controller); break;
                case FalloutNifGeometry geometry:
                    Link("Controller", geometry.Controller); break;
                case FalloutNifParticleSystem particle:
                    Link("Geometry.Controller", particle.Geometry.Controller); break;
            }

            void Time(FalloutNifTimeController time, string? nextOwner = null, Action<FalloutNifObject?>? nextValidation = null)
            {
                row.DependencyDeclarations.Add(new { kind = "nif-controller-clock", sourceSha256 = source.Sha256,
                    block = block.Index, type = block.TypeName, offset = block.Offset, bytes = block.Size,
                    flags = time.Flags, frequency = time.Frequency, phase = time.Phase,
                    startTime = time.StartTime, stopTime = time.StopTime,
                    activation = "uninspected", interval = "uninspected", timeline = "uninspected", nativeAdmission = "unverified" });
                Link("Time.NextController", time.NextController, nextOwner, nextValidation);
                Link("Time.Target", time.Target);
            }

            void SplineLinks(int data, int basis, bool active, string owner, string dataError, string basisError)
            {
                if (!active)
                {
                    // The actual constructor intentionally ignores both links
                    // when every source handle is the invalid-handle sentinel.
                    // Decode evidence remains separate from typed admission.
                    Link("Data", data, owner + ": no active source handles");
                    Link("BasisData", basis, owner + ": no active source handles");
                    return;
                }
                Link("Data", data, owner, target => Require<FalloutNifSplineData>(target, dataError));
                Link("BasisData", basis, owner, target => Require<FalloutNifSplineBasisData>(target, basisError));
            }

            void Link(string field, int index, string? owner = null, Action<FalloutNifObject?>? validate = null,
                bool optional = false, int? ordinal = null, string? declaredName = null)
            {
                FalloutNifBlock? targetBlock = index >= 0 && index < source.Blocks.Count ? source.Blocks[index] : null;
                var declaration = new ControllerLinkDeclaration
                {
                    SourceSha256 = source.Sha256, Block = block.Index, Type = block.TypeName,
                    Offset = block.Offset, Bytes = block.Size, Field = field, Ordinal = ordinal, DeclaredName = declaredName,
                    TargetBlock = index, TargetType = targetBlock?.TypeName,
                    TargetOffset = targetBlock?.Offset, TargetBytes = targetBlock?.Size, TypedOwner = owner,
                };
                row.DependencyDeclarations.Add(declaration);
                try
                {
                    FalloutNifObject? target = null;
                    if (index == -1) declaration.DecodeDisposition = "encoded-null";
                    else
                    {
                        target = source.ReadObject(index);
                        declaration.DecodeDisposition = "target-block-decoded";
                    }
                    if (validate is null) return;
                    if (index == -1 && optional)
                    {
                        declaration.TypedDisposition = "encoded-absent; no target substituted";
                        return;
                    }
                    // A required null target uses the actual reader's original
                    // refusal rather than a fabricated default object.
                    if (index == -1) _ = source.ReadObject(index);
                    validate(target);
                    declaration.TypedDisposition = "typed-consumer-target-admitted; native binding unverified";
                }
                catch (Exception error)
                {
                    if (declaration.DecodeDisposition == "uninspected") declaration.DecodeDisposition = "target-reader-refused";
                    declaration.TypedDisposition = "consumer-link-refused";
                    declaration.Error = error.Message;
                    row.Failures.Add(new { lane = "nif-controller-link", block = block.Index, type = block.TypeName,
                        field, ordinal, targetBlock = index, targetType = targetBlock?.TypeName,
                        typedOwner = owner, error = error.Message });
                }
            }

            void Constructor(string owner, Action construct)
            {
                string? failure = null;
                try { construct(); }
                catch (Exception error)
                {
                    failure = error.Message;
                    row.Failures.Add(new { lane = "nif-controller-input-owner", block = block.Index,
                        type = block.TypeName, owner, error = error.Message });
                }
                row.DependencyDeclarations.Add(new ControllerInputReceipt(source.Sha256,
                    block.Index, block.TypeName, block.Offset, block.Size, owner,
                    failure is null ? "pure-input-constructor-admitted" : "pure-input-constructor-refused", failure));
            }
        }

        private static void Require<T>(FalloutNifObject? target, string error) where T : FalloutNifObject
        {
            if (target is not T) throw new InvalidDataException(error);
        }
    }
}
