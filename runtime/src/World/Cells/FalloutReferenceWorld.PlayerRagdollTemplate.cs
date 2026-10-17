using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerRagdollTemplateRead(FalloutReferenceModelDataInvocation Load,
    FalloutReferenceControllerExtraRead Override, FalloutFormKey CurrentBase, FalloutFormKey ModelOwner,
    string Path, string Sha256, string DeclaredPath);
internal sealed record FalloutPlayerRagdollModelKey(FalloutReferenceModelDataInvocation Load,
    FalloutPlayerRagdollTemplateRead Template, uint ReferenceScale, uint BaseHeight, FalloutModelKeyFistpCall Conversion, string Value);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutNifPhysicalModel ReadPlayerRagdollTemplate(FalloutReferenceModelDataInvocation load, FalloutPlayerReturnedModel model)
    {
        RequireStandalonePlayerModelLoadPhysics(load, model);
        var extra = _sourceReferenceScriptInitializer!.ReadControllerConstructorExtra(
            FalloutPlayerModelLoadContinuationSource.ControllerModelOverrideExtra, load, RequireActualPlayerModelLoad);
        var current = ReadPlayerModelCurrentBase(load);
        if (current.Record.Signature != "NPC_")
            throw new NotSupportedException("source-ragdoll-current-non-NPC-model-component-getter-unowned");
        var owner = FalloutActorTemplateOwner.Resolve(records, current.Record, 64);
        var paths = owner.ReadSubrecords().Where(field => field.Signature == "MODL").ToArray();
        if (paths is not [{ Data.IsEmpty: false }])
            throw new NotSupportedException("source-ragdoll-model-component-null-or-empty-path-fallback-unowned");
        var declaredPath = FalloutDialogueTopic.Text(paths[0].Data.Span);
        var path = declaredPath.Replace('\\', '/');
        if (!path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) path = "meshes/" + path;
        if (owner.FormKey != model.Source.Appearance.ModelOwner ||
            !path.Equals(model.Source.Appearance.SkeletonPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("source-ragdoll-current-model-component-and-selected-skeleton-rebinding-unowned");
        // Reuse the immutable original decode. Current animation and logical
        // pose fields never become the template's source declarations.
        var template = model.Source.Skeleton;
        _playerModelControllerCall = _playerModelControllerCall! with
        { Template = new(load, extra, current.Record.FormKey, owner.FormKey, path, template.Source.Sha256, declaredPath) };
        RequireStandalonePlayerModelLoadPhysics(load, model); return template;
    }

    private FalloutPlayerRagdollModelKey ReadPlayerRagdollModelKey(FalloutReferenceModelDataInvocation load, FalloutPlayerReturnedModel model)
    {
        _ = ReadPlayerRagdollTemplate(load, model);
        var selected = _playerModelControllerCall!.Template!;
        var current = ReadPlayerModelCurrentBase(load);
        if (current.Record.FormKey != ActorBase(_enginePlayer))
            throw new NotSupportedException("source-ragdoll-current-Base-height-rebinding-unowned");
        var referenceScale = ReadPlayerAnimationLoadScale(load);
        var baseHeight = ActorHeight(_enginePlayer);
        var effective = World.Physics.FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(referenceScale), baseHeight);
        var converted = ProcessRuntime.ConvertPlayerModelScaleKey(load, BitConverter.SingleToUInt32Bits(effective), RequireActualPlayerModelLoad);
        var value = selected.DeclaredPath + converted.Integer!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (System.Text.Encoding.UTF8.GetByteCount(value) >= 260)
            throw new NotSupportedException("source-ragdoll-model-key-bounded-format-overflow-unowned");
        return new(load, selected, referenceScale, BitConverter.SingleToUInt32Bits(baseHeight), converted, value);
    }
}
