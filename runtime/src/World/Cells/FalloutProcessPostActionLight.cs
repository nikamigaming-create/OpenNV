using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutSceneDirectionalFields(FalloutFormKey Cell, float[] Ambient, float[] Diffuse, float Dimmer);
internal sealed record FalloutProcessPostActionLightCall(FalloutProcessSceneQuery Query,
    FalloutSourceSharedShaderIdentity Shader, string Step, bool FirstGetterReturned = false, Guid? FirstLight = null,
    bool ModelRead = false, FalloutSourceSceneNode? FirstModel = null, bool SecondGetterReturned = false, Guid? SecondLight = null,
    FalloutReferencePositionRead? Position = null, FalloutDetectionBounds? MaximumBounds = null,
    FalloutDetectionBounds? MinimumBounds = null, float[]? SamplePosition = null,
    FalloutScenePointLightSum? Points = null, FalloutSceneDirectionalFields? Directional = null,
    FalloutReferenceParentCellRead? Parent = null, uint? ValueBits = null, bool Returned = false, string? Failure = null);

internal sealed partial class FalloutActorProcessCommonState
{
    private FalloutProcessSceneQuery? _postActionLightBorrow;
    internal FalloutProcessPostActionLightCall? LastPostActionLight { get; private set; }
    internal FalloutProcessSceneQuery ReadPostActionLightBorrow(FalloutFormKey actor, long epoch)
    {
        var current = Require(actor);
        if (!_busy || _postActionLightBorrow is not { } query || query.Actor.Reference != actor || query.Epoch != epoch ||
            current.Source != query.Actor || current.Epoch != epoch || current.Level != query.Level || query.Process != _process)
            throw new InvalidOperationException("Light reference getter left its actual entered Common process borrow.");
        return query;
    }
    private FalloutProcessPostActionLightCall ReadPostActionLight(FalloutProcessSceneQuery query,
        Func<FalloutProcessPostActionLightCall, FalloutSourceSharedShaderProperty,
            Action<FalloutProcessPostActionLightCall>, FalloutProcessPostActionLightCall>? read)
    {
        if (!_loadedShaders.TryGetValue(query.Actor.Reference, out var lease) ||
            Require(query.Actor.Reference).SceneFields!.PostActionContext != lease.Resource.Source.Identity)
            throw new NotSupportedException("source-process-post-action-current-shared-shader-lease-unbound");
        var shader = lease.Resource;
        LastPostActionLight = new(query, shader.Source, "shader-first-light-getter");
        try
        {
            var first = shader.ReadFirstLight();
            LastPostActionLight = LastPostActionLight with { FirstGetterReturned = true, FirstLight = first, Step = "spatial-consumers" };
            if (_postActionLightBorrow is not null) throw new InvalidOperationException("Post-action light sampling reentered its current borrow.");
            _postActionLightBorrow = query;
            var result = Callback(() => (read ?? throw new NotSupportedException("source-process-post-action-current-scene-light-consumers-unbound"))(
                LastPostActionLight, shader, call =>
                {
                    if (call.Query != query || call.Shader != shader.Source)
                        throw new InvalidOperationException("Post-action light publication changed its actual current shader/process.");
                    LastPostActionLight = call;
                }));
            if (!result.Returned || result.Failure is not null || result.ValueBits is null || result.Query != query || result.Shader != shader.Source)
                throw new InvalidDataException("Post-action light did not return its actual current source fields.");
            LastPostActionLight = result; return result;
        }
        catch (Exception error)
        { LastPostActionLight = LastPostActionLight! with { Failure = error.Message }; throw; }
        finally { _postActionLightBorrow = null; }
    }
}
