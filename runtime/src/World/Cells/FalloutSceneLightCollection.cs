using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutSceneLightContribution(Guid Registration, Guid Wrapper, FalloutFormKey Reference,
    FalloutDetectionLightInput? Fields, uint? Contribution, long Stores, bool Skipped = false);
internal sealed record FalloutScenePointLightSum(Guid Scene, long Revision, IReadOnlyList<FalloutSceneLightContribution> Lights, uint SumBits,
    bool Returned = false, string? Failure = null);

// This is the current source scene's independent light list. Membership is
// acquired by real light construction/attachment and released by retirement.
internal sealed class FalloutSceneLightCollection
{
    private readonly LinkedList<(Guid Registration, FalloutSourcePointLight Light)> _lights = [];
    private int? _thread;
    private object? _publication;
    private string? _failure;
    private long _revision;
    private bool _reading;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutScenePointLightSum? LastSum { get; private set; }
    internal int Count => _lights.Count;
    internal object State => new { Identity, count = Count, published = _publication is not null, failure = _failure, last = LastSum };
    internal string? SaveBlocker => _failure ?? (_lights.Count == 0 ? null : "source-scene-light-complete-cold-binding-unowned");
    internal void PublishThread(object actualNativeRoot)
    {
        ArgumentNullException.ThrowIfNull(actualNativeRoot);
        if (_publication is not null)
        {
            if (!ReferenceEquals(_publication, actualNativeRoot) || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Scene light publication changed its real root or calling thread.");
            return;
        }
        if (_lights.Count != 0 || _reading) throw new InvalidOperationException("Entered scene light state cannot change its publication thread.");
        _publication = actualNativeRoot; _thread = Environment.CurrentManagedThreadId;
    }
    internal void ObserveAttachmentFailure(Exception error) => _failure ??= "source-scene-light-attachment-failed:" + error.Message;
    internal IDisposable Attach(FalloutSourcePointLight light, Func<FalloutReferencePlacement> placement, Func<bool> enabled)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(light);
        if (_lights.Any(entry => ReferenceEquals(entry.Light, light)))
            throw new InvalidOperationException("Scene light wrapper was registered twice.");
        var registration = Guid.NewGuid(); light.Bind(registration, placement, enabled);
        var node = _lights.AddLast((registration, light)); _revision = checked(_revision + 1);
        return new Membership(this, node);
    }
    internal FalloutScenePointLightSum Sum(float[] sourcePosition)
    {
        RequireIdle();
        if (sourcePosition.Length != 3 || sourcePosition.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Scene lighting needs the current finite source sample position.");
        _reading = true;
        LastSum = new(Identity, _revision, [], 0);
        try
        {
            var rows = new List<FalloutSceneLightContribution>(); var sum = 0f;
            foreach (var (registration, light) in _lights)
            {
                if (!light.ReadEnabled(registration))
                {
                    rows.Add(new(registration, light.Identity, light.Reference, null, null, light.ContributionStores, true));
                    LastSum = new(Identity, _revision, rows.ToArray(), BitConverter.SingleToUInt32Bits(sum));
                    continue;
                }
                var fields = light.Read(registration); uint? bits = null;
                if (!fields.Disabled && !fields.Excluded)
                {
                    var contribution = FalloutDetectionLight.Contribution(fields, sourcePosition);
                    light.StoreContribution(registration, contribution); bits = BitConverter.SingleToUInt32Bits(contribution);
                    sum = (float)((double)sum + contribution);
                }
                rows.Add(new(registration, light.Identity, light.Reference, fields, bits, light.ContributionStores));
                LastSum = new(Identity, _revision, rows.ToArray(), BitConverter.SingleToUInt32Bits(sum));
            }
            LastSum = LastSum with { Returned = true }; return LastSum;
        }
        catch (Exception error) { LastSum = LastSum! with { Failure = error.Message }; throw; }
        finally { _reading = false; }
    }
    private void Detach(LinkedListNode<(Guid Registration, FalloutSourcePointLight Light)> node)
    {
        RequireIdle(allowFailure: true);
        if (node.List != _lights) throw new InvalidOperationException("Source light retired a different scene list node.");
        node.Value.Light.Unbind(node.Value.Registration); _lights.Remove(node); _revision = checked(_revision + 1);
    }
    private void RequireIdle(bool allowFailure = false)
    {
        if (_publication is null || _thread != Environment.CurrentManagedThreadId || _reading)
            throw new InvalidOperationException("Source scene light membership crossed its current idle thread.");
        if (!allowFailure && _failure is not null) throw new NotSupportedException(_failure);
    }
    private sealed class Membership(FalloutSceneLightCollection scene,
        LinkedListNode<(Guid Registration, FalloutSourcePointLight Light)> node) : IDisposable
    {
        private bool _retired;
        public void Dispose() { if (_retired) return; scene.Detach(node); _retired = true; }
    }
}
