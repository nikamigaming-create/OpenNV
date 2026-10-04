using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed record AccessSource(FalloutPluginRecord Record, string Sha256,
        IReadOnlyList<FalloutPluginSubrecord> Fields);
    private sealed record LockDeclaration(int Level, FalloutFormKey? Key, byte Flags, bool InitiallyLocked = true);
    private readonly Dictionary<FalloutFormKey, AccessSource> _accessSources = [];
    private readonly Dictionary<FalloutFormKey, AccessSource> _terminalAccessSources = [];

    private AccessSource Access(FalloutFormKey reference)
    {
        _ = Get(reference); // Require the actual placed instance, not its base.
        if (_accessSources.TryGetValue(reference, out var existing)) return existing;
        var source = records.GetEffective(reference);
        var access = ReadAccess(source);
        _accessSources.Add(reference, access);
        return access;
    }

    private static AccessSource ReadAccess(FalloutPluginRecord source)
    {
        // Identical record bytes can name different keys after a winning
        // plugin's master table changes. Pin that adjustment context as well.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(source.ReadData());
        foreach (var name in source.Plugin.Masters.Append(source.Plugin.Name))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
            hash.AppendData([0]);
        }
        return new AccessSource(source, Convert.ToHexString(hash.GetHashAndReset()),
            source.ReadSubrecords().ToArray());
    }

    private AccessSource? TerminalAccess(FalloutFormKey reference)
    {
        var basis = Get(reference).Base;
        var record = records.GetEffective(basis);
        if (record.Signature != "TERM") return null;
        if (!_terminalAccessSources.TryGetValue(basis, out var source))
            _terminalAccessSources.Add(basis, source = ReadAccess(record));
        return source;
    }

    private string LockSourceHash(FalloutFormKey reference, AccessSource source)
    {
        if (TerminalAccess(reference) is not { } terminal) return source.Sha256;
        return Convert.ToHexString(SHA256.HashData(Convert.FromHexString(source.Sha256)
            .Concat(Convert.FromHexString(terminal.Sha256)).ToArray()));
    }

    private LockDeclaration TerminalLock(AccessSource terminal, AccessSource reference)
    {
        if (!AccessField(reference, "XLOC").IsEmpty)
            throw new NotSupportedException("Placed terminal XLOC overrides need their effective declaration owner.");
        var data = AccessField(terminal, "DNAM");
        if (data.Length != 4 || data.Span[0] > 5 || (data.Span[1] & ~15) != 0)
            throw new InvalidDataException("Terminal lock declaration has an invalid difficulty, flags or extent.");
        var password = OptionalAccessForm(terminal, "PNAM");
        if (password is { } key && records.GetEffective(key).Signature != "NOTE")
            throw new InvalidDataException("Terminal password is not a NOTE form.");
        int[] levels = [0, 25, 50, 75, 100, 255];
        return new(levels[data.Span[0]], password, (byte)((data.Span[1] & 1) != 0 ? 4 : 0),
            (data.Span[1] & 2) == 0);
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
        if (TerminalAccess(reference) is { } terminal) return TerminalLock(terminal, source);
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
            new FalloutReferenceLockState(LockSourceHash(reference, source), declaration.Level,
                declaration.InitiallyLocked && !instance.Unlocked));
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
        instance.LockState = new(LockSourceHash(reference, Access(reference)),
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
            if (!state.ReferenceSha256.Equals(LockSourceHash(instance.Reference, source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved lock differs from its winning reference or terminal declaration.");
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
            if (declaration is not null) instance.LockState = new(LockSourceHash(instance.Reference, source), declaration.Level, false);
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
