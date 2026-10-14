using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// One shared index serves package/reference borrows and the Player frame.
// A cold publication preserves the preceding bits without replaying writers.
internal sealed class FalloutReferenceFlagHistory
{
    private readonly string _source, _stack;
    private readonly FalloutCombatActorIdentity _actor;
    private readonly Dictionary<Guid, FalloutReferenceScriptExtraInitializerPublication> _publications;
    private readonly Dictionary<(Guid Process, long Sequence), (uint Flags, bool Available)> _boundaries = [];

    internal FalloutReferenceFlagHistory(FalloutReferenceScriptExtraInitializerSnapshot saved,
        FalloutReferenceScriptExtraInitializerSource source, string stack, FalloutCombatActorIdentity actor)
    {
        FalloutReferenceScriptExtraInitializer.Validate(saved, source, stack, actor);
        _source = source.Identity; _stack = stack; _actor = actor;
        _publications = saved.Publications.ToDictionary(row => row.Process);
        var events = saved.Calls.Select(row => (Process: row.Invocation.Process, row.Changed, row.FlagsAfter,
                Unavailable: row.InvalidatedWriter is FalloutReferenceScriptExtraWriter.ReferenceFlags or FalloutReferenceScriptExtraWriter.ScriptCallback))
            .Concat((saved.ReferenceFlagWrites ?? []).Select(row =>
                (Process: row.Invocation.CommonProcess, row.Changed, FlagsAfter: row.After, Unavailable: false)))
            .OrderBy(row => row.Changed).GroupBy(row => row.Process).ToDictionary(group => group.Key, group => group.ToArray());
        var flags = FalloutReferenceScriptExtraInitializerSource.ConstructorFlags; var available = true;
        foreach (var publication in saved.Publications)
        {
            _boundaries.Add((publication.Process, publication.PublishedSequence!.Value), (flags, available));
            foreach (var row in events.GetValueOrDefault(publication.Process) ?? [])
            {
                flags = row.FlagsAfter; available &= !row.Unavailable;
                _boundaries.Add((publication.Process, row.Changed), (flags, available));
            }
        }
    }

    internal void Require(FalloutReferenceFlagRead read, Guid commonProcess, long epoch)
    {
        if (!_publications.TryGetValue(commonProcess, out var publication) || read.Process != commonProcess ||
            read.Actor != _actor || read.Stack != _stack || read.Source != _source || read.Epoch != epoch || publication.Epoch != epoch ||
            read.Factory != publication.Factory || read.FieldsFactory != publication.FieldsFactory || read.Constructor != publication.Constructor ||
            !_boundaries.TryGetValue((read.Process, read.Sequence), out var boundary) || !boundary.Available || boundary.Flags != read.Flags)
            throw new InvalidDataException("Reference flag borrow has no identical retained merged initializer writer boundary.");
    }

    internal void RequirePlayerFrame(FalloutReferenceFlagRead read, FalloutPlayerLateFrameInvocation invocation)
    {
        Require(read, invocation.Common.Process, invocation.Common.Epoch);
        var publication = _publications[read.Process];
        if (publication.ThreadProcess != invocation.PlayerProcess || publication.Constructor != invocation.PlayerConstructor)
            throw new InvalidDataException("Player frame flags borrowed another actual Scene/Common constructor publication.");
    }
}
