using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed record AccessSource(FalloutPluginRecord Record, string Sha256,
        IReadOnlyList<FalloutPluginSubrecord> Fields);
    private sealed record LockDeclaration(int Level, FalloutFormKey? Key, byte Flags);
    private readonly Dictionary<FalloutFormKey, AccessSource> _accessSources = [];

    private AccessSource Access(FalloutFormKey reference)
    {
        _ = Get(reference); // Require the actual placed instance, not its base.
        if (_accessSources.TryGetValue(reference, out var existing)) return existing;
        var source = records.GetEffective(reference);
        // Identical record bytes can name different keys after a winning
        // plugin's master table changes. Pin that adjustment context as well.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(source.ReadData());
        foreach (var name in source.Plugin.Masters.Append(source.Plugin.Name))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
            hash.AppendData([0]);
        }
        var access = new AccessSource(source, Convert.ToHexString(hash.GetHashAndReset()),
            source.ReadSubrecords().ToArray());
        _accessSources.Add(reference, access);
        return access;
    }

    private static ReadOnlyMemory<byte> AccessField(AccessSource source, string name)
    {
        var fields = source.Fields.Where(field => field.Signature == name).ToArray();
        if (fields.Length > 1) throw new InvalidDataException($"Reference {name} is ambiguous.");
        if (fields.Length == 1 && fields[0].Data.IsEmpty)
            throw new InvalidDataException($"Reference {name} is empty.");
        return fields.Length == 0 ? default : fields[0].Data;
    }

    private LockDeclaration? LockSource(FalloutFormKey reference, AccessSource source)
    {
        if (records.GetEffective(Get(reference).Base).Signature == "TERM")
            throw new NotSupportedException("Terminal lock/access requires its terminal-state owner.");
        var field = AccessField(source, "XLOC");
        if (field.IsEmpty)
        {
            if (!AccessField(source, "XTEL").IsEmpty)
                throw new NotSupportedException("Linked-door lock inheritance requires its effective lock owner.");
            return null;
        }
        if (field.Length is not (12 or 20)) throw new InvalidDataException("Reference lock has an invalid extent.");
        var key = source.Record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Span[4..]));
        if (key is { } form && records.GetEffective(form).Signature != "KEYM")
            throw new InvalidDataException("Reference lock key is not a KEYM form.");
        return new(field.Span[0], key, field.Span[8]);
    }

    private (FalloutReferenceLockState? State, LockDeclaration? Source) EffectiveLock(FalloutFormKey reference)
    {
        var instance = Get(reference);
        var source = Access(reference);
        var declaration = LockSource(reference, source);
        var state = instance.LockState ?? (declaration is null ? null :
            new FalloutReferenceLockState(source.Sha256, declaration.Level, !instance.Unlocked));
        return (state, declaration);
    }

    internal int GetLocked(FalloutFormKey reference) => EffectiveLock(reference).State?.Locked == true ? 1 : 0;

    internal int GetLockLevel(FalloutFormKey reference)
    {
        var (state, declaration) = EffectiveLock(reference);
        if (((declaration?.Flags ?? 0) & 4) != 0)
            throw new NotSupportedException("Leveled lock difficulty requires its encounter-level owner.");
        return state?.Level ?? 0;
    }

    internal void LockReference(FalloutFormKey reference, double level = 0, double cellAccess = 0)
    {
        var difficulty = FalloutReferenceLockState.Integer(level);
        RequireCellAccess(cellAccess);
        var (state, _) = EffectiveLock(reference);
        var instance = Get(reference);
        instance.LockState = new(Access(reference).Sha256,
            difficulty == 0 ? state?.Level ?? 0 : unchecked((byte)difficulty), true);
        instance.Unlocked = false;
    }

    internal void UnlockReference(FalloutFormKey reference, double cellAccess = 0)
    {
        RequireCellAccess(cellAccess);
        var (state, _) = EffectiveLock(reference);
        if (state is null) return;
        var instance = Get(reference);
        instance.LockState = state with { Locked = false };
        instance.Unlocked = true;
    }

    private static void RequireCellAccess(double value)
    {
        if (FalloutReferenceLockState.Integer(value) > 0)
            throw new NotSupportedException("Lock/Unlock public-CELL side effects require their CELL access owner.");
    }

    internal FalloutReferenceOwnership Ownership(FalloutFormKey reference)
    {
        var source = Access(reference);
        var owner = OptionalAccessForm(source, "XOWN");
        if (owner is { } declared && records.GetEffective(declared).Signature is not ("NPC_" or "FACT" or "ACHR" or "CREA"))
            throw new InvalidDataException("Reference source ownership has an invalid owner type.");
        var rank = AccessField(source, "XRNK");
        if (!rank.IsEmpty && rank.Length != 4) throw new InvalidDataException("Reference ownership rank has an invalid extent.");
        var global = OptionalAccessForm(source, "XGLB");
        if (global is { } condition && records.GetEffective(condition).Signature != "GLOB")
            throw new InvalidDataException("Reference ownership global is not a GLOB form.");
        return new(Get(reference).OwnershipOverride?.Owner ?? owner,
            rank.IsEmpty ? null : BinaryPrimitives.ReadInt32LittleEndian(rank.Span), global);
    }

    private static FalloutFormKey? OptionalAccessForm(AccessSource source, string name)
    {
        var field = AccessField(source, name);
        if (field.IsEmpty) return null;
        if (field.Length != 4) throw new InvalidDataException($"Reference {name} has an invalid extent.");
        return source.Record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Span));
    }

    internal void SetOwnership(FalloutFormKey reference, FalloutFormKey? owner = null)
    {
        var selected = owner ?? records.RuntimeFormKey(7); // Engine player base, not PlayerRef.
        RequireCommandOwner(selected);
        _ = Ownership(reference);
        Get(reference).OwnershipOverride = new(Access(reference).Sha256, selected);
    }

    private void RequireCommandOwner(FalloutFormKey owner)
    {
        if (records.GetEffective(owner).Signature is not ("NPC_" or "FACT"))
            throw new InvalidDataException("SetOwnership requires an NPC_ or FACT base owner.");
    }

    private void RestoreAccess(FalloutReferenceInstance instance, FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.LockState is null && snapshot.OwnershipOverride is null && !snapshot.Unlocked) return;
        var source = Access(instance.Reference);
        if (snapshot.LockState is { } state)
        {
            if (!state.ReferenceSha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved lock differs from its winning reference declaration.");
            _ = LockSource(instance.Reference, source);
            if (state.Locked && snapshot.Unlocked)
                throw new InvalidDataException("Saved lock contradicts the legacy unlocked flag.");
            instance.LockState = state;
            instance.Unlocked = !state.Locked;
        }
        else if (snapshot.Unlocked)
        {
            // Legacy saves retained only access. Preserve the winning difficulty
            // and key; an absent source lock does not become a new lock.
            var declaration = LockSource(instance.Reference, source);
            if (declaration is not null) instance.LockState = new(source.Sha256, declaration.Level, false);
            instance.Unlocked = true;
        }
        if (snapshot.OwnershipOverride is { } ownership)
        {
            if (!ownership.ReferenceSha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved ownership differs from its winning reference declaration.");
            RequireCommandOwner(ownership.Owner);
            _ = Ownership(instance.Reference);
            instance.OwnershipOverride = ownership;
        }
    }
}
