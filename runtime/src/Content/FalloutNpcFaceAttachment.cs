using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal static class FalloutNpcFaceAttachment
{
    internal const string HeadBone = "Bip01 Head";
    // Head, hair, headband, hat and eyeglasses are head-mounted biped slots.
    internal static bool IsRigidHeadEquipment(uint slots) => slots != 0 && (slots & ~0x4603u) == 0;
    // Rigid biped head equipment uses the head-bone frame, distinct from the
    // FaceGen skin's inverse bind. Observed equipment maps model +Z to bone +X,
    // retains +Y and maps +X to -Z; its export root is replaced at attachment.
    internal static FalloutNifTransform RigidHeadEquipmentBind => new(new(0, 0, 0),
        [0, 0, 1, 0, 1, 0, -1, 0, 0], 1);

    internal static bool UsesHeadModelSpace(string role) => role is
        "mouth" or "teeth-lower" or "teeth-upper" or "tongue" or "eye-left" or "eye-right" or "hair" or "head-addon";

    // Rigid FaceGen vertices share the skinned head's model space. The actual
    // Prn target selects the head skin's inverse bind; the component's export
    // root rotation must not be composed a second time. This recovers the
    // source-skin binding contract from the first-party actor implementation.
    internal static IReadOnlyDictionary<string, FalloutNifTransform> ReadHeadBinds(FalloutNifFile head)
    {
        var shapes = head.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Select(block => head.ReadGeometry(block.Index)).Where(shape => shape.SkinInstance >= 0).ToArray();
        if (shapes.Length != 1)
            throw new NotSupportedException("Rigid FaceGen attachment requires a unique source skinned head geometry.");
        var instance = (FalloutNifSkinInstance)head.ReadObject(shapes[0].SkinInstance);
        var data = (FalloutNifSkinData)head.ReadObject(instance.Data);
        if (instance.Bones.Length != data.Bones.Length)
            throw new InvalidDataException("Source head skin has inconsistent inverse-bind identities.");
        var result = new Dictionary<string, FalloutNifTransform>(StringComparer.Ordinal);
        for (var index = 0; index < instance.Bones.Length; index++)
            if (!result.TryAdd(head.ReadNode(instance.Bones[index]).Name, data.Bones[index].SkinTransform))
                throw new InvalidDataException("Source head skin repeats an inverse-bind target.");
        return result;
    }
}
