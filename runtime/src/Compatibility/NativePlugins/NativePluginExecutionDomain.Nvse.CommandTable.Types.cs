using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseCommandTableCall : uint
{
    Start = 1, End, ByOpcode, ByName, ReturnType, RequiredVersion,
    ParentPlugin, PluginByName, PluginByDll, PublishCommand, PublishPlugin,
}
internal sealed record NativeNvseCommandTableModule(ulong Generation, ulong Module,
    uint Handle, string SourcePath, string SourceSha256, NativeNvsePluginInfo Info);
internal sealed record NativeNvseCommandTableCallback(ulong Generation, ulong Callback,
    ulong Parent, NativeNvseCommandTableCall Call, uint Argument, uint NativeAddress,
    uint? Opcode, ulong? Registration, string? Failure);

// The module list is the actual completed loader prefix; it does not include
// a currently-loading or merely queried plugin. Commands separately retain
// their actual registration prefix, including the current Load transaction.
// A partial registry may answer an exact occupied opcode. It cannot certify
// a global name winner, an absent opcode, or a complete contiguous table.
internal abstract class NativeNvseCommandTableAuthority
{
    internal abstract string SourceIdentity { get; }
    internal abstract void RequireCurrent();
    internal abstract IReadOnlyList<NativeNvseCommand> Commands();
    internal abstract IReadOnlyList<NativeNvseCommandTableModule> LoadedModules();
    internal virtual bool NameWinnersComplete => false;
    internal virtual bool OpcodeDomainComplete => false;

    internal NativeNvseCommand? ByOpcode(uint opcode)
    {
        RequireCurrent();
        var matches = Commands().Where(command => command.AssignedOpcode == opcode).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Native command opcode has conflicting live registration owners.");
        if (matches.Length == 1) return matches[0];
        if (!OpcodeDomainComplete)
            throw new NotSupportedException("CommandTable absent opcode requires the complete original core/padding/selected-module command owner.");
        return null;
    }
    internal NativeNvseCommand? ByName(NativeNvseText name)
    {
        RequireCurrent(); RequireIdentifier(name);
        if (!NameWinnersComplete)
            throw new NotSupportedException("CommandTable name lookup requires the original global command-name/alias cache winner, including core commands and versioned replacement.");
        var matches = Commands().Where(command => Same(name, command.Name) || Same(name, command.Alias)).ToArray();
        if (matches.Length > 1)
            throw new NotSupportedException("CommandTable repeated name/alias requires its source cache insertion/replacement owner.");
        return matches.SingleOrDefault();
    }
    internal NativeNvseCommandTableModule? Plugin(NativeNvseText name, bool dll)
    {
        RequireCurrent(); RequireIdentifier(name);
        var matches = LoadedModules().Where(module => dll
            ? SameDll(module.SourcePath, name.Display)
            : Same(name, module.Info.Name)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Native loaded-plugin name is ambiguous in its actual loader prefix.");
        return matches.SingleOrDefault();
    }
    internal NativeNvseCommandTableModule? Parent(NativeNvseCommand command)
    {
        RequireCurrent();
        if (command.AssignedOpcode < 0x2000)
            throw new NotSupportedException("CommandTable core parent/version metadata has no admitted original core table owner.");
        // Public GetInfoFromBase observes completed plugins, not the current
        // loading descriptor. A null here is that actual loader disposition.
        var matches = LoadedModules().Where(module => module.Generation == command.Generation && module.Module == command.Module).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Native command parent has multiple live module owners.");
        return matches.SingleOrDefault();
    }
    private static void RequireIdentifier(NativeNvseText name)
    {
        if (name.IsNull || !name.IsAscii || name.Bytes.IsEmpty)
            throw new NotSupportedException("CommandTable identifier needs its nonnull source/locale owner.");
    }
    private static bool Same(NativeNvseText first, NativeNvseText second) =>
        second.IsAscii && !second.IsNull && StringComparer.OrdinalIgnoreCase.Equals(first.Display, second.Display);
    private static bool SameDll(string selectedPath, string declared)
    {
        if (declared.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            if (!Path.IsPathFullyQualified(declared))
                throw new NotSupportedException("DLL-name lookup has no exact module owner for a relative qualified path.");
            return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(selectedPath), Path.GetFullPath(declared));
        }
        // GetModuleHandle permits an omitted extension. These are module-name
        // spellings of the actual loaded source, never catalogue substitutions.
        var leaf = Path.GetFileName(selectedPath);
        return StringComparer.OrdinalIgnoreCase.Equals(leaf, declared) ||
            Path.GetExtension(declared).Length == 0 && StringComparer.OrdinalIgnoreCase.Equals(leaf, declared + ".dll");
    }
}

internal sealed record NativeNvseCommandPublication(uint Address, NativeNvseCommand Command);
internal sealed record NativeNvsePluginPublication(uint Address, NativeNvseCommandTableModule Module,
    uint NameAddress, ImmutableArray<byte> Name);
