namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CommandTableEvent = 0x480;
    private NativeNvseCommandTableAuthority? _nvseCommandTable;
    private readonly Dictionary<uint, NativeNvseCommandPublication> _nvseCommandPublications = [];
    private readonly Dictionary<uint, NativeNvsePluginPublication> _nvsePluginPublications = [];
    private readonly List<NativeNvseCommandTableCallback> _nvseCommandTableCallbacks = [];
    internal IReadOnlyList<NativeNvseCommandTableCallback> NvseCommandTableCallbacks => _nvseCommandTableCallbacks.AsReadOnly();

    internal void AttachNvseCommandTable(NativeNvsePlugin plugin, NativeNvseCommandTableAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(authority);
        if (_nvseCommandTable is not null || plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue))
            throw new InvalidOperationException("The actual command-table authority attaches once before original Query/Load.");
        authority.RequireCurrent();
        if (!StringComparer.OrdinalIgnoreCase.Equals(authority.SourceIdentity, _nvseHostSource!.StackIdentity))
            throw new InvalidDataException("Native command table differs from the retained selected host/source graph.");
        _nvseCommandTable = authority;
    }

    private byte[] DispatchNvseCommandTable(Frame frame, ulong parent)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("CommandTable callback has no original module owner.");
        VerifyNvse(plugin);
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle)
            throw new InvalidDataException("CommandTable callback belongs to a foreign generation/thread/module/handle.");
        var call = (NativeNvseCommandTableCall)reader.ReadUInt32();
        var argument = reader.ReadUInt32(); var nativeAddress = reader.ReadUInt32(); var name = ReadNvseText(reader);
        var source = reader.ReadUInt32(); var registration = reader.ReadUInt64();
        NativeNvseCommand? command = null;
        try
        {
            var authority = _nvseCommandTable ?? throw new NotSupportedException("CommandTable has no actual source registration/loaded-prefix authority.");
            authority.RequireCurrent();
            switch (call)
            {
                case NativeNvseCommandTableCall.Start:
                case NativeNvseCommandTableCall.End:
                    throw new NotSupportedException("CommandTable Start/End requires the complete contiguous original core/padding/plugin table and its used handler/parser owners.");
                case NativeNvseCommandTableCall.ByOpcode:
                case NativeNvseCommandTableCall.ByName:
                    command = call == NativeNvseCommandTableCall.ByOpcode ? authority.ByOpcode(argument) : authority.ByName(name);
                    if (command is not null) RequireLocalCommand(command, plugin);
                    Record(); return Payload(writer =>
                    {
                        writer.Write(command is not null ? 1U : 0U);
                        if (command is null) return;
                        writer.Write(command.SourceAddress); writer.Write(command.AssignedOpcode); writer.Write(command.Registration);
                        writer.Write((uint)command.ReturnType);
                    });
                case NativeNvseCommandTableCall.PublishCommand:
                    command = authority.ByOpcode(argument) ?? throw new InvalidDataException("Native command publication has no actual registration.");
                    RequireLocalCommand(command, plugin);
                    if (nativeAddress == 0 || command.SourceAddress != source || command.Registration != registration ||
                        _nvseCommandPublications.Values.Any(row => row.Command.Registration == registration && row.Address != nativeAddress))
                        throw new InvalidDataException("Native command publication changed its registered source/ordinal/pointer lifetime.");
                    RequirePublicationDeclaration(reader, command);
                    var publication = new NativeNvseCommandPublication(nativeAddress, command);
                    if (_nvseCommandPublications.TryGetValue(nativeAddress, out var previous) && previous != publication)
                        throw new InvalidDataException("Native command pointer aliases another registered owner.");
                    _nvseCommandPublications[nativeAddress] = publication;
                    Record(); return Payload(writer => writer.Write(1U));
                case NativeNvseCommandTableCall.ReturnType:
                case NativeNvseCommandTableCall.RequiredVersion:
                case NativeNvseCommandTableCall.ParentPlugin:
                    if (nativeAddress == 0 && call == NativeNvseCommandTableCall.RequiredVersion)
                    { Record(); return Payload(writer => writer.Write(0U)); }
                    if (!_nvseCommandPublications.TryGetValue(nativeAddress, out var retained) || retained.Command.AssignedOpcode != argument)
                        throw new InvalidDataException("CommandTable metadata pointer has no retained actual publication.");
                    command = authority.ByOpcode(argument) ?? throw new InvalidDataException("Published native command lost its live registration.");
                    RequireLocalCommand(command, plugin);
                    if (!ReferenceEquals(command, retained.Command))
                        throw new InvalidDataException("Published native command has an unjoined replacement/mutation owner.");
                    if (call == NativeNvseCommandTableCall.ParentPlugin)
                    { var owner = authority.Parent(command); Record(); return PluginReply(owner); }
                    if (call == NativeNvseCommandTableCall.RequiredVersion && command.AssignedOpcode < 0x2000)
                        throw new NotSupportedException("CommandTable required release requires its original core release-boundary catalogue.");
                    Record(); return Payload(writer => writer.Write(call == NativeNvseCommandTableCall.ReturnType ? (uint)command.ReturnType : uint.MaxValue));
                case NativeNvseCommandTableCall.PluginByName:
                case NativeNvseCommandTableCall.PluginByDll:
                    var found = authority.Plugin(name, call == NativeNvseCommandTableCall.PluginByDll);
                    Record(); return PluginReply(found);
                case NativeNvseCommandTableCall.PublishPlugin:
                    var loaded = authority.LoadedModules().SingleOrDefault(row => row.Generation == registration && row.Module == source && row.Handle == argument)
                        ?? throw new InvalidDataException("PluginInfo publication has no actual completed module source owner.");
                    if (nativeAddress == 0 || name.IsNull || !name.Bytes.SequenceEqual(loaded.Info.Name.Bytes))
                        throw new InvalidDataException("Native PluginInfo name/pointer differs from its actual original Query receipt.");
                    var info = new NativeNvsePluginPublication(nativeAddress, loaded, name.Address, name.Bytes);
                    if (_nvsePluginPublications.TryGetValue(nativeAddress, out var old) && old != info)
                        throw new InvalidDataException("Native PluginInfo pointer aliases a different original module identity.");
                    _nvsePluginPublications[nativeAddress] = info;
                    Record(); return Payload(writer => writer.Write(1U));
                default: throw new InvalidDataException("Unknown public CommandTable callback operation.");
            }
        }
        catch (Exception error)
        {
            _nvseCommandTableCallbacks.Add(new(Generation, frame.Id, parent, call, argument, nativeAddress, command?.AssignedOpcode, command?.Registration, error.Message));
            if (error is NotSupportedException) plugin.Registry.Missing(frame.Id, error.Message);
            throw;
        }
        void Record()
        {
            Finish(reader);
            _nvseCommandTableCallbacks.Add(new(Generation, frame.Id, parent, call, argument, nativeAddress, command?.AssignedOpcode, command?.Registration, null));
        }
    }
    private static byte[] PluginReply(NativeNvseCommandTableModule? module) => Payload(writer =>
    {
        writer.Write(module is not null ? 1U : 0U); if (module is null) return;
        writer.Write(module.Generation); writer.Write(module.Module); writer.Write(module.Handle);
        writer.Write(module.Info.InfoVersion); writer.Write(module.Info.Version);
        writer.Write(checked((uint)module.Info.Name.Bytes.Length)); writer.Write(module.Info.Name.Bytes.AsSpan());
    });
    private void RequireLocalCommand(NativeNvseCommand command, NativeNvsePlugin plugin)
    {
        if (command.Generation != Generation || command.Module != plugin.Module || command.PluginHandle != plugin.Handle ||
            !plugin.Registry.Commands.Any(row => ReferenceEquals(row, command)))
            throw new NotSupportedException("CommandTable command belongs to another real x86 child; cross-domain COMMAND_ARGS/object/parser/hook execution is not owned.");
    }
    private static void RequirePublicationDeclaration(BinaryReader reader, NativeNvseCommand command)
    {
        var opcode = reader.ReadUInt32(); var parent = reader.ReadUInt16(); var count = reader.ReadUInt16();
        var flags = reader.ReadUInt32(); var execute = reader.ReadUInt32(); var parse = reader.ReadUInt32(); var evaluate = reader.ReadUInt32();
        var name = ReadNvseText(reader); var alias = ReadNvseText(reader); var help = ReadNvseText(reader); var parameters = reader.ReadUInt32();
        if (opcode != command.RawOpcode || parent != command.NeedsParent || count != command.Parameters.Length || flags != command.Flags ||
            execute != command.Execute || parse != command.Parse || evaluate != command.Evaluate || parameters != command.ParameterAddress ||
            !Same(name, command.Name) || !Same(alias, command.Alias) || !Same(help, command.Help))
            throw new InvalidDataException("CommandTable source fields/strings drifted from their original registration transaction.");
        for (var index = 0; index < count; ++index)
        {
            var parameter = ReadNvseText(reader); var type = reader.ReadUInt32(); var optional = reader.ReadUInt32(); var original = command.Parameters[index];
            if (!Same(parameter, original.Name) || type != original.Type || optional != original.Optional)
                throw new InvalidDataException("CommandTable source parameter differs from its original registration extent.");
        }
        static bool Same(NativeNvseText first, NativeNvseText second) => first.Address == second.Address && first.Bytes.SequenceEqual(second.Bytes);
    }
    private void ClearNvseCommandTable()
    {
        _nvseCommandPublications.Clear(); _nvsePluginPublications.Clear(); _nvseCommandTable = null;
        // Histories survive retirement; their pointers cannot be invoked again.
    }
}
