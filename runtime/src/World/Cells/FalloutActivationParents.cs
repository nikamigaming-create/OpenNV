using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActivationParent(FalloutFormKey Parent, float DelaySeconds);
internal sealed record FalloutActivationParentIssue(FalloutFormKey Child, FalloutFormKey Parent, string Error);
internal sealed record FalloutActivationRelayChild(FalloutFormKey Reference, string SourceSha256,
    float DelaySeconds, FalloutFormKey ActionReference, long Revision, bool Due);
internal sealed record FalloutActivationRelaySnapshot(string SourceSha256, float ElapsedSeconds, long Revision,
    IReadOnlyList<FalloutActivationRelayChild> Children)
{
    internal void Validate()
    {
        static bool Key(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (!Hash(SourceSha256) || !float.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 || Revision <= 0 ||
            Children is null || Children.Any(child => child is null || !Key(child.Reference) || !Key(child.ActionReference) ||
                !Hash(child.SourceSha256) || !float.IsFinite(child.DelaySeconds) || child.Revision <= 0 || child.Revision > Revision) || Children.Select(child => child.Reference).Distinct().Count() != Children.Count)
            throw new InvalidDataException("Saved activation-parent cycle has invalid source identities, thresholds or delivery receipts.");
    }
    internal FalloutActivationRelaySnapshot Copy() => this with { Children = Children.ToArray() };
}
internal sealed record FalloutActivationRelayDelivery(FalloutFormKey Parent, FalloutActivationRelayChild Child);

// Source XAPR belongs to the child. Its reverse graph and elapsed cycle are
// gameplay state, independent of the parent's model/controller lifetime.
internal static class FalloutActivationParents
{
    internal static IReadOnlyList<FalloutActivationParent> Read(FalloutPluginRecord reference)
    {
        var parents = new List<FalloutActivationParent>();
        foreach (var field in reference.ReadSubrecords().Where(field => field.Signature == "XAPR"))
        {
            if (field.Data.Length != 8) throw new InvalidDataException($"Reference {reference.FormKey} XAPR must contain a FormID and float delay.");
            var parent = reference.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) ??
                throw new InvalidDataException($"Reference {reference.FormKey} XAPR has no parent.");
            var delay = BinaryPrimitives.ReadSingleLittleEndian(field.Data.Span[4..]);
            if (!float.IsFinite(delay) || parents.Any(value => value.Parent == parent))
                throw new InvalidDataException($"Reference {reference.FormKey} XAPR delay is nonfinite or its parent is duplicated.");
            parents.Add(new(parent, delay));
        }
        return parents;
    }
    internal static bool ParentOnly(FalloutPluginRecord reference)
    {
        var fields = reference.ReadSubrecords().Where(field => field.Signature == "XAPD").ToArray();
        if (fields.Length > 1 || fields.Length == 1 && (fields[0].Data.Length != 1 || fields[0].Data.Span[0] > 1))
            throw new InvalidDataException($"Reference {reference.FormKey} XAPD is invalid.");
        return fields.Length == 1 && fields[0].Data.Span[0] == 1;
    }
    internal static string Hash(FalloutPluginRecord record)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData()); hash.AppendData(Encoding.UTF8.GetBytes(record.FormKey + "\0"));
        foreach (var name in record.Plugin.Masters.Append(record.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant() + "\0"));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private Dictionary<FalloutFormKey, List<(FalloutFormKey Child, float Delay)>>? _activationChildren;
    private IReadOnlyList<FalloutActivationParentIssue> _activationParentIssues = [];
    private Dictionary<FalloutFormKey, List<(FalloutFormKey Child, float Delay)>> ActivationChildren
    {
        get
        {
            if (_activationChildren is { } known) return known;
            var children = new Dictionary<FalloutFormKey, List<(FalloutFormKey, float)>>();
            var issues = new List<FalloutActivationParentIssue>();
            foreach (var reference in new[] { "REFR", "ACHR", "ACRE", "PGRE", "PMIS" }.SelectMany(records.EffectiveRecords))
            {
                if (reference.IsDeleted) continue;
                foreach (var declaration in FalloutActivationParents.Read(reference))
                {
                    var error = !records.TryGetWinner(declaration.Parent, out var parent) ? "missing parent" :
                        parent.IsDeleted ? "deleted winning parent" :
                        !Placed(parent) ? $"parent has non-reference type {parent.Signature}" : null;
                    if (error is not null) issues.Add(new(reference.FormKey, declaration.Parent, error));
                    if (!children.TryGetValue(declaration.Parent, out var values)) children.Add(declaration.Parent, values = []);
                    values.Add((reference.FormKey, declaration.DelaySeconds));
                }
            }
            _activationParentIssues = Array.AsReadOnly(issues.ToArray());
            _activationChildren = children; return children;
        }
    }
    private static bool Placed(FalloutPluginRecord record) => record.Signature is "REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS";
    internal IReadOnlyList<FalloutActivationParentIssue> ActivationParentIssues
    {
        get { _ = ActivationChildren; return _activationParentIssues; }
    }
    internal bool IsActivationParent(FalloutFormKey child, FalloutFormKey parent) =>
        FalloutActivationParents.Read(records.GetEffective(child)).Any(value => value.Parent == parent);
    internal bool AllowsActivation(FalloutFormKey child, FalloutFormKey action) =>
        !FalloutActivationParents.ParentOnly(records.GetEffective(child)) || IsActivationParent(child, action);
    internal object ActivationRelayState => _instances.Values.Where(instance => instance.ActivationRelay is { Children.Count: > 0 })
        .Select(instance => new { parent = instance.Reference.ToString(), state = instance.ActivationRelay }).ToArray();
    internal void ArmActivationChildren(FalloutFormKey parent, FalloutFormKey action)
    {
        RequireActivationComponent(parent);
        if (!ActivationChildren.TryGetValue(parent, out var children)) return;
        var instance = Get(parent);
        var previous = instance.ActivationRelay;
        var revision = checked((previous?.Revision ?? 0) + 1);
        var elapsed = previous?.ElapsedSeconds ?? 0;
        var intrinsicWrapper = records.GetEffective(instance.Base).Signature == "ACTI" &&
            (instance.Base == records.RuntimeFormKey(0x1b) || instance.Base == records.RuntimeFormKey(0x1c));
        var marks = children.Select(child => new FalloutActivationRelayChild(child.Child,
            FalloutActivationParents.Hash(records.GetEffective(child.Child)), child.Delay,
            child.Delay <= 0 && intrinsicWrapper ? action : parent, revision, child.Delay <= 0)).ToArray();
        instance.ActivationRelay = new(FalloutActivationParents.Hash(records.GetEffective(parent)), elapsed, revision, marks);
    }
    private void RequireActivationComponent(FalloutFormKey parent)
    {
        var children = ActivationChildren;
        var seen = new HashSet<FalloutFormKey>(); var path = new HashSet<FalloutFormKey>();
        void RequireAcyclic(FalloutFormKey key)
        {
            if (path.Contains(key)) throw new NotSupportedException("Recursive activation-parent graph has no bounded source dispatch owner.");
            if (!seen.Add(key)) return;
            if (!records.TryGetEffective(key, out var reference) || !Placed(reference))
                throw new InvalidDataException($"Activation-parent target {key} is not a winning placed reference.");
            // Keep every declared edge in the index. A broken endpoint stays a
            // source divergence and refuses its causal component, without making
            // unrelated doors depend on every DLC's reference completeness.
            if (_activationParentIssues.FirstOrDefault(issue => issue.Child == key) is { } issue)
                throw new InvalidDataException($"Reference {issue.Child} XAPR {issue.Parent}: {issue.Error}.");
            path.Add(key);
            foreach (var next in children.GetValueOrDefault(key) ?? []) RequireAcyclic(next.Child);
            path.Remove(key);
        }
        RequireAcyclic(parent);
    }
    internal IReadOnlyList<FalloutActivationRelayDelivery> AdvanceActivationRelays(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Activation-parent simulation delta is invalid.");
        var deliveries = new List<FalloutActivationRelayDelivery>();
        foreach (var parent in _instances.Values.Where(instance => instance.ActivationRelay is { Children.Count: > 0 }).ToArray())
        {
            if (!IsResident(parent.Reference)) continue;
            var previous = parent.ActivationRelay!;
            var elapsed = previous.ElapsedSeconds + seconds;
            if (!float.IsFinite(elapsed)) throw new InvalidDataException("Activation-parent elapsed clock overflowed.");
            var marks = new List<FalloutActivationRelayChild>();
            foreach (var mark in previous.Children)
            {
                if (mark.Due || previous.ElapsedSeconds < mark.DelaySeconds && mark.DelaySeconds <= elapsed)
                {
                    var due = mark with { Due = true }; marks.Add(due); deliveries.Add(new(parent.Reference, due));
                }
                else if (mark.DelaySeconds > elapsed) marks.Add(mark);
            }
            parent.ActivationRelay = previous with { ElapsedSeconds = marks.Any(mark => !mark.Due) ? elapsed : 0, Children = marks.ToArray() };
        }
        return deliveries;
    }
    internal void ConsumeActivationRelay(FalloutActivationRelayDelivery delivery)
    {
        var parent = Get(delivery.Parent);
        var previous = parent.ActivationRelay ?? throw new InvalidOperationException("Activation-parent delivery has no retained cycle.");
        parent.ActivationRelay = previous with
        {
            Children = previous.Children.Where(mark =>
            mark.Reference != delivery.Child.Reference || mark.Revision != delivery.Child.Revision).ToArray()
        };
    }
    internal void ValidateActivationRelay(FalloutFormKey parent, FalloutActivationRelaySnapshot snapshot)
    {
        snapshot.Validate();
        RequireActivationComponent(parent);
        if (!snapshot.SourceSha256.Equals(FalloutActivationParents.Hash(records.GetEffective(parent)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved activation-parent source changed.");
        foreach (var mark in snapshot.Children)
        {
            var child = records.GetEffective(mark.Reference);
            var declaration = FalloutActivationParents.Read(child).SingleOrDefault(value => value.Parent == parent);
            var parentBase = Get(parent).Base;
            var intrinsicWrapper = records.GetEffective(parentBase).Signature == "ACTI" &&
                (parentBase == records.RuntimeFormKey(0x1b) || parentBase == records.RuntimeFormKey(0x1c));
            if (declaration is null || declaration.DelaySeconds != mark.DelaySeconds ||
                !mark.SourceSha256.Equals(FalloutActivationParents.Hash(child), StringComparison.OrdinalIgnoreCase) ||
                (mark.DelaySeconds > 0 || !intrinsicWrapper) && mark.ActionReference != parent)
                throw new InvalidDataException("Saved activation child differs from its authored parent/delay/action binding.");
            if (mark.ActionReference != records.RuntimeFormKey(0x14)) _ = Get(mark.ActionReference);
        }
    }
}
