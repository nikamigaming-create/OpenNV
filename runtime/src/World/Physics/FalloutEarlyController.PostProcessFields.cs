using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal enum FalloutControllerPostProcessField { FirstBody, SecondBody, Object }
internal enum FalloutControllerPlayerSourceScope { PostProcessContext, PostActions }
internal sealed record FalloutControllerPostProcessFields(Guid? FirstBody, Guid? SecondBody, Guid? Object,
    string? FirstBodyBoundary = null, string? SecondBodyBoundary = null, string? ObjectBoundary = null)
{
    internal static FalloutControllerPostProcessFields Constructed => new(null, null, null);
}
internal sealed record FalloutControllerPostProcessInvocation(Guid Main, long MainOrdinal, Guid PlayerProcess,
    Guid RuntimeProcess, Guid Constructor, int ThreadId, FalloutProcessSceneContextQuery Context, int QueryOrdinal,
    FalloutControllerPlayerSourceScope SourceScope = FalloutControllerPlayerSourceScope.PostProcessContext);
internal sealed record FalloutControllerPostProcessRead(FalloutControllerPostProcessInvocation Invocation,
    FalloutEarlyControllerLineage Lineage, Guid Controller, FalloutControllerPostProcessField Field,
    Guid? Value, bool Returned, string? Failure);
internal sealed record FalloutControllerPostProcessFlagsRead(FalloutControllerPostProcessInvocation Invocation,
    FalloutEarlyControllerLineage Lineage, Guid Controller, uint? Flags, long Revision, bool Returned, string? Failure);

internal sealed partial class FalloutEarlyController
{
    private FalloutControllerPostProcessFields _postProcessFields = FalloutControllerPostProcessFields.Constructed;
    private bool _readingPostProcess;
    private long _postProcessReadFault;

    internal FalloutControllerPostProcessFields CapturePostProcessFields() => _postProcessFields;
    private string? PostProcessFieldSaveBlocker => _postProcessFields.FirstBodyBoundary ??
        _postProcessFields.SecondBodyBoundary ?? _postProcessFields.ObjectBoundary;

    internal FalloutControllerPostProcessRead ReadPostProcessField(FalloutControllerPostProcessInvocation invocation,
        FalloutControllerPostProcessField field, Action<FalloutControllerPostProcessInvocation> requireActual,
        Action<FalloutControllerPostProcessRead> observe)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(observe);
        if (_readingPostProcess) { ++_postProcessReadFault; throw new InvalidOperationException("Controller context field getter reentered."); }
        if (!Enum.IsDefined(field) || (invocation.SourceScope, invocation.QueryOrdinal, field) is not
            ((FalloutControllerPlayerSourceScope.PostProcessContext, 0, FalloutControllerPostProcessField.FirstBody or FalloutControllerPostProcessField.SecondBody) or
             (FalloutControllerPlayerSourceScope.PostProcessContext, 1, FalloutControllerPostProcessField.Object) or
             (FalloutControllerPlayerSourceScope.PostActions, 0, FalloutControllerPostProcessField.FirstBody)))
            throw new InvalidDataException("Controller context getter selected no original field in its source scope.");
        var call = new FalloutControllerPostProcessRead(invocation, Lineage, Identity, field, null, false, null);
        var fault = _postProcessReadFault; var sequence = _sequence; _readingPostProcess = true;
        try
        {
            Check(); observe(call); Check();
            var (value, boundary) = field switch
            {
                FalloutControllerPostProcessField.FirstBody => (_postProcessFields.FirstBody, _postProcessFields.FirstBodyBoundary),
                FalloutControllerPostProcessField.SecondBody => (_postProcessFields.SecondBody, _postProcessFields.SecondBodyBoundary),
                FalloutControllerPostProcessField.Object => (_postProcessFields.Object, _postProcessFields.ObjectBoundary),
                _ => throw new InvalidDataException("Controller context selected an invalid field."),
            };
            if (boundary is not null) throw new NotSupportedException(boundary);
            // A source constructor-null body/object field has its own owner.
            // It is independent from this genuine nonnull controller, its
            // separate base proxy pointer and any native presentation node.
            call = call with { Value = value, Returned = true }; observe(call); Check(); return call;
        }
        catch (Exception error)
        {
            call = call with { Returned = false, Failure = error.Message }; observe(call); throw;
        }
        finally { _readingPostProcess = false; }

        void Check() => RequirePostProcessInvocation(invocation, fault, sequence, requireActual);
    }

    internal void ObserveUnownedPostProcessFieldWriter(FalloutControllerPostProcessField field, string boundary)
    {
        RequireCurrent(); ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        if (_readingPostProcess) { ++_postProcessReadFault; throw new InvalidOperationException("Controller field writer entered its current getter."); }
        _postProcessFields = field switch
        {
            FalloutControllerPostProcessField.FirstBody => _postProcessFields with { FirstBodyBoundary = boundary },
            FalloutControllerPostProcessField.SecondBody => _postProcessFields with { SecondBodyBoundary = boundary },
            FalloutControllerPostProcessField.Object => _postProcessFields with { ObjectBoundary = boundary },
            _ => throw new InvalidDataException("Controller context writer selected no original field."),
        };
    }

    internal static void ValidatePostProcessFields(FalloutControllerPostProcessFields fields)
    {
        if (fields is null || fields.FirstBody is not null || fields.SecondBody is not null || fields.Object is not null ||
            fields.FirstBodyBoundary is { } first && string.IsNullOrWhiteSpace(first) ||
            fields.SecondBodyBoundary is { } second && string.IsNullOrWhiteSpace(second) ||
            fields.ObjectBoundary is { } other && string.IsNullOrWhiteSpace(other))
            throw new InvalidDataException("Controller context fields lost their actual nullable constructor or preceding-writer history.");
    }

    internal static void RequireCompletePostProcessFields(FalloutControllerPostProcessFields fields)
    {
        ValidatePostProcessFields(fields);
        if (fields.FirstBodyBoundary is not null || fields.SecondBodyBoundary is not null || fields.ObjectBoundary is not null)
            throw new NotSupportedException("Complete controller state has an unowned body/object field writer.");
    }
}
