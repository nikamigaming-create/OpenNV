using System.Collections.Immutable;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private FalloutEngineCommandBooleanSource? _nvseEngineCommandSource;
    private readonly Dictionary<uint, NativeNvseEngineCommandBinding> _nvseEngineCommands = [];
    private readonly List<NativeNvseEngineCommandEvent> _nvseEngineCommandEvents = [];
    private uint _nextNvseEngineCommand;
    internal IReadOnlyList<NativeNvseEngineCommandEvent> NvseEngineCommandEvents => _nvseEngineCommandEvents.AsReadOnly();
    private uint DispatchNvseEngineCommand(NativeNvseHostCall operation, Frame frame, ulong parent, BinaryReader reader)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Engine command callback lost its actual original module.");
        var host = _nvseHostSource ?? throw new InvalidDataException("Engine command callback lost its retained host source.");
        host.Check(Generation); NativeNvseEngineCommandBinding? binding = null; var arguments = ImmutableArray<uint>.Empty;
        bool? result = null; uint sourceAddress = 0;
        try
        {
            if (operation == NativeNvseHostCall.EngineCommandDeclare)
            {
                sourceAddress = reader.ReadUInt32(); var descriptor = reader.ReadUInt32(); var role = reader.ReadUInt32();
                if (plugin.Phase != NativeNvsePhase.Loading || descriptor == 0 || sourceAddress == 0 || role != 1)
                    throw new NotSupportedException("Borrowed command publication requires its actual Load execute declaration and known public ABI.");
                if (_nvseEngineCommands.TryGetValue(sourceAddress, out binding))
                {
                    if (!binding.Live) throw new InvalidDataException("Borrowed engine command attempted to replay a failed or retired publication.");
                }
                else
                {
                    if (_nextNvseEngineCommand >= 1024) throw new NotSupportedException("Engine command callable lifetime budget is exhausted.");
                    _nvseEngineCommandSource ??= FalloutExecutableStringTable.ReadEngineCommandBooleanSource(host.RuntimePath, host.RuntimeSha256);
                    var source = _nvseEngineCommandSource.Read(sourceAddress);
                    binding = new(checked(++_nextNvseEngineCommand), Generation, plugin.Module, NativeThread, source);
                    _nvseEngineCommands.Add(sourceAddress, binding);
                }
                binding.SourceDescriptors.Add(descriptor); Finish(reader); Record(); return binding.Id;
            }
            var id = reader.ReadUInt32(); sourceAddress = reader.ReadUInt32();
            if (!_nvseEngineCommands.TryGetValue(sourceAddress, out binding) || binding.Id != id || binding.Generation != Generation ||
                binding.Module != plugin.Module || binding.Thread != NativeThread || binding.NativeRetired || binding.ClosedByProcessExit)
                throw new InvalidDataException("Engine command capability is foreign, retired or changed its source/thread generation.");
            if (operation == NativeNvseHostCall.EngineCommandPublish)
            {
                var lease = reader.ReadUInt64(); var target = reader.ReadUInt32();
                if (binding.NativePublished || lease == 0 || target == 0 || plugin.Phase != NativeNvsePhase.Loading)
                    throw new InvalidDataException("Engine command publication repeated or lost its actual native callable lease.");
                _process.RequireEngineCommandCallable(sourceAddress, target, retired: false);
                binding.CallableLease = lease; binding.NativeTarget = target; binding.NativePublished = true;
                Finish(reader); Record(); return 1;
            }
            if (!binding.Live) throw new InvalidDataException("Engine command use lacks an actual executable first-party publication.");
            if (operation == NativeNvseHostCall.EngineCommandExecute)
            {
                var values = ImmutableArray.CreateBuilder<uint>(8);
                for (var at = 0; at < 8; ++at) values.Add(reader.ReadUInt32());
                arguments = values.MoveToImmutable();
                _process.RequireEngineCommandCallable(sourceAddress, binding.NativeTarget, retired: false);
                // The inspected source never reads COMMAND_ARGS or mutates a
                // result/cursor/object. Those pointers retain their raw identity;
                // this operation neither projects an actor nor invokes a parser.
                result = binding.Source.Result; Finish(reader); Record(); return result.Value ? 1U : 0U;
            }
            if (operation == NativeNvseHostCall.EngineCommandRetire)
            {
                var lease = reader.ReadUInt64();
                if (_operation != NativePluginDomainOperation.UnloadNvse.ToString() || lease != binding.CallableLease)
                    throw new InvalidDataException("Engine command retirement escaped the real original-module unload transaction.");
                _process.RequireEngineCommandCallable(sourceAddress, binding.NativeTarget, retired: true);
                binding.NativeRetired = true; Finish(reader); Record(); return 1;
            }
            throw new InvalidDataException("Engine command callback operation is unknown.");
        }
        catch (Exception failure) { if (binding is not null) binding.Failed = true; Record(failure.Message); throw; }
        void Record(string? failure = null) => _nvseEngineCommandEvents.Add(new(Generation, plugin.Module, NativeThread,
            frame.Id, parent, operation, binding?.Id ?? 0, sourceAddress, host.RuntimeSha256, binding?.Source.BodySha256 ?? "",
            result, arguments, binding?.CallableLease ?? 0, binding?.NativeTarget ?? 0, failure));
    }
    private NativeNvseEngineCommandBinding? EngineCommandBinding(uint execute)
    {
        if (!_nvseEngineCommands.TryGetValue(execute, out var binding)) return null;
        if (!binding.Live || binding.Generation != Generation || binding.Module != _nvsePlugin?.Module)
            throw new InvalidDataException("Registered borrowed command has no current source/first-party callable owner.");
        return binding;
    }
    private bool HasEngineCommandCaller(NativeNvseCommand command) => command.EngineExecute is { } binding &&
        ReferenceEquals(binding, EngineCommandBinding(command.Execute)) && binding.Registrations.Any(value => ReferenceEquals(value, command)) &&
        command.Parameters.IsEmpty && command.NeedsParent == 0 && command.ReturnType == NativeNvseCommandReturn.Default;
    private void RequireEngineCommandInvocation(NativeNvseExpressionCaller caller, int firstEvent, ulong parent, uint raw, double number, uint end)
    {
        if (caller.Command.EngineExecute is not { } binding) return;
        if (!HasEngineCommandCaller(caller.Command)) throw new InvalidDataException("Engine Boolean command changed its genuine registered caller owner.");
        var calls = _nvseEngineCommandEvents.Skip(firstEvent).Where(row => row.Event == NativeNvseHostCall.EngineCommandExecute && row.Binding == binding.Id).ToArray();
        if (calls.Length != 1 || calls[0].Failure is not null || calls[0].BooleanResult != binding.Source.Result ||
            calls[0].Generation != Generation || calls[0].Module != caller.Command.Module || calls[0].Thread != NativeThread || calls[0].Parent != parent ||
            calls[0].Arguments.Length != 8 || calls[0].Arguments[0] != caller.NativeParameters ||
            calls[0].Arguments[1] != caller.ScriptData.Address || calls[0].Arguments[2] != 0 || calls[0].Arguments[3] != 0 ||
            calls[0].Arguments[4] != caller.NativeScript || calls[0].Arguments[5] != caller.NativeEventList ||
            calls[0].Arguments[6] != caller.NativeResult || calls[0].Arguments[7] != caller.NativeOffset ||
            (raw & 255) != (binding.Source.Result ? 1U : 0U) || BitConverter.DoubleToUInt64Bits(number) != 0 ||
            end != caller.Arguments.StartOffset || caller.Created != 0 || caller.Destroyed != 0 || caller.Tokens != 0 || caller.PublishedValue is not null)
            throw new InvalidDataException("Engine Boolean command has no exact correlated semantic call/result/cursor receipt.");
    }
    private void RequireEngineCommandRetirement()
    {
        if (_nvseEngineCommands.Values.Any(binding => !binding.NativeRetired))
            throw new InvalidDataException("Original-module retirement still owns a live or failed borrowed engine callable.");
        _nvseEngineCommandSource = null;
    }
    private void ClearEngineCommandsAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Engine callable source owners must survive until exact child closure.");
        foreach (var binding in _nvseEngineCommands.Values) binding.ClosedByProcessExit = true;
        _nvseEngineCommandSource = null;
        // Keep every declaration/publication/call/refusal and retirement row.
    }
}
