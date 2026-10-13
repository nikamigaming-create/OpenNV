using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutTriggerMembershipState { Known, LegacyMissing }

internal sealed record FalloutTriggerContactsSnapshot(string Schema, FalloutFormKey Reference,
    string SourceSha256, FalloutTriggerMembershipState State, IReadOnlyList<FalloutFormKey> Admitted)
{
    internal const string ExpectedSchema = "opennv-trigger-membership/v1";

    internal void Validate()
    {
        if (Schema != ExpectedSchema || !FalloutTriggerContacts.ValidReferenceKey(Reference) ||
            SourceSha256 is not { Length: 64 } || !SourceSha256.All(Uri.IsHexDigit) ||
            State is not (FalloutTriggerMembershipState.Known or FalloutTriggerMembershipState.LegacyMissing) ||
            Admitted is null || Admitted.Any(reference => !FalloutTriggerContacts.ValidReferenceKey(reference)) ||
            Admitted.Distinct(FalloutFormKeyComparer.Instance).Count() != Admitted.Count ||
            State == FalloutTriggerMembershipState.LegacyMissing && Admitted.Count != 0)
            throw new InvalidDataException("Saved trigger membership has an invalid source, state or ordered contact list.");
    }
}

// Contact discovery belongs to physics; script admission belongs to the world.
// Admit one enter OR one leave per frame, with enters taking precedence. Keep
// deferred contacts so simultaneous overlaps cannot silently lose an event.
internal sealed class FalloutTriggerContacts
{
    private readonly FalloutPluginRecord? _source;
    private readonly Action<FalloutFormKey>? _requireReference;
    private readonly List<FalloutFormKey> _admitted = [];
    private readonly HashSet<FalloutFormKey> _members = new(FalloutFormKeyComparer.Instance);
    private string? _sourceSha256;
    private bool _used;
    internal bool LegacyMembershipMissing { get; private set; }
    internal bool HasAdmittedContacts => _admitted.Count != 0;

    // The parameterless owner exercises admission alone. Production membership
    // and cold snapshots always belong to an actual retained source reference.
    internal FalloutTriggerContacts() { }
    internal FalloutTriggerContacts(FalloutPluginRecord source, Action<FalloutFormKey> requireReference)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(requireReference);
        _source = source; _requireReference = requireReference;
    }

    internal FalloutTriggerContactsSnapshot Capture()
    {
        var source = _source ?? throw new InvalidOperationException("Trigger capture has no actual source reference owner.");
        return new(FalloutTriggerContactsSnapshot.ExpectedSchema, source.FormKey, SourceSha256(),
            LegacyMembershipMissing ? FalloutTriggerMembershipState.LegacyMissing : FalloutTriggerMembershipState.Known,
            Array.AsReadOnly(_admitted.ToArray()));
    }

    internal void Restore(FalloutTriggerContactsSnapshot? saved)
    {
        var source = _source ?? throw new InvalidOperationException("Trigger restoration has no actual source reference owner.");
        if (_used) throw new InvalidOperationException("Trigger restoration requires a fresh logical owner.");
        if (saved is null)
        {
            // Old reference snapshots did not retain membership. Absence is an
            // independent unowned lane, never evidence of an empty trigger.
            LegacyMembershipMissing = true; _used = true; return;
        }
        saved.Validate();
        if (saved.Reference != source.FormKey || !StringComparer.OrdinalIgnoreCase.Equals(saved.SourceSha256, SourceSha256()))
            throw new InvalidDataException("Saved trigger membership differs from its winning source reference.");
        foreach (var contact in saved.Admitted) _requireReference!(contact);
        _admitted.AddRange(saved.Admitted); _members.UnionWith(saved.Admitted);
        LegacyMembershipMissing = saved.State == FalloutTriggerMembershipState.LegacyMissing; _used = true;
    }

    internal IReadOnlyList<FalloutReferenceScriptEvent> Advance(IReadOnlyList<FalloutFormKey> overlapping)
    {
        ArgumentNullException.ThrowIfNull(overlapping);
        if (LegacyMembershipMissing)
            throw new NotSupportedException("source-trigger-contact-membership-legacy-snapshot-missing");
        var physical = overlapping.ToHashSet(FalloutFormKeyComparer.Instance);
        if (physical.Count != overlapping.Count)
            throw new InvalidDataException("Trigger contacts contain duplicate reference identities.");
        foreach (var contact in overlapping)
        {
            if (!ValidReferenceKey(contact)) throw new InvalidDataException("Trigger contact reference identity is invalid.");
            _requireReference?.Invoke(contact);
        }
        _used = true;
        var events = new List<FalloutReferenceScriptEvent>();
        var entered = overlapping.Where(reference => !_members.Contains(reference)).Select(reference => (FalloutFormKey?)reference).FirstOrDefault();
        if (entered is { } added)
        {
            _admitted.Add(added);
            _members.Add(added);
            events.Add(new("OnTriggerEnter", added));
        }
        else
        {
            var departed = _admitted.Where(reference => !physical.Contains(reference)).Select(reference => (FalloutFormKey?)reference).FirstOrDefault();
            if (departed is { } removed)
            {
                _admitted.Remove(removed);
                _members.Remove(removed);
                events.Add(new("OnTriggerLeave", removed));
            }
        }
        if (_admitted.Count != 0) events.Add(new("OnTrigger", TriggerReferences: _admitted.ToHashSet(FalloutFormKeyComparer.Instance)));
        return events;
    }

    internal static bool ValidReferenceKey(FalloutFormKey reference) =>
        !string.IsNullOrWhiteSpace(reference.OwnerPlugin) && reference.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;

    internal static void RequireReference(FalloutPluginStack records, FalloutFormKey reference)
    {
        if (FalloutScriptBindings.IsPlayerReference(records, reference)) return;
        if (!FalloutReferenceInstance.IsPlacedReferenceSignature(records.GetEffective(reference).Signature))
            throw new InvalidDataException("Trigger membership subject is not a winning placed reference.");
    }

    private string SourceSha256()
    {
        if (_sourceSha256 is not null) return _sourceSha256;
        var source = _source ?? throw new InvalidOperationException("Trigger membership has no source identity.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(source.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(source.FormKey + "\0" + source.Signature + "\0" +
            source.Flags.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
        foreach (var name in source.Plugin.Masters.Append(source.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant() + "\0"));
        return _sourceSha256 = Convert.ToHexString(hash.GetHashAndReset());
    }
}
