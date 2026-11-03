using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseExternalControllerSlots()
    {
        var declaredBytes = ManagerControllerChainFixture("valid");
        var declared = FalloutNifFile.Read(declaredBytes);
        var absentBytes = (byte[])declaredBytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(absentBytes.AsSpan(declared.Blocks[14].Offset + 8), -1);
        var absent = FalloutNifFile.Read(absentBytes);
        var skeleton = new RuntimeNativeNifSkeleton(FalloutNifFile.Read(Synthetic(false)), 1);
        using var material = new StandardMaterial3D { AlbedoColor = new(.2f, .3f, .4f, .8f) };
        try
        {
            AddChild(skeleton.Node);
            const string node = "ActualMaterialGeometry";
            RuntimeNativeNifMaterialChannels Registry(FalloutNifFile model)
            {
                var channels = new RuntimeNativeNifMaterialChannels();
                channels.Add(node, model, model.ReadObject(14), [material]);
                return channels;
            }
            RuntimeNativeNifAnimation Clip(FalloutNifControllerLink link) => new(declared,
                new(declared.Blocks[8], "ExternalMaterialSlot", [link], 1, -1, 2, 1, 0, 1, -1, "ArbitraryNode", -1, null), skeleton);
            var link = new FalloutNifControllerLink(node, "NiMaterialProperty", "NiAlphaController", "", "", 17, -1, 0);
            skeleton.MaterialChannels.ReplaceWith(Registry(absent));
            var nullSlot = Clip(link);
            var before = material.AlbedoColor;
            nullSlot.ApplySourceTime(.5f);
            if (nullSlot.UnboundChannels.Count != 0 || nullSlot.AbsentSourceControllers.Count != 1 ||
                material.AlbedoColor != before)
                throw new InvalidDataException("Absent source material controller invented a binding or blocked its clip.");
            var fallback = Clip(link with { Controller = 15 });
            if (fallback.UnboundChannels.Count != 1 || fallback.AbsentSourceControllers.Count != 0)
                throw new InvalidDataException("Declared retained KF controller was silently treated as an absent source slot.");
            var malformed = Clip(link with { Interpolator = 0 });
            if (malformed.UnboundChannels.Count != 1 || malformed.AbsentSourceControllers.Count != 0)
                throw new InvalidDataException("Absent controller hid a non-interpolator source declaration.");
            var ambiguous = Registry(absent);
            var secondSource = FalloutNifFile.Read(absentBytes);
            ambiguous.Add(node, secondSource, secondSource.ReadObject(14), [material]);
            skeleton.MaterialChannels.ReplaceWith(ambiguous);
            var duplicate = Clip(link);
            if (duplicate.UnboundChannels.Count != 1 || duplicate.AbsentSourceControllers.Count != 0)
                throw new InvalidDataException("Ambiguous declared material entities were silently removed.");
            skeleton.MaterialChannels.ReplaceWith(Registry(declared));
            var present = Clip(link with { Variable2 = "UnqualifiedControllerId" });
            if (present.UnboundChannels.Count != 1 || present.AbsentSourceControllers.Count != 0)
                throw new InvalidDataException("Present unsupported controller identity was silently treated as absent.");
            var wrongType = Clip(link with { ControllerType = "BSMaterialEmittanceMultController" });
            if (wrongType.UnboundChannels.Count != 0 || wrongType.AbsentSourceControllers.Count != 1)
                throw new InvalidDataException("Unrelated source controller prevented the exact missing-controller disposition.");
            // The original null slot follows actual material replacement. A new
            // declared controller must be attempted and retain its real runtime
            // material refusal; it must not remain a cached permanent no-op.
            try { nullSlot.ApplySourceTime(.5f); throw new InvalidDataException("Replacement controller was never rebound."); }
            catch (NotSupportedException) { }
            var emissive = FalloutNifFile.Read(ManagerControllerChainFixture("emissive"));
            skeleton.MaterialChannels.ReplaceWith(Registry(emissive));
            var specularLink = link with { ControllerType = "NiMaterialColorController", Variable1 = "SPEC" };
            var missingSpecular = Clip(specularLink);
            missingSpecular.ApplySourceTime(.5f);
            if (missingSpecular.UnboundChannels.Count != 0 || missingSpecular.AbsentSourceControllers.Count != 1 ||
                material.AlbedoColor != before ||
                skeleton.MaterialChannels.HasAbsentSourceController(specularLink with { Variable1 = "SELF_ILLUM" }) ||
                skeleton.MaterialChannels.HasAbsentSourceController(specularLink with { Variable1 = "UnknownColorId" }))
                throw new InvalidDataException("Shared material controller type replaced its exact channel identity or hid an unknown identity.");
            GD.Print("OPENNV_EXTERNAL_CONTROLLER_SLOT_PASS missingController=true noInventedMaterial=true retainedFallbackRefused=true malformedInterpolatorRefused=true ambiguousRefused=true declaredUnsupportedRefused=true replacementRebind=true");
        }
        finally { skeleton.Node.Free(); }
    }
}
