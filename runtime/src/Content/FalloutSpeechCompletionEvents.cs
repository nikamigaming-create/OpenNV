using System.Collections.Frozen;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpeechCompletionReceipt
{
    internal FalloutFormKey Speaker { get; }
    internal IReadOnlySet<FalloutFormKey> Topics { get; }
    internal FalloutFormKey? Info { get; }
    internal long Generation { get; }

    internal FalloutSpeechCompletionReceipt(FalloutFormKey speaker, IReadOnlySet<FalloutFormKey> topics,
        FalloutFormKey? info = null, long generation = 0)
    {
        if (topics is null || topics.Count == 0 ||
            (info is null ? generation != 0 : generation <= 0 || topics.Count != 1))
            throw new InvalidDataException("Speech completion has absent topics or an invalid voice generation.");
        Speaker = speaker; Topics = topics.ToFrozenSet(); Info = info; Generation = generation;
    }
}

// Empty speech waits until the calling script's suffix has run. A finished
// voice delivers immediately, retaining its exact receipt through source
// results and SayToDone. Failed delivery keeps its consumed prefix, not a retry.
internal sealed class FalloutSpeechCompletionEvents
{
    private sealed record Registration(FalloutFormKey Speaker, HashSet<FalloutFormKey> Topics,
        List<(FalloutFormKey Topic, Action Completed)> Packages)
    {
        internal FalloutSpeechCompletionReceipt? Receipt { get; set; }
    }
    private readonly Queue<Registration> _pending = [];
    private readonly Dictionary<FalloutFormKey, Registration> _bySpeaker = [];
    private readonly Dictionary<FalloutFormKey, long> _completedVoices = [];
    private Registration? _dispatching;
    private bool _draining;
    private long _requests, _completedTopics, _completedPackages;
    internal string? Error { get; private set; }
    internal bool Active => _pending.Count > 0 || _dispatching is not null;
    internal FalloutSpeechCompletionReceipt? Dispatching => _dispatching?.Receipt;
    internal object State => new
    {
        acceptedRequests = _requests,
        completedTopics = _completedTopics,
        completedPackages = _completedPackages,
        pending = _pending.Select(Describe).ToArray(),
        dispatching = _dispatching is null ? null : Describe(_dispatching),
        error = Error,
    };
    private static object Describe(Registration value) => new
    {
        speaker = value.Speaker.ToString(),
        topics = value.Topics.Select(topic => topic.ToString()).ToArray(),
        info = value.Receipt?.Info?.ToString(),
        generation = value.Receipt?.Generation ?? 0,
        packageTopics = value.Packages.Select(package => package.Topic.ToString()).ToArray(),
    };

    internal void Mark(FalloutFormKey speaker, FalloutFormKey topic)
    {
        Register(speaker).Topics.Add(topic);
        ++_requests;
    }

    internal void MarkPackage(FalloutFormKey speaker, FalloutFormKey topic, Action completed)
    {
        Register(speaker).Packages.Add((topic, completed));
        ++_requests;
    }

    private Registration Register(FalloutFormKey speaker)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        if (!_bySpeaker.TryGetValue(speaker, out var registration))
        {
            registration = new(speaker, [], []);
            _bySpeaker.Add(speaker, registration);
            _pending.Enqueue(registration);
        }
        return registration;
    }

    internal void Drain(Action<FalloutFormKey, IReadOnlySet<FalloutFormKey>> dispatch, Func<bool>? ready = null)
        => Drain(receipt => dispatch(receipt.Speaker, receipt.Topics), ready);

    internal void Drain(Action<FalloutSpeechCompletionReceipt> dispatch, Func<bool>? ready = null)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        if (_draining) throw new InvalidOperationException("Speech completion dispatch cannot be recursive.");
        _draining = true;
        try
        {
            var count = _pending.Count;
            for (var index = 0; index < count && ready?.Invoke() != false; ++index)
            {
                _dispatching = _pending.Dequeue();
                _bySpeaker.Remove(_dispatching.Speaker);
                if (_dispatching.Topics.Count != 0)
                {
                    _dispatching.Receipt = new(_dispatching.Speaker, _dispatching.Topics);
                    dispatch(_dispatching.Receipt);
                }
                _completedTopics += _dispatching.Topics.Count;
                foreach (var package in _dispatching.Packages) { package.Completed(); ++_completedPackages; }
                _dispatching = null;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FileNotFoundException)
        {
            // Retain the failed registration and source prefix, without replay.
            Error = error.Message;
            throw;
        }
        finally { _draining = false; }
    }

    internal void Complete(FalloutSpeechCompletionReceipt receipt, Action<FalloutSpeechCompletionReceipt> dispatch, Action? settled = null)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        if (_draining) throw new InvalidOperationException("Speech completion dispatch cannot be recursive.");
        if (receipt.Info is null || receipt.Generation <= _completedVoices.GetValueOrDefault(receipt.Speaker))
            throw new InvalidOperationException("Finished speech has no new voice completion generation.");
        _draining = true;
        _dispatching = new(receipt.Speaker, new(receipt.Topics), []) { Receipt = receipt };
        ++_requests;
        try
        {
            dispatch(receipt);
            _completedTopics += receipt.Topics.Count;
            _completedVoices[receipt.Speaker] = receipt.Generation;
            _dispatching = null;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FileNotFoundException)
        {
            Error = error.Message;
            throw;
        }
        finally { _draining = false; }
        settled?.Invoke();
    }
}
