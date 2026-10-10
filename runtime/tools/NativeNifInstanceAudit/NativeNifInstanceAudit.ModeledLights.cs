using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseModeledLights()
    {
        const float units = 1f / 64;
        var basis = new FalloutBaseObjectDefinition(new("Synthetic.esm", 0x801), "LIGH", "SourceLight",
            "meshes/synthetic/light.nif", new FalloutLightDefinition(-1, 200, [80, 120, 160], 0, 0, 1, 90, 0, 0, 1.5f));
        var reference = new FalloutPlacedReference(new("Synthetic.esm", 0x802), "SourceReference", new("Synthetic.esm", 0x800),
            basis.FormKey, 0, [0, 0, 0], [0, 0, 0], 2, null, null, null, false)
        { RadiusAdjustmentGameUnits = -40 };
        var placement = new Transform3D(new Basis(Vector3.Up, .37f).Scaled(Vector3.One * reference.Scale), new(4, 2, -1));
        var model = FalloutNifFile.Read(ModeledLightFixture(animated: false));
        var first = RuntimeNativeModeledLightBuilder.Build(reference, basis, model, placement, units, 1, 0, false);
        var otherBasis = basis with { Light = basis.Light! with { ColorRgb = [180, 10, 30], RadiusGameUnits = 400, Intensity = 2 } };
        var second = RuntimeNativeModeledLightBuilder.Build(reference with { FormKey = new("Synthetic.esm", 0x803) },
            otherBasis, model, placement, units, 1, 0, false);
        try
        {
            AddChild(first.Root); AddChild(second.Root);
            RuntimeNativeModeledLightBuilder.RestoreObjectClocks(first.Root, null);
            var point = ModeledPoint(first.Root); var other = ModeledPoint(second.Root);
            var expected = placement * (new Vector3(35, 8, 2) * units);
            if (!point.GlobalPosition.IsEqualApprox(expected) || point.GlobalPosition.IsEqualApprox(placement.Origin) ||
                point.OmniRange != 2.5f || other.OmniRange != 5.625f || point.LightEnergy != 1.5f || other.LightEnergy != 2 ||
                point.GetMeta("opennv_nif_point_source_dimmer").AsDouble() != 9 ||
                !point.GetMeta("opennv_ligh_current_diffuse").AsFloat32Array().SequenceEqual(FalloutPlacedLightResolver.NormalizeModeledRecordColor(basis.Light!.ColorRgb)) ||
                !other.GetMeta("opennv_ligh_current_diffuse").AsFloat32Array().SequenceEqual(FalloutPlacedLightResolver.NormalizeModeledRecordColor(otherBasis.Light!.ColorRgb)) ||
                first.Root.GetChild<Node3D>(0).Transform != Transform3D.Identity)
                throw new InvalidOperationException("Modeled point lost record overrides, descendant pose, root replacement, unscaled reach or instance independence.");
            OpenNV.Runtime.World.Cells.GamebryoReferenceEnableRuntime.Apply(first.Root, false);
            if (point.IsVisibleInTree() || first.Root.ProcessMode != ProcessModeEnum.Disabled)
                throw new InvalidOperationException("The actual modeled-light hierarchy ignored its shared enable owner.");
            OpenNV.Runtime.World.Cells.GamebryoReferenceEnableRuntime.Apply(first.Root, true);
            if (!point.IsVisibleInTree() || point.OmniRange != 2.5f || !point.GlobalPosition.IsEqualApprox(expected))
                throw new InvalidOperationException("Warm modeled-light enable changed its actual point pose or reach.");
        }
        finally { first.Root.Free(); second.Root.Free(); }

        var animated = FalloutNifFile.Read(ModeledLightFixture());
        var warm = RuntimeNativeModeledLightBuilder.Build(reference, basis, animated, placement, units, 1, 0, false);
        Node3D? cold = null;
        try
        {
            AddChild(warm.Root); warm.Root.ProcessMode = ProcessModeEnum.Disabled;
            var player = ModeledPlayer(warm.Root); var point = ModeledPoint(warm.Root);
            player.RequestSourceSequence("Forward", 1); player.SeekSourceTime(.5);
            var expected = new Vector3(.5f, .25f, .125f);
            if (!point.GetMeta("opennv_ligh_current_diffuse").AsFloat32Array().SequenceEqual(new[] { expected.X, expected.Y, expected.Z }))
                throw new InvalidOperationException("Managed diffuse multiplied record/NIF color instead of publishing the absolute curve.");
            var emissionSource = FalloutPlacedLightResolver.ResolveModeled(reference, basis,
                FalloutModeledLightSource.Read(model, basis.ModelPath!));
            var modulated = RuntimeNativePlacedLightBuilder.BuildResolved(emissionSource, basis, Transform3D.Identity,
                units, 1, 0, false);
            try
            {
                float[] modulation = [.5f, .25f, .75f];
                modulated.ConfigureSourceDiffuse([expected.X, expected.Y, expected.Z], () => modulation);
                modulated._Process(0);
                if (!modulated.GetMeta("opennv_ligh_shader_rgb").AsFloat32Array().SequenceEqual(new[] { .25f, .0625f, .09375f }))
                    throw new InvalidOperationException("External emittance double-multiplied managed diffuse.");
                RejectModeledLight(() => modulated.ConfigureSourceDiffuse([1f, 1f, 1f]));
                modulated.ApplySourceDiffuse(new(0, 0, 0));
                if (modulated.LightColor != new Color(0, 0, 0))
                    throw new InvalidOperationException("Source zero diffuse was raised by native publication.");
            }
            finally { modulated.Free(); }
            var saved = player.CaptureScriptState()!;
            cold = RuntimeNativeModeledLightBuilder.Build(reference, basis, animated, placement, units, 1, 0, false).Root;
            AddChild(cold); cold.ProcessMode = ProcessModeEnum.Disabled;
            RuntimeNativeModeledLightBuilder.RestoreObjectClocks(cold, [saved]);
            if (!ModeledPoint(cold).GlobalTransform.IsEqualApprox(point.GlobalTransform) ||
                !ModeledPoint(cold).GetMeta("opennv_ligh_current_diffuse").AsFloat32Array()
                    .SequenceEqual(point.GetMeta("opennv_ligh_current_diffuse").AsFloat32Array()) ||
                ModeledPlayer(cold).CaptureScriptState() != saved)
                throw new InvalidOperationException("Modeled point cold restore lost its selected source clock or actual pose/color.");
            player.RequestSourceSequence("Backward", 1); player.SeekSourceTime(2);
            if (point.GetMeta("opennv_ligh_current_diffuse").AsFloat32Array().Any(value => value != 0) ||
                point.LightColor != new Color(0, 0, 0))
                throw new InvalidOperationException("Source zero diffuse endpoint was lifted or retained an earlier color.");
            var coldPose = ModeledPoint(cold).GlobalTransform;
            RejectModeledLight(() => RuntimeNativeModeledLightBuilder.RestoreObjectClocks(cold,
                [saved with { Sha256 = new string('0', 64) }]));
            RejectModeledLight(() => RuntimeNativeModeledLightBuilder.RestoreObjectClocks(cold, [saved, saved]));
            if (ModeledPoint(cold).GlobalTransform != coldPose || ModeledPlayer(cold).CaptureScriptState() != saved)
                throw new InvalidOperationException("Rejected cold source identity mutated the actual selected light clock or pose.");
        }
        finally { cold?.Free(); warm.Root.Free(); }

        var automatic = RuntimeNativeModeledLightBuilder.Build(reference, basis,
            FalloutNifFile.Read(ModeledLightFixture(automatic: true)), placement, units, 1, 0, false);
        try
        {
            var owner = ModeledPlayer(automatic.Root);
            if (owner.ActiveSequence != "Forward" || owner.CaptureObjectState() is not { ScriptSelected: false })
                throw new InvalidOperationException("The current shared model owner lost its automatic source loop.");
            owner._Process(.37);
            var state = owner.CaptureObjectState()!;
            var automaticCold = RuntimeNativeModeledLightBuilder.Build(reference, basis,
                FalloutNifFile.Read(ModeledLightFixture(automatic: true)), placement, units, 1, 0, false);
            try
            {
                RuntimeNativeModeledLightBuilder.RestoreObjectClocks(automaticCold.Root, [state]);
                if (ModeledPlayer(automaticCold.Root).CaptureObjectState() != state ||
                    !ModeledPoint(automaticCold.Root).Transform.IsEqualApprox(ModeledPoint(automatic.Root).Transform) ||
                    !ModeledPoint(automaticCold.Root).GetMeta("opennv_ligh_current_diffuse").AsFloat32Array()
                        .SequenceEqual(ModeledPoint(automatic.Root).GetMeta("opennv_ligh_current_diffuse").AsFloat32Array()))
                    throw new InvalidOperationException("Current automatic object-clock cold restore lost the native source phase.");
            }
            finally { automaticCold.Root.Free(); }
        }
        finally { automatic.Root.Free(); }
        var multipleSource = FalloutNifFile.Read(ModeledLightFixture(animated: false, invalid: "duplicate"));
        var ordered = FalloutModeledLightSource.Read(multipleSource, basis.ModelPath!);
        if (ordered.Point.Block.Index != 2 || !ordered.ReachablePoints.SequenceEqual(new[] { 2, 18 }))
            throw new InvalidOperationException("Modeled point selection lost original ordered depth-first source traversal.");
        var multiple = RuntimeNativeModeledLightBuilder.Build(reference, basis, multipleSource, placement, units, 1, 0, false);
        try
        {
            if (multiple.Root.FindChildren("*", "", true, false).OfType<RuntimeNativePlacedLight>().Count() != 1 ||
                ModeledPoint(multiple.Root).GetMeta("opennv_nif_block").AsInt32() != 2)
                throw new InvalidOperationException("Additional unreferenced NIF points became extra record emitters.");
        }
        finally { multiple.Root.Free(); }
        RejectModeledLight(() => FalloutPlacedLightResolver.ResolveModeled(reference, basis,
            FalloutModeledLightSource.Read(model, "meshes/another/light.nif")));
        if (BitConverter.SingleToUInt32Bits(FalloutPlacedLightResolver.NormalizeModeledRecordColor(new byte[] { 80, 120, 160 })[0]) != 0x3ea0a0a1)
            throw new InvalidOperationException("Source light byte division was replaced by a rounded reciprocal.");
        foreach (var invalid in new[] { "missing", "effect", "ambient", "target", "layout", "root-motion" })
            RejectModeledLight(() => RuntimeNativeModeledLightBuilder.Build(reference, basis,
                FalloutNifFile.Read(ModeledLightFixture(invalid: invalid)), placement, units, 1, 0, false).Root.Free());
        foreach (var flags in new[] { 1U, 0x600U })
            RejectModeledLight(() => RuntimeNativeModeledLightBuilder.Build(reference,
                basis with { Light = basis.Light! with { Flags = flags } }, model, placement, units, 1, 0, false).Root.Free());
        GD.Print("OPENNV_MODELED_LIGHT_CONTRACTS_PASS actualDescendant=true rootReplacement=true scaleRadiusSplit=true " +
            "recordOverride=true sharedControllerRanges=true absoluteDiffuse=true zero=true independentInstances=true " +
            "selectedClockCold=true automaticObjectClockCold=true orderedFirstPoint=true rootMotionRefused=true dynamicRefused=true spotRefused=true pixels=unverified");
    }

    private static RuntimeNativePlacedLight ModeledPoint(Node3D root) => root.FindChildren("*", "", true, false)
        .OfType<RuntimeNativePlacedLight>().Single();
    private static RuntimeNifControllerPlayer ModeledPlayer(Node3D root) => root.FindChildren("*", "", true, false)
        .OfType<RuntimeNifControllerPlayer>().Single();
    private static void RejectModeledLight(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException) { return; }
        throw new InvalidOperationException("An unowned modeled-light source or continuation was admitted.");
    }

    private static byte[] ModeledLightFixture(bool animated = true, bool automatic = false, string invalid = "")
    {
        string[] names = ["Root", "Ancestor", "Emitter", "Forward", "Backward", "NiLightColorController", "Diffuse", "NiTransformController", "start", "end"];
        void Av(BinaryWriter w, int name, int controller, float[] translation, bool exported = false)
        {
            w.Write(name); w.Write(0); w.Write(controller); w.Write(14U);
            foreach (var value in translation) w.Write(value);
            foreach (var value in exported ? new[] { 0f, -1, 0, 1, 0, 0, 0, 0, 1 } : new[] { 1f, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(value);
            w.Write(exported ? 3f : 1f); w.Write(0); w.Write(-1);
        }
        void Time(BinaryWriter w, int next, ushort flags, int target, bool fixedRange = false)
        { w.Write(next); w.Write(flags); w.Write(1f); w.Write(0f); w.Write(fixedRange ? 0f : float.MaxValue); w.Write(fixedRange ? 9f : float.MinValue); w.Write(target); }
        byte[] Node(int name, int controller, int[] children, bool exported = false) => Bytes(w =>
        {
            Av(w, name, controller, exported ? [100, 200, 300] : [30, -5, 7], exported);
            w.Write(children.Length); foreach (var child in children) w.Write(child);
            w.Write(invalid == "effect" && name == 1 ? 1 : 0); if (invalid == "effect" && name == 1) w.Write(2);
        });
        byte[] Point(int controller) => Bytes(w =>
        {
            Av(w, 2, controller, [5, 3, 1]); w.Write(true); w.Write(0); w.Write(9f);
            foreach (var value in new[] { 0f, 0, 0, .9f, .8f, .7f, .2f, .3f, .4f, 1, 0, 0 }) w.Write(value);
        });
        byte[] Sequence(int name, int interpolator, int keys, float stop, uint cycle) => Bytes(w =>
        {
            w.Write(name); w.Write(2); w.Write(0);
            w.Write(interpolator); w.Write(7); w.Write((byte)0); w.Write(2); w.Write(-1); w.Write(5); w.Write(6); w.Write(-1);
            w.Write(13); w.Write(4); w.Write((byte)0); w.Write(invalid == "root-motion" ? 0 : 1); w.Write(-1); w.Write(7); w.Write(-1); w.Write(-1);
            w.Write(1f); w.Write(keys); w.Write(cycle); w.Write(1f); w.Write(0f); w.Write(stop); w.Write(3); w.Write(0); w.Write((ushort)0);
        });
        byte[] VectorKeys(float stop, bool reverse) => Bytes(w =>
        {
            w.Write(2); w.Write(2U);
            foreach (var time in new[] { 0f, stop })
            {
                var on = reverse ? time == 0 : time != 0;
                w.Write(time); w.Write(on ? 1f : 0); w.Write(on ? .5f : 0); w.Write(on ? .25f : 0);
                for (var i = 0; i < 6; i++) w.Write(0f);
            }
        });
        byte[] Keys(float stop) => Bytes(w => { w.Write(-1); w.Write(2); w.Write(0f); w.Write(8); w.Write(stop); w.Write(9); });
        var blocks = new List<(string Type, byte[] Data)>
        {
            ("NiNode", Node(0, animated ? 3 : -1, [1], true)),
            ("NiNode", Node(1, -1, invalid == "missing" ? [] : invalid == "duplicate" ? [2, 18] : [2])),
            ("NiPointLight", Point(animated ? 7 : -1)),
            ("NiControllerManager", Bytes(w => { Time(w, 4, 0x004c, 0); w.Write(false); w.Write(2); w.Write(5); w.Write(8); w.Write(6); })),
            ("NiMultiTargetTransformController", Bytes(w => { Time(w, -1, 0x006c, 0); w.Write((ushort)1); w.Write(invalid == "root-motion" ? 0 : 1); })),
            ("NiControllerSequence", Sequence(3, 9, 16, 1, automatic ? 0U : 2U)),
            ("NiDefaultAVObjectPalette", Bytes(w => { w.Write(0); w.Write(2); foreach (var index in invalid == "root-motion" ? new[] { 0, 2 } : new[] { 1, 2 }) { w.Write(names[index].Length); w.Write(Encoding.ASCII.GetBytes(names[index])); w.Write(index); } })),
            ("NiLightColorController", Bytes(w => { Time(w, -1, 0x006c, invalid == "target" ? 1 : 2, true); w.Write(10); w.Write((ushort)(invalid == "ambient" ? 1 : 0)); if (invalid == "layout") w.Write((byte)0); })),
            ("NiControllerSequence", Sequence(4, 11, 17, 2, 2)),
            ("NiPoint3Interpolator", Bytes(w => { for (var i = 0; i < 3; i++) w.Write(float.MinValue); w.Write(14); })),
            ("NiBlendPoint3Interpolator", Bytes(w => { w.Write((byte)1); w.Write((byte)2); w.Write(0f); for (var i = 0; i < 3; i++) w.Write(float.MinValue); })),
            ("NiPoint3Interpolator", Bytes(w => { for (var i = 0; i < 3; i++) w.Write(float.MinValue); w.Write(12); })),
            ("NiPosData", VectorKeys(2, true)),
            ("NiTransformInterpolator", Bytes(w => { for (var i = 0; i < 3; i++) w.Write(float.MinValue); w.Write(1f); for (var i = 0; i < 3; i++) w.Write(0f); w.Write(1f); w.Write(15); })),
            ("NiPosData", VectorKeys(1, false)),
            ("NiTransformData", Bytes(w => { w.Write(0); w.Write(2); w.Write(1U); foreach (var time in new[] { 0f, 2 }) { w.Write(time); w.Write(30 + 2 * time); w.Write(-5f); w.Write(7f); } w.Write(0); })),
            ("NiTextKeyExtraData", Keys(1)), ("NiTextKeyExtraData", Keys(2)),
        };
        if (invalid == "duplicate") blocks.Add(("NiPointLight", Point(-1)));
        return Bytes(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Count); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); w.Write((ushort)blocks.Count);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(names.Length); w.Write(names.Max(name => name.Length)); foreach (var name in names) { w.Write(name.Length); w.Write(Encoding.ASCII.GetBytes(name)); }
            w.Write(0); foreach (var block in blocks) w.Write(block.Data); w.Write(1); w.Write(0);
        });
    }
}
