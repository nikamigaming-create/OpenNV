using System.Collections.Frozen;

namespace OpenNV.Runtime.Content;

// Empty scripted speech marks the actor's topic-filtered and unfiltered
// completion blocks. Delivery must follow the calling script's suffix.
internal sealed class FalloutSpeechCompletionEvents
{
    private sealed record Registration(FalloutFormKey Speaker, HashSet<FalloutFormKey> Topics,
        List<(FalloutFormKey Topic, Action Completed)> Packages);
    private readonly Queue<Registration> _pending = [];
    private readonly Dictionary<FalloutFormKey, Registration> _bySpeaker = [];
    private Registration? _dispatching;
    private bool _draining;
    private long _requests, _completedTopics, _completedPackages;
    internal string? Error { get; private set; }
    internal bool Active => _pending.Count > 0 || _dispatching is not null;
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
                if (_dispatching.Topics.Count != 0) dispatch(_dispatching.Speaker, _dispatching.Topics.ToFrozenSet());
                _completedTopics += _dispatching.Topics.Count;
                foreach (var package in _dispatching.Packages) { package.Completed(); ++_completedPackages; }
                _dispatching = null;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            // Retain the failed registration and source prefix, without replay.
            Error = error.Message;
            throw;
        }
        finally { _draining = false; }
    }
}
