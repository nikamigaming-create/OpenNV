using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

// Static ANIO geometry is rebuilt from its winning source. Its parent bone and
// any externally driven channels belong to the retained actor/KF pose. An
// independently ticking or physical object needs a separate continuation.
internal sealed record FalloutActorIdleObjectBinding(FalloutFormKey Form, string RecordSha256,
    string Resource, string Sha256)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Form) ||
            !FalloutActorFurnitureContinuation.ValidHash(RecordSha256) ||
            !FalloutActorFurnitureContinuation.ValidHash(Sha256) || string.IsNullOrWhiteSpace(Resource))
            throw new InvalidDataException("Saved idle animation object has invalid source ownership.");
    }

    private static bool StaticBlock(string type) => type is
        "NiNode" or "BSFadeNode" or "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape" or
        "NiTriShapeData" or "NiTriStripsData" or "NiSkinInstance" or "BSDismemberSkinInstance" or
        "NiSkinData" or "NiSkinPartition" or "NiMaterialProperty" or "NiTexturingProperty" or
        "NiSourceTexture" or "NiAlphaProperty" or "NiStencilProperty" or "NiVertexColorProperty" or
        "NiSpecularProperty" or "NiZBufferProperty" or "NiWireframeProperty" or "NiShadeProperty" or
        "NiDitherProperty" or "BSShaderPPLightingProperty" or "BSShaderTextureSet" or
        "BSLightingShaderProperty" or "BSShaderNoLightingProperty" or "BSShaderLightingProperty" or
        "NiStringExtraData" or "NiStringsExtraData" or "NiIntegerExtraData" or "NiBinaryExtraData" or "BSXFlags";

    internal static bool StaticTransformClock(FalloutNifTimeController time) =>
        time.Flags == 0x4c && time.Frequency == 1 && time.Phase == 0 &&
        time.StartTime == 0 && time.StopTime == 0 && time.NextController == -1 && time.UnknownInteger == 0;

    private static IReadOnlyList<FalloutNifBlock> UnownedBlocks(FalloutNifFile source)
    {
        var staticControllers = new HashSet<int>();
        foreach (var block in source.Blocks.Where(block => block.TypeName == "NiTransformController"))
        {
            var controller = (FalloutNifTransformController)source.ReadObject(block.Index);
            var time = controller.Time;
            if (!StaticTransformClock(time) ||
                time.Target < 0 || time.Target >= source.Blocks.Count || controller.Interpolator < 0 ||
                controller.Interpolator >= source.Blocks.Count || source.ReadObject(time.Target) is not FalloutNifNode node ||
                node.Controller != block.Index || source.ReadObject(controller.Interpolator) is not FalloutNifTransformInterpolator interpolator)
                continue;
            if (interpolator.Data < 0 || interpolator.Data >= source.Blocks.Count ||
                source.ReadObject(interpolator.Data) is not FalloutNifTransformData) continue;
            // The mesh builder applies the first source key for this zero-time
            // controller. It does not acquire an independent ticking clock.
            staticControllers.Add(block.Index);
            staticControllers.Add(controller.Interpolator);
            staticControllers.Add(interpolator.Data);
        }
        return source.Blocks.Where(block => !StaticBlock(block.TypeName) && !staticControllers.Contains(block.Index)).ToArray();
    }

    internal static bool IsStatic(FalloutNifFile source) => UnownedBlocks(source).Count == 0;

    internal void ValidateModel(FalloutNifFile source)
    {
        Validate();
        if (!Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved idle animation object differs from its winning NIF.");
        var unowned = UnownedBlocks(source);
        if (unowned.Count != 0)
            throw new NotSupportedException($"Idle animation object {Form} ({Resource}) requires its independent motion or physics continuation; " +
                $"unowned blocks: {string.Join(", ", unowned.Select(block => $"{block.Index}:{block.TypeName}"))}.");
    }

    internal static void ValidateSources(FalloutPluginStack records, FalloutActorIdleSource source,
        IReadOnlyList<FalloutActorIdleObjectBinding>? saved)
    {
        var bindings = saved ?? [];
        if (bindings.Count != source.Objects.Count || bindings.Any(value => value is null) ||
            bindings.Select(value => value.Form).Distinct().Count() != bindings.Count)
            throw new InvalidDataException("Saved idle animation objects differ from the winning attachment set.");
        for (var index = 0; index < bindings.Count; ++index)
        {
            var binding = bindings[index]; var expected = source.Objects[index];
            binding.Validate();
            var record = records.GetEffective(expected.Form);
            if (binding.Form != expected.Form || record.Signature != "ANIO" ||
                FalloutActorFurnitureContinuation.RecordHash(record) != binding.RecordSha256 ||
                !binding.Resource.Equals(expected.ModelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved idle animation object differs from its winning ANIO declaration.");
        }
    }
}
