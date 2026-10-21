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

    internal static bool IsStatic(FalloutNifFile source) => source.Blocks.All(block => block.TypeName is
        "NiNode" or "BSFadeNode" or "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape" or
        "NiTriShapeData" or "NiTriStripsData" or "NiSkinInstance" or "BSDismemberSkinInstance" or
        "NiSkinData" or "NiSkinPartition" or "NiMaterialProperty" or "NiTexturingProperty" or
        "NiSourceTexture" or "NiAlphaProperty" or "NiStencilProperty" or "NiVertexColorProperty" or
        "NiSpecularProperty" or "NiZBufferProperty" or "NiWireframeProperty" or "NiShadeProperty" or
        "NiDitherProperty" or "BSShaderPPLightingProperty" or "BSShaderTextureSet" or
        "BSLightingShaderProperty" or "BSShaderNoLightingProperty" or "BSShaderLightingProperty" or
        "NiStringExtraData" or "NiStringsExtraData" or "NiIntegerExtraData" or "NiBinaryExtraData" or "BSXFlags");

    internal void ValidateModel(FalloutNifFile source)
    {
        Validate();
        if (!Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved idle animation object differs from its winning NIF.");
        if (!IsStatic(source))
            throw new NotSupportedException("Idle animation object requires its independent motion or physics continuation.");
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
