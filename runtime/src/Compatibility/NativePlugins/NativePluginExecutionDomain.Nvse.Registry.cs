using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Registration is an actual callback transaction, not a command implementation.
// The original executable pointers remain tied to their image/generation. No
// command becomes runnable without its own typed eight-pointer frame/objects.
internal sealed class NativeNvseRegistry(NativeNvsePlugin plugin)
{
    private readonly List<NativeNvseCommand> _commands = [];
    private readonly List<NativeNvseInterfaceQuery> _queries = [];
    private readonly List<NativeNvseListenerRegistration> _listenerHistory = [];
    private readonly List<NativeNvseSerializationRegistration> _serializationHistory = [];
    private readonly List<NativeNvseUnownedRequest> _unowned = [];
    private readonly List<NativeNvseMessageDispatch> _dispatches = [];
    private readonly List<NativeNvseMessageCompletion> _deliveries = [];
    private readonly List<NativeNvseSerializationCompletion> _serializationDeliveries = [];
    private readonly Dictionary<uint, uint> _listeners = [];
    private readonly Dictionary<NativeNvseSerializationEvent, uint> _serialization = [];
    private readonly Dictionary<uint, NativeNvseCommand> _opcodes = [];
    private readonly Dictionary<ulong, (NativeNvseMessageDispatch Row, uint Function)> _pendingMessages = [];
    private readonly Dictionary<ulong, (NativeNvseSerializationEvent Event, uint Function)> _pendingSerialization = [];
    private uint? _nextOpcode;
    private uint _baseOpcode, _commandAttempts;
    private ulong _nextRegistration;
    private bool _retired;
    internal IReadOnlyList<NativeNvseCommand> Commands => _commands.AsReadOnly();
    internal IReadOnlyList<NativeNvseInterfaceQuery> InterfaceQueries => _queries.AsReadOnly();
    internal IReadOnlyList<NativeNvseListenerRegistration> ListenerHistory => _listenerHistory.AsReadOnly();
    internal IReadOnlyList<NativeNvseSerializationRegistration> SerializationHistory => _serializationHistory.AsReadOnly();
    internal IReadOnlyList<NativeNvseUnownedRequest> UnownedRequests => _unowned.AsReadOnly();
    internal IReadOnlyList<NativeNvseMessageDispatch> MessageDispatches => _dispatches.AsReadOnly();
    internal IReadOnlyList<NativeNvseMessageCompletion> MessageCompletions => _deliveries.AsReadOnly();
    internal IReadOnlyList<NativeNvseSerializationCompletion> SerializationCompletions => _serializationDeliveries.AsReadOnly();
    internal NativeNvseRegistryCounts Counts => new(_commandAttempts, checked((uint)_listenerHistory.Count),
        checked((uint)_serializationHistory.Count), checked((uint)_commands.Count), checked((uint)_listeners.Count));

    internal uint QueryInterface(ulong callback, uint id, bool valuesAvailable = false, bool scriptAvailable = false)
    {
        RequireLive();
        var version = id switch { 0 => 2U, 2 => 4U, 4 when valuesAvailable => 1U, 5 when valuesAvailable => 2U, 6 when scriptAvailable => 1U, _ => 0U };
        var missing = version == 0 ? $"NVSE interface {id} has no native C# runtime owner." : null;
        _queries.Add(new(callback, id, version, missing));
        if (missing is not null) Missing(callback, missing);
        return version;
    }
    internal uint SetOpcode(ulong callback, uint value)
    {
        RequireLoad();
        if (value == 0) { Missing(callback, "NVSE SetOpcodeBase declared zero, which has no command identity."); return 0; }
        _nextOpcode = value;
        if (_baseOpcode == 0) _baseOpcode = value;
        return 1;
    }
    internal uint Register(ulong callback, uint source, uint rawOpcode, byte returnType, uint requiredVersion,
        ushort needsParent, uint flags, uint parametersAddress, NativeNvseText name, NativeNvseText alias,
        NativeNvseText help, ImmutableArray<NativeNvseParameter> parameters, uint execute, uint parse, uint evaluate)
    {
        RequireLoad(); ++_commandAttempts;
        if (_nextOpcode is null || _nextOpcode == 0 || _nextOpcode == uint.MaxValue || _opcodes.ContainsKey(_nextOpcode.Value))
        { Missing(callback, "NVSE command registration has no unambiguous source opcode allocation."); return 0; }
        // Public xNVSE normalizes unknown return enums to Default. Preserve the
        // original byte independently, rather than erase the declaration.
        var effectiveReturn = returnType < 6 ? (NativeNvseCommandReturn)returnType : NativeNvseCommandReturn.Default;
        var command = new NativeNvseCommand(plugin.Generation, plugin.Module, plugin.Handle, ++_nextRegistration,
            source, rawOpcode, _nextOpcode.Value, _baseOpcode, returnType, effectiveReturn, requiredVersion,
            needsParent, flags, parametersAddress, name, alias, help, parameters, execute, parse, evaluate);
        _commands.Add(command); _opcodes.Add(command.AssignedOpcode, command); ++_nextOpcode;
        return command.AssignedOpcode;
    }
    internal uint RegisterListener(ulong callback, uint handle, NativeNvseText sender, uint function)
    {
        RequireLive();
        uint? bound = null, retained = null;
        var accepted = handle == plugin.Handle && function != 0;
        if (accepted && !sender.IsNull)
        {
            if (!sender.IsAscii)
            { accepted = false; Missing(callback, "NVSE messaging identifier requires its non-ASCII locale owner."); }
            else if (string.Equals(sender.Display, "NVSE", StringComparison.OrdinalIgnoreCase)) bound = 0;
            else if (plugin.QueryReceipt?.Info.Name is { IsAscii: true } name && string.Equals(sender.Display, name.Display, StringComparison.OrdinalIgnoreCase)) bound = plugin.Handle;
            else { accepted = false; Missing(callback, $"NVSE messaging sender {sender.Display} has no retained module in this generation."); }
        }
        if (accepted && bound is uint source)
        {
            // The public owner keeps the first registration for this pair and
            // returns true on duplicates, even when a different pointer was supplied.
            if (!_listeners.TryGetValue(source, out var existing)) _listeners.Add(source, function);
            retained = existing == 0 ? function : existing;
        }
        _listenerHistory.Add(new(callback, handle, sender, function, accepted, bound, retained));
        return accepted ? 1U : 0U;
    }
    internal uint RegisterSerialization(ulong callback, uint handle, NativeNvseSerializationEvent @event, uint function)
    {
        RequireLive();
        if (handle != plugin.Handle || !Enum.IsDefined(@event))
        { Missing(callback, "NVSE serialization callback has a foreign handle/event."); return 0; }
        _serializationHistory.Add(new(callback, handle, @event, function));
        if (function == 0) _serialization.Remove(@event); else _serialization[@event] = function;
        return 1;
    }
    internal uint Dispatch(ulong callback, ulong parent, ulong sequence, uint sender, uint type, uint dataAddress,
        ImmutableArray<byte> data, NativeNvseText receiver)
    {
        RequireLive();
        var matches = sender is 0 || sender == plugin.Handle;
        if (!receiver.IsNull)
        {
            if (!receiver.IsAscii || !(plugin.QueryReceipt?.Info.Name.IsAscii ?? false))
            { matches = false; Missing(callback, "NVSE message receiver requires its non-ASCII locale owner."); }
            else matches &= string.Equals(receiver.Display, plugin.QueryReceipt!.Info.Name.Display, StringComparison.OrdinalIgnoreCase);
        }
        if (sequence == 0 || _dispatches.Any(row => row.Dispatch == sequence))
            throw new InvalidDataException("Original NVSE message lifetime identity is stale or duplicated.");
        var count = matches && _listeners.TryGetValue(sender, out _) ? 1U : 0U;
        var row = new NativeNvseMessageDispatch(callback, parent, sequence, sender, type, dataAddress, data, receiver, count);
        _dispatches.Add(row);
        if (count != 0) _pendingMessages.Add(sequence, (row, _listeners[sender]));
        return count;
    }
    internal uint CompleteMessage(ulong callback, ulong parent, ulong sequence, uint function, uint sender, uint type, uint pointer, uint length)
    {
        RequireLive();
        if (!_pendingMessages.Remove(sequence, out var invocation) || invocation.Function != function ||
            invocation.Row.Parent != parent || invocation.Row.Sender != sender || invocation.Row.Type != type ||
            invocation.Row.DataAddress != pointer || invocation.Row.Data.Length != length)
            throw new InvalidDataException("Original NVSE message returned without its actual dispatch/function owner.");
        _deliveries.Add(new(callback, parent, sequence, function, sender, type, pointer, length)); return 1;
    }
    internal uint BeginSerialization(ulong parent, NativeNvseSerializationEvent @event, uint function)
    {
        RequireLive();
        if (!_serialization.TryGetValue(@event, out var current) || current != function ||
            !_pendingSerialization.TryAdd(parent, (@event, function)))
            throw new InvalidDataException("Original NVSE serialization invocation has no exact callback/transaction owner.");
        return 1;
    }
    internal uint CompleteSerialization(ulong callback, ulong parent, NativeNvseSerializationEvent @event, uint function)
    {
        RequireLive();
        if (!_pendingSerialization.Remove(parent, out var invocation) || invocation.Event != @event || invocation.Function != function)
            throw new InvalidDataException("Original NVSE serialization returned without its retained callback owner.");
        _serializationDeliveries.Add(new(callback, parent, @event, function)); return 1;
    }
    internal void Missing(ulong callback, string operation) { RequireLive(); _unowned.Add(new(callback, operation)); }
    internal void RequireCompletedInvocations()
    {
        RequireLive();
        if (_pendingMessages.Count != 0 || _pendingSerialization.Count != 0)
            throw new InvalidDataException("Original NVSE callback invocation has no completed return receipt.");
    }
    internal void Retire()
    {
        if (_retired) return;
        _retired = true; _listeners.Clear(); _serialization.Clear(); _opcodes.Clear(); _nextOpcode = null;
        _pendingMessages.Clear(); _pendingSerialization.Clear();
        // Immutable history remains evidence; no live callback pointer can be
        // invoked through it once its actual module capability is retired.
    }
    private void RequireLoad()
    {
        RequireLive();
        if (plugin.Phase != NativeNvsePhase.Loading) throw new InvalidOperationException("NVSE command registration is outside actual plugin Load.");
    }
    private void RequireLive()
    {
        if (_retired || plugin.Phase is NativeNvsePhase.Retired or NativeNvsePhase.Faulted)
            throw new InvalidOperationException("NVSE registry belongs to a retired/faulted image generation.");
    }
}
