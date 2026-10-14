using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerLateGlobalAiPublication(Guid Process, Guid? PreviousProcess, long Changed);
internal sealed record FalloutPlayerLateGlobalAiWriter(Guid Process, long Revision, long Changed,
    string Producer, string Boundary);
internal sealed record FalloutPlayerLateGlobalAiSnapshot(string Source, Guid Process, long Revision,
    byte Value, string? Boundary, string? Producer,
    IReadOnlyList<FalloutPlayerLateGlobalAiPublication> Publications,
    IReadOnlyList<FalloutPlayerLateGlobalAiWriter> Writers);

internal sealed partial class FalloutActorUpdateState
{
    // Independent shared byte in the existing Actor-update domain. Actor B4
    // and global ToggleAI have different writers and source effects.
    private FalloutPlayerLateActorTimeSource? _frameAiSource;
    private Action? _requireFrameAiLifetime;
    private byte _frameGlobalAi;
    private string? _frameGlobalAiBoundary, _frameGlobalAiProducer;
    private long _frameGlobalAiRevision;
    private readonly List<FalloutPlayerLateGlobalAiPublication> _frameAiPublications = [];
    private readonly List<FalloutPlayerLateGlobalAiWriter> _frameAiWriters = [];
    private FalloutPlayerLateGlobalAiSnapshot? _preparedColdFrameGlobalAi;
    private int? _frameAiThread;
    internal string? PlayerFrameGlobalAiSaveBlocker => _frameAiSource is null ?
        _preparedColdFrameGlobalAi is null ? null : "source-global-AI-cold-live-owner-unbound" : _frameGlobalAiBoundary;

    internal void BindPlayerFrameGlobalAi(FalloutPlayerLateActorTimeSource source, Action requireLifetime)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireLifetime);
        ObjectDisposedException.ThrowIf(_disposed, this); requireLifetime();
        if (_busy || _frameAiSource is not null || source.Following.Controls.Main.EngineSha256 != _source.ExecutableSha256)
            throw new InvalidOperationException("Global AI lane changed its actual source Actor-update domain.");
        // Only this actual owner's validated Restore baseline crosses processes.
        // The binder has no caller-supplied saved byte or writer history.
        var saved = _preparedColdFrameGlobalAi;
        if (_cold is not null && saved is null)
            throw new InvalidDataException("Cold Actor-update domain omitted its required shared AI field and writer history.");
        if (saved is not null && (saved.Source != source.Identity || saved.Process != _cold?.PreviousProcess))
            throw new InvalidDataException("Cold global AI lane borrowed another source or parent baseline.");
        _frameAiSource = source; _requireFrameAiLifetime = requireLifetime;
        _frameAiThread = Environment.CurrentManagedThreadId;
        if (saved is not null)
        {
            _frameGlobalAi = saved.Value; _frameGlobalAiRevision = saved.Revision;
            _frameGlobalAiBoundary = saved.Boundary; _frameGlobalAiProducer = saved.Producer;
            _frameAiPublications.AddRange(saved.Publications); _frameAiWriters.AddRange(saved.Writers);
        }
        _frameAiPublications.Add(new(_process, saved?.Process, Next()));
        _preparedColdFrameGlobalAi = null;
    }

    internal FalloutPlayerLateGlobalAiRead ReadPlayerFrameGlobalAi(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); RequireFrameAi(); requireActual(invocation);
        var actor = RequireHealthy(invocation.Common.Actor.Reference);
        if (actor.Source != invocation.Common.Actor || !actor.Source.EnginePlayer ||
            invocation.Common.Stack != _stack || invocation.ThreadId != _frameAiThread ||
            invocation.Common.ExecutableSha256 != _source.ExecutableSha256)
            throw new InvalidOperationException("Global AI read borrowed another actual Actor, source or product thread.");
        var result = new FalloutPlayerLateGlobalAiRead(_frameAiSource!.Identity, _stack, _process,
            _sequence, _frameGlobalAiRevision, _frameGlobalAi, _frameGlobalAiBoundary);
        requireActual(invocation); RequireFrameAi();
        if (result.Revision != _frameGlobalAiRevision || result.Value != _frameGlobalAi || result.Boundary != _frameGlobalAiBoundary)
            throw new InvalidOperationException("Global AI writer entered during its source field read.");
        return result;
    }

    // Global ToggleAI stores before its console/task children. Until that body
    // is owned, every admitted ingress invalidates here before effects. This
    // retains a failed prefix; it does not fabricate a toggle or command return.
    internal void ObserveUnownedGlobalAiWriter(string producer)
    {
        RequireFrameAi(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        _frameGlobalAiRevision = checked(_frameGlobalAiRevision + 1);
        _frameGlobalAiProducer ??= producer;
        _frameGlobalAiBoundary ??= "source-global-ToggleAI-shared-byte-and-task-retirement-unowned";
        _frameAiWriters.Add(new(_process, _frameGlobalAiRevision, Next(), producer, _frameGlobalAiBoundary));
    }

    internal FalloutPlayerLateGlobalAiSnapshot CapturePlayerFrameGlobalAi()
    {
        RequireFrameAi(); return CaptureRetainedPlayerFrameGlobalAi()!;
    }
    private FalloutPlayerLateGlobalAiSnapshot? CaptureRetainedPlayerFrameGlobalAi() => _frameAiSource is null ?
        _preparedColdFrameGlobalAi : new(_frameAiSource.Identity, _process, _frameGlobalAiRevision,
            _frameGlobalAi, _frameGlobalAiBoundary, _frameGlobalAiProducer,
            Array.AsReadOnly(_frameAiPublications.ToArray()), Array.AsReadOnly(_frameAiWriters.ToArray()));

    private static void ValidatePlayerFrameGlobalAi(FalloutPlayerLateGlobalAiSnapshot saved, Guid process, long sequence)
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(saved.Source) || saved.Process != process || saved.Value != 0 ||
            saved.Revision < 0 || saved.Publications is not { Count: > 0 } || saved.Writers is null ||
            saved.Revision != saved.Writers.Count || (saved.Boundary is null) != (saved.Revision == 0) ||
            (saved.Producer is null) != (saved.Revision == 0) || saved.Boundary is { Length: 0 } || saved.Producer is { Length: 0 })
            throw new InvalidDataException("Shared AI lost its exact source loader value or retained writer prefix.");
        var owners = new Dictionary<Guid, (long Start, long End)>();
        FalloutPlayerLateGlobalAiPublication? previous = null;
        for (var index = 0; index < saved.Publications.Count; index++)
        {
            var row = saved.Publications[index];
            if (row.Process == Guid.Empty || row.Changed < 1 || row.Changed > sequence ||
                row.PreviousProcess != previous?.Process || previous is not null && row.Changed <= previous.Changed ||
                !owners.TryAdd(row.Process, (row.Changed,
                    index + 1 < saved.Publications.Count ? saved.Publications[index + 1].Changed : checked(sequence + 1))))
                throw new InvalidDataException("Shared AI replaced or replayed a real source process publication.");
            previous = row;
        }
        if (previous!.Process != process)
            throw new InvalidDataException("Shared AI current byte belongs to another Actor-update domain.");
        long prior = 0, revision = 0;
        foreach (var writer in saved.Writers)
        {
            if (!owners.TryGetValue(writer.Process, out var interval) || writer.Changed <= interval.Start || writer.Changed >= interval.End ||
                writer.Changed <= prior || writer.Revision != checked(++revision) || string.IsNullOrWhiteSpace(writer.Producer) ||
                writer.Boundary != "source-global-ToggleAI-shared-byte-and-task-retirement-unowned")
                throw new InvalidDataException("Shared AI unowned writer erased its real process/store-before-effects prefix.");
            prior = writer.Changed;
        }
        if (saved.Writers.Count > 0 && (saved.Producer != saved.Writers[0].Producer || saved.Boundary != saved.Writers[0].Boundary))
            throw new InvalidDataException("Shared AI current boundary differs from its first failed source producer.");
    }
    private void RequireFrameAi()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy || _frameAiThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Global AI lane is already entered or changed product thread.");
        (_requireFrameAiLifetime ?? throw new NotSupportedException("source-global-AI-current-loader-writer-owner-unbound"))();
    }
}
