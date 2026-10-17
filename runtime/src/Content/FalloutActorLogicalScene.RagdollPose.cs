using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRagdollTemplatePoseStore(FalloutNifSceneKey Target, int Template,
    FalloutNifSceneTransform Before, FalloutNifSceneTransform Applied, long Changed);
internal sealed record FalloutRagdollTemplatePoseCall(FalloutReferenceModelDataInvocation Load,
    string TemplateSha256, long Entered, long Changed, IReadOnlyList<FalloutRagdollTemplatePoseStore> Stores,
    bool Applied, bool Restored, string? Failure);

internal sealed partial class FalloutActorLogicalScene
{
    private FalloutRagdollTemplatePoseCall? _ragdollTemplatePose;
    private string? RagdollTemplatePoseBoundary => _ragdollTemplatePose is { Restored: false } ?
        "source-ragdoll-template-pose-remains-entered" : null;

    // The source walks the original template, looks each name up in the current
    // model, and copies rotation/translation only. NonAccum uses its template
    // parent's local frame. Neither this copy nor its inverse writes scale.
    internal void ApplyRagdollTemplatePose(FalloutReferenceModelDataInvocation load, FalloutNifPhysicalModel template,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load);
        if (_ragdollTemplatePose is not null || template.Source.Sha256 != SkeletonSha256)
            throw new InvalidOperationException("Ragdoll template pose changed or replayed its actual selected source model.");
        var fault = _reentry; var stores = new List<FalloutRagdollTemplatePoseStore>();
        _ragdollTemplatePose = new(load, template.Source.Sha256, Next(), _sequence, stores.AsReadOnly(), false, false, null);
        _busy = true;
        try
        {
            foreach (var row in template.Scene)
            {
                Check();
                var (name, local) = TemplatePose(row.Declaration);
                var target = _order.FirstOrDefault(node => node.Name == name);
                if (target is null) continue;
                if (name == FalloutPlayerModelLoadContinuationSource.NonAccumulationNode)
                    local = row.Parent is { } parent ? TemplatePose(template.Source.ReadObject(parent)).Pose :
                        throw new InvalidDataException("Ragdoll NonAccum template has no actual parent frame.");
                var before = FalloutNifSceneTransform.From(target.Local);
                target.Local = FalloutNifSceneMath.Copy(local with { Scale = target.Local.Scale });
                target.PoseStores = checked(target.PoseStores + 1);
                stores.Add(new(target.Key, row.Declaration.Block.Index, before, FalloutNifSceneTransform.From(target.Local), Next()));
                _ragdollTemplatePose = _ragdollTemplatePose with { Changed = Next() }; Check();
            }
            _ragdollTemplatePose = _ragdollTemplatePose with { Applied = true, Changed = Next() };
        }
        catch (Exception error)
        { _failure ??= error.Message; _ragdollTemplatePose = _ragdollTemplatePose with { Failure = error.Message, Changed = Next() }; throw; }
        finally { _busy = false; }
        void Check()
        {
            requireActual(load);
            if (_reentry != fault || _retired || load.ThreadId != _thread)
                throw new InvalidOperationException("Ragdoll template pose lost its actual current source thread or swallowed reentry.");
        }
    }

    internal void RestoreRagdollTemplatePose(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(allowFailure: true); requireActual(load);
        if (_failure is not null || _ragdollTemplatePose is not { Applied: true, Restored: false, Failure: null } call || call.Load != load)
            throw new InvalidOperationException("Ragdoll pose restore has no same returned temporary-pose child.");
        var fault = _reentry; _busy = true;
        try
        {
            foreach (var store in call.Stores)
            {
                requireActual(load);
                if (_reentry != fault) throw new InvalidOperationException("Ragdoll pose restore swallowed source reentry.");
                var target = _objects[store.Target];
                target.Local = FalloutNifSceneMath.Copy(store.Before.Read() with { Scale = target.Local.Scale });
                target.PoseStores = checked(target.PoseStores + 1); Next();
            }
            _ragdollTemplatePose = call with { Restored = true, Changed = Next() };
        }
        catch (Exception error)
        { _failure ??= error.Message; _ragdollTemplatePose = call with { Failure = error.Message, Changed = Next() }; throw; }
        finally { _busy = false; }
    }

    private static (string Name, FalloutNifTransform Pose) TemplatePose(FalloutNifObject value) => value switch
    {
        FalloutNifNode node => (node.Name, node.Transform),
        FalloutNifGeometry geometry => (geometry.Name, geometry.Transform),
        _ => throw new NotSupportedException("source-ragdoll-template-AVObject-pose-class-unowned:" + value.Block.TypeName)
    };

    private void RestoreRagdollTemplatePoseFields(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.RagdollTemplatePose is not { } call) return;
        if (call.TemplateSha256 != SkeletonSha256 || call.Stores.Any(store =>
            !_objects.TryGetValue(store.Target, out var target) || store.Template < 0 || store.Template >= Asset(0).Blocks.Count ||
            TemplatePose(Asset(0).ReadObject(store.Template)).Name != target.Name))
            throw new InvalidDataException("Cold ragdoll pose changed its actual source template/name/target join.");
        _ragdollTemplatePose = call with { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
    }

    private static void ValidateRagdollTemplatePose(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.RagdollTemplatePose is not { } call) return;
        if (call.Load.Call == Guid.Empty || call.TemplateSha256 != saved.Assets[0].Sha256 || call.Entered < 1 ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || call.Stores is null ||
            call.Restored && !call.Applied || call.Failure is not null && call.Failure != saved.Failure ||
            !call.Applied && call.Failure is null || call.Stores.Any(store => store.Before is null || store.Applied is null ||
                store.Template < 0 || store.Changed <= call.Entered || store.Changed >= call.Changed ||
                !saved.Objects.Any(node => node.Key == store.Target) || store.Before.Scale != store.Applied.Scale))
            throw new InvalidDataException("Cold ragdoll template pose lost its actual ordered temporary stores.");
        foreach (var store in call.Stores) { _ = store.Before.Read(); _ = store.Applied.Read(); }
    }
}
