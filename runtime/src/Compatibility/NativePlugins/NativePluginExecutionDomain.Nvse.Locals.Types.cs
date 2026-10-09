using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// This implements the public ScriptEventList/local-storage ABI only. A
// polymorphic native Script or TESObjectREFR is a separate required owner.
internal enum NativeNvseLocalKind : uint { Number, Integer, Form, String, Array }
internal readonly record struct NativeNvseLocalDeclaration(uint Index, NativeNvseLocalKind Kind, byte StorageFlags = 0);
internal readonly record struct NativeNvseLocalChange(uint Index, ulong Before, ulong After);
internal readonly record struct NativeNvseLocalEntryChange(int Entry, uint Index, ulong Before, ulong After);
internal readonly record struct NativeNvseLocalStatistics(uint LiveContexts, uint RetiredContexts, uint ActiveCalls,
    uint LiveRegions, uint RetiredRegions, uint CommittedBytes, uint ReservedBytes);

internal abstract class NativeNvseLocalAuthority
{
    internal abstract string SourceOwner { get; }
    internal abstract string SourceSha256 { get; }
    internal abstract string CodeSha256 { get; }
    internal abstract uint CodeBytes { get; }
    internal abstract object ValueStoreIdentity { get; }
    internal abstract IReadOnlyList<NativeNvseLocalDeclaration> Declarations { get; }
    internal abstract IDisposable RetainSource();
    internal abstract void RequireCurrent();
    internal abstract ulong Read(uint index);
    internal virtual ulong ReadEntry(int entry)
    {
        if ((uint)entry >= Declarations.Count || Declarations.Count(row => row.Index == Declarations[entry].Index) != 1)
            throw new NotSupportedException("Repeated native local IDs require their actual ordered campaign entry owner.");
        return Read(Declarations[entry].Index);
    }
    internal virtual Action PrepareEntries(IReadOnlyList<NativeNvseLocalEntryChange> changes)
    {
        foreach (var change in changes)
            if ((uint)change.Entry >= Declarations.Count || Declarations[change.Entry].Index != change.Index ||
                Declarations.Count(row => row.Index == change.Index) != 1)
                throw new NotSupportedException("Native mutation requires its exact ordered local entry owner.");
        return Prepare(changes.Select(change => new NativeNvseLocalChange(change.Index, change.Before, change.After)).ToArray());
    }
    // Admission of every changed slot precedes publication. The returned
    // operation writes through the existing campaign owner and value store.
    internal abstract Action Prepare(IReadOnlyList<NativeNvseLocalChange> changes);
}

internal sealed class NativeNvseLocalContext
{
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal ulong Id { get; }
    internal uint EventList { get; set; }
    internal uint Variables { get; set; }
    internal NativeNvseLocalAuthority Authority { get; }
    internal NativeNvseSourceObject? Script { get; set; }
    internal IDisposable SourceLease { get; }
    internal ImmutableArray<NativeNvseLocalDeclaration> Declarations { get; }
    internal ulong[] Baseline { get; }
    internal int Active { get; set; }
    internal int Retainers { get; set; }
    internal bool Retired { get; set; }
    internal NativeNvseLocalTransfer? Transfer { get; set; }

    internal NativeNvseLocalContext(ulong generation, ulong module, ulong id, NativeNvseLocalAuthority authority, NativeNvseSourceObject? script = null)
    {
        Generation = generation; Module = module; Id = id; Authority = authority; Script = script; SourceLease = authority.RetainSource();
        try
        {
            Declarations = ImmutableArray.CreateRange(authority.Declarations);
            if (Declarations.Any(row => !Enum.IsDefined(row.Kind)))
                throw new InvalidDataException("Native event-list entries have no source type.");
            Baseline = Enumerable.Range(0, Declarations.Length).Select(authority.ReadEntry).ToArray();
        }
        catch { SourceLease.Dispose(); throw; }
    }
}

internal sealed class NativeNvseLocalTransfer(ulong sequence, ulong caller, bool entry, int count)
{
    internal ulong Sequence { get; } = sequence;
    internal ulong Caller { get; } = caller;
    internal bool Entry { get; } = entry;
    internal ulong[] Native { get; } = new ulong[count];
    internal int Received { get; set; }
    internal ulong[]? Canonical { get; set; }
    internal int Sent { get; set; }
}
