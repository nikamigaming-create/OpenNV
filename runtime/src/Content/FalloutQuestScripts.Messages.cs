using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMessageIntent(FalloutFormKey Form, FalloutFormKey? Script = null, FalloutFormKey? Caller = null);

internal sealed partial class FalloutQuestScripts
{
    private FalloutFormKey MessageCaller(FalloutFormKey? script, FalloutFormKey? caller)
    {
        if (script is { } sourceScript && _records.GetEffective(sourceScript).Signature != "SCPT")
            throw new InvalidDataException("ShowMessage executing script is not SCPT.");
        if (caller is { } reference && _records.RuntimeFormId(reference) != 0x14)
        {
            if (_records.GetEffective(reference).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("ShowMessage executing object is not a winning reference.");
            return reference;
        }
        return script ?? throw new NotSupportedException("ShowMessage has no executing reference or SCPT owner.");
    }

    internal PreparedMessageBatch PrepareMessageBatch(IReadOnlyList<FalloutMessageIntent> intents)
    {
        var before = MessageResults.Capture();
        var next = before.Sequence;
        var messages = new FalloutSourceMessage[intents.Count];
        for (var index = 0; index < intents.Count; ++index)
        {
            var intent = intents[index];
            var message = FalloutSourceMessage.Read(_records.GetEffective(intent.Form));
            var owner = MessageCaller(intent.Script, intent.Caller);
            messages[index] = message with { Request = new(intent.Form, owner, checked(++next)) };
        }
        var hud = messages.Select((message, index) => (message, intent: intents[index]))
            .Where(value => !value.message.Modal).Select(value =>
                new FalloutHudEvent(FalloutHudEventKind.Message, value.message.Form, 0, Script: value.intent.Script)).ToArray();
        return new(this, before, messages, hud);
    }

    internal sealed class PreparedMessageBatch
    {
        private readonly FalloutQuestScripts _owner;
        private readonly FalloutMessageResultsSnapshot _before;
        private readonly FalloutSourceMessage[] _messages;
        internal IReadOnlyList<FalloutHudEvent> HudEvents { get; }
        private readonly FalloutMessageRequest[] _requests;
        private readonly HashSet<ulong> _sequences;
        private int _phase;

        internal PreparedMessageBatch(FalloutQuestScripts owner, FalloutMessageResultsSnapshot before,
            FalloutSourceMessage[] messages, FalloutHudEvent[] hud)
        {
            _owner = owner; _before = before; _messages = messages.Where(message => message.Modal).ToArray();
            _requests = messages.Select(message => message.Request!).ToArray();
            _sequences = _messages.Select(message => message.Request!.Sequence).ToHashSet();
            HudEvents = Array.AsReadOnly(hud);
        }

        internal void RequireCurrent()
        {
            if (_phase != 0) throw new InvalidOperationException("Prepared message batch is no longer awaiting publication.");
            if (_owner.MessageResults.Capture() != _before)
                throw new InvalidOperationException("Prepared message batch has stale source result ownership.");
        }

        internal void Commit()
        {
            RequireCurrent();
            // Allocate/validate before publishing either the global slot or FIFO.
            _owner._messages.EnsureCapacity(checked(_owner._messages.Count + _messages.Length));
            _owner.MessageResults.CommitPrepared(_before, _requests);
            foreach (var message in _messages) _owner._messages.Enqueue(message);
            _phase = 1;
        }

        internal void Rollback()
        {
            if (_phase == 2) return;
            if (_phase == 0) { _phase = 2; return; }
            if (_messages.Length != 0 && _owner._lastPresentedMessageSequence >= _messages[0].Request!.Sequence)
                throw new InvalidOperationException("Published message batch cannot roll back an exposed modal.");
            var queued = _owner._messages.ToArray();
            if (queued.Count(message => _sequences.Contains(message.Request!.Sequence)) != _messages.Length)
                throw new InvalidOperationException("Published message batch cannot roll back after presentation has started.");
            var retained = queued.Where(message => !_sequences.Contains(message.Request!.Sequence)).ToArray();
            _owner._messages.Clear();
            foreach (var message in retained) _owner._messages.Enqueue(message);
            if (_requests.Length != 0) _owner.MessageResults.RollbackPrepared(_before, _requests[^1].Sequence);
            _phase = 2;
        }
    }
}
