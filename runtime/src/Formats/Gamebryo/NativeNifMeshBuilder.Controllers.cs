using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private void ValidateNodeController(FalloutNifNode owner, Node3D node)
        {
            // Validate the entire original linked list before publishing any
            // controller. A dormant controller still owns its real suffix.
            var chain = FalloutNifNodeControllerChain.Read(_source, owner);
            for (var ordinal = 0; ordinal < chain.Count; ordinal++)
            {
                var controller = chain[ordinal];
                if (controller is FalloutNifBlendController blend)
                {
                    FalloutNifNodeControllerChain.RequireDormantBlend(owner, blend);
                    node.SetMeta($"opennv_nif_blend_controller_{blend.Block.Index}", blend.Keys);
                    node.SetMeta("opennv_nif_blend_controller", blend.Block.Index);
                    node.SetMeta("opennv_nif_blend_controller_next", blend.Time.NextController);
                    node.SetMeta("opennv_nif_blend_controller_source_dormant", true);
                    continue;
                }
                if (controller is FalloutNifVisibilityController visibility)
                {
                    if (visibility.Time.Flags == DormantManagerFlags &&
                        new FalloutNifBoolAnimation(_source, visibility.Interpolator).ConstantValue is { } constantVisible)
                    {
                        node.Visible = constantVisible;
                        node.SetMeta("opennv_nif_constant_visibility_controller", visibility.Block.Index);
                    }
                    else if (ExternalTransformTargets?.Contains(owner.Name) == true &&
                        (visibility.Time.Flags & 0x0040) != 0)
                    {
                        // The original range can include export preroll. The
                        // selected KF later supplies this node's external clock.
                        node.Visible = new FalloutNifBoolAnimation(_source, visibility.Interpolator)
                            .Sample((float)FalloutNifControllerClock.Resolve(visibility.Time, 0));
                        node.SetMeta("opennv_nif_external_visibility_controller", visibility.Block.Index);
                    }
                    else if ((visibility.Time.Flags & 0x20) == 0)
                        BuildDirectVisibilityController(visibility, node);
                    continue;
                }
                if (controller is FalloutNifBoneLodController boneLod)
                {
                    ValidateBoneLodController(owner, node, boneLod);
                    continue;
                }
                if (controller is FalloutNifTransformController direct)
                {
                    if (ExternalTransformTargets?.Contains(owner.Name) == true &&
                        (direct.Time.Flags & 0x0040) != 0)
                    {
                        node.SetMeta("opennv_nif_external_transform_controller", direct.Block.Index);
                        continue;
                    }
                    if (TryPreserveConstantBindTransform(owner, node, direct) ||
                        TryApplyStaticTransformController(owner, node, direct) ||
                        TryBuildDirectTransformController(owner, node, direct))
                        continue;
                    if (direct.Time.Flags is not (DormantDirectTransformFlags or DormantMultiTargetFlags or DormantManagerFlags) ||
                        direct.Time.Frequency != 1.0f || direct.Time.Phase != 0.0f ||
                        direct.Time.StartTime != float.MaxValue || direct.Time.StopTime != float.MinValue ||
                        direct.Interpolator != -1)
                        throw new NotSupportedException(
                            $"NIF node {owner.Block.Index} has an unsupported direct transform controller " +
                            $"block={direct.Block.Index} next={direct.Time.NextController} " +
                            $"flags=0x{direct.Time.Flags:x4} frequency={direct.Time.Frequency:R} " +
                            $"phase={direct.Time.Phase:R} start={direct.Time.StartTime:R} " +
                            $"stop={direct.Time.StopTime:R} target={direct.Time.Target} " +
                            $"interpolator={direct.Interpolator}.");
                    node.SetMeta("opennv_nif_dormant_transform_controller", direct.Block.Index);
                    node.SetMeta("opennv_nif_dormant_transform_next", direct.Time.NextController);
                    continue;
                }
                if (controller is FalloutNifControllerManager manager)
                {
                    ValidateNodeControllerManager(owner, node, manager);
                    // The existing manager owner requires its exact terminal
                    // multi-target companion. It is not a second direct owner.
                    if (ordinal + 1 >= chain.Count || chain[ordinal + 1].Block.Index != manager.Time.NextController)
                        throw new InvalidDataException("NIF controller manager lost its original companion order.");
                    ordinal++;
                    continue;
                }
                throw new NotSupportedException(
                    $"NIF node {owner.Block.Index} ({owner.Name}) controller {controller.Block.Index} " +
                    $"{controller.Block.TypeName} has no native node channel owner.");
            }
            if (chain.Count != 0)
                node.SetMeta("opennv_nif_node_controller_chain", chain.Select(value => value.Block.Index).ToArray());
        }

        private void ValidateNodeControllerManager(FalloutNifNode owner, Node3D node, FalloutNifControllerManager manager)
        {
            if (manager.Time.Flags != DormantManagerFlags || manager.Time.Frequency != 1.0f ||
                manager.Time.Phase != 0.0f || manager.Time.StartTime != float.MaxValue ||
                manager.Time.StopTime != float.MinValue || manager.Time.Target != owner.Block.Index ||
                manager.Time.UnknownInteger != 0 || manager.Cumulative || manager.Sequences.Length == 0 ||
                manager.ObjectPalette == -1 || manager.Time.NextController == -1)
                throw new NotSupportedException(
                    $"NIF node {owner.Block.Index} ({owner.Name}) has an unsupported active controller manager contract: {manager}.");
            if (_source.ReadObject(manager.Time.NextController) is not FalloutNifMultiTargetTransformController multi ||
                multi.Time.NextController != -1 || multi.Time.Flags != DormantMultiTargetFlags ||
                multi.Time.Frequency != 1.0f || multi.Time.Phase != 0.0f ||
                multi.Time.StartTime != float.MaxValue || multi.Time.StopTime != float.MinValue ||
                multi.Time.Target != owner.Block.Index || multi.Time.UnknownInteger != 0 ||
                multi.ExtraTargets.Any(reference => reference != -1 &&
                    _source.ReadObject(reference) is not (FalloutNifNode or FalloutNifGeometry or FalloutNifParticleSystem)))
                throw new NotSupportedException(
                    $"NIF controller manager {manager.Block.Index} has an unsupported target chain.");
            if (_source.ReadObject(manager.ObjectPalette) is not FalloutNifDefaultAvObjectPalette palette ||
                palette.UnknownInteger != 0 || palette.Objects.Length == 0)
                throw new NotSupportedException(
                    $"NIF controller manager {manager.Block.Index} has an unsupported object palette.");
            foreach (var sequenceReference in manager.Sequences)
            {
                if (_source.ReadObject(sequenceReference) is not FalloutNifControllerSequence sequence ||
                    sequence.Manager != manager.Block.Index || sequence.TextKeys == -1 ||
                    _source.ReadObject(sequence.TextKeys) is not FalloutNifTextKeyExtraData ||
                    sequence.CycleType is not (0U or 2U) ||
                    sequence.ControlledBlocks.Any(link => link.Interpolator == -1 ||
                        link.Controller == -1 || link.Priority != 0 ||
                        link.ControllerType is not ("NiTransformController" or "NiVisController" or
                            "NiTextureTransformController" or "NiMaterialColorController" or "NiAlphaController" or
                            "BSMaterialEmittanceMultController" or "BSRefractionStrengthController" or "BSRefractionFirePeriodController" or
                            "NiGeomMorpherController" or "NiPSysEmitterCtlr" or "NiPSysEmitterSpeedCtlr" or
                            "NiPSysEmitterLifeSpanCtlr" or "NiPSysModifierActiveCtlr")))
                    throw new NotSupportedException(
                        $"NIF controller manager {manager.Block.Index} has an unsupported sequence chain.");
            }
            _controllerManagers.Add(manager);
            node.SetMeta("opennv_nif_dormant_controller_manager", manager.Block.Index);
        }
    }
}
