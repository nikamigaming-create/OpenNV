using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// The declaring consumer supplies the genuine ABI. A PE address alone cannot
// declare a signature, a game receiver, a task queue or a persistent pointer.
internal sealed record NativeNvseFunctionDeclaration(uint SourceRva, NativePluginAbi Abi, uint StackWords, string? Export = null);
internal sealed class NativeNvseFunctionBorrow(ulong generation, ulong id, NativeNvsePlugin caller,
    NativeNvsePlugin provider, NativeNvseFunctionDeclaration declaration, uint address)
{
    internal ulong Generation { get; } = generation;
    internal ulong Id { get; } = id;
    internal NativeNvsePlugin Caller { get; } = caller;
    internal NativeNvsePlugin Provider { get; } = provider;
    internal NativeNvseFunctionDeclaration Declaration { get; } = declaration;
    internal uint Address { get; } = address;
    internal bool Live { get; set; } = true;
    internal bool Entered { get; set; }
}
internal readonly record struct NativeNvseFunctionReceipt(uint RawEax, int StackDelta, uint PreservedRegisters, uint ExceptionCode);

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeNvseFunctionBorrow> _nvseFunctionBorrows = [];

    internal NativeNvseFunctionBorrow BorrowNvseFunction(NativeNvsePlugin caller, NativeNvsePlugin provider,
        NativeNvseFunctionDeclaration declaration)
    {
        VerifyNvse(caller); VerifyNvse(provider); RequireNvseLoaded(caller); RequireNvseLoaded(provider); RequireNvseEmptyCall();
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.SourceRva == 0 || declaration.StackWords > 4 || !Enum.IsDefined(declaration.Abi))
            throw new ArgumentException("Original function requires its known source RVA and zero through four raw stack words.", nameof(declaration));
        if (declaration.Export is { } name)
        {
            var export = _nvseModuleDeclarations[provider.Module].Exports.SingleOrDefault(row => row.Name == name);
            if (export is not { Executable: true, Forwarded: false } || export.Rva != declaration.SourceRva)
                throw new InvalidDataException("Original function changed its direct selected named export.");
        }
        else RequireNvseSourceCodeRva(provider, declaration.SourceRva);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            {
                writer.Write(1U); writer.Write(caller.Module); writer.Write(provider.Module);
                WriteText(writer, declaration.Export ?? string.Empty); writer.Write(declaration.SourceRva);
                writer.Write((uint)declaration.Abi); writer.Write(declaration.StackWords);
            }));
            var id = reader.ReadUInt64(); var address = reader.ReadUInt32(); Finish(reader);
            if (id == 0 || (ulong)provider.Image + declaration.SourceRva != address || _nvseFunctionBorrows.ContainsKey(id))
                throw new InvalidDataException("Original callable lost its actual process, source address or borrow identity.");
            var result = new NativeNvseFunctionBorrow(Generation, id, caller, provider, declaration, address);
            _nvseFunctionBorrows.Add(id, result); return result;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception failure) { throw Fatal(failure); }
    }
    internal NativeNvseFunctionReceipt InvokeNvseFunction(NativeNvseFunctionBorrow function,
        NativePluginGuestAllocation? receiver, ReadOnlySpan<uint> words)
    {
        VerifyNvseFunction(function); RequireNvseEmptyCall();
        if (function.Declaration.StackWords != words.Length || (function.Declaration.Abi == NativePluginAbi.Thiscall) != (receiver is not null))
            throw new ArgumentException("Original function requires its declared raw-word count and actual receiver lifetime.");
        if (receiver is not null) VerifyGuest(receiver);
        var arguments = words.ToArray(); function.Entered = true; ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            {
                writer.Write(2U); writer.Write(function.Id); writer.Write(receiver?.Handle ?? 0);
                writer.Write(receiver?.Address ?? 0); writer.Write(receiver?.Length ?? 0);
                writer.Write(checked((uint)arguments.Length)); foreach (var word in arguments) writer.Write(word);
            }));
            var result = new NativeNvseFunctionReceipt(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            Finish(reader); CheckNvseBoundary(result.StackDelta, result.PreservedRegisters, result.ExceptionCode);
            VerifyOwner(); function.Entered = false; return result;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception failure) { throw Fatal(failure); }
        finally { --_callDepth; }
    }
    internal void ReleaseNvseFunction(NativeNvseFunctionBorrow function)
    {
        VerifyNvseFunction(function); RequireNvseEmptyCall();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            { writer.Write(3U); writer.Write(function.Id); }));
            if (reader.ReadUInt32() != 1) throw new InvalidDataException("Original function borrow did not retire.");
            Finish(reader); function.Live = false; _nvseFunctionBorrows.Remove(function.Id);
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception failure) { throw Fatal(failure); }
    }
    private void VerifyNvseFunction(NativeNvseFunctionBorrow function)
    {
        VerifyOwner();
        if (function.Generation != Generation || !function.Live || function.Entered ||
            !_nvseFunctionBorrows.TryGetValue(function.Id, out var retained) || !ReferenceEquals(function, retained))
            throw new InvalidOperationException("Original function borrow is foreign, retired or retains an entered call.");
        VerifyNvse(function.Caller); VerifyNvse(function.Provider);
        RequireNvseLoaded(function.Caller); RequireNvseLoaded(function.Provider);
    }
    private static void RequireNvseSourceCodeRva(NativeNvsePlugin plugin, uint rva)
    {
        using var source = new FileStream(plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(source);
        var code = pe.PEHeaders.SectionHeaders.Any(section => section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) &&
            section.VirtualAddress >= 0 && section.SizeOfRawData > 0 && rva >= section.VirtualAddress &&
            (ulong)rva - (uint)section.VirtualAddress < (uint)section.SizeOfRawData);
        if (!code) throw new InvalidDataException("Original function RVA is outside its file-backed executable source section.");
    }
    private void RequireNvseFunctionsIdle(NativeNvsePlugin plugin)
    {
        if (_nvseFunctionBorrows.Values.Any(function => function.Live &&
            (ReferenceEquals(function.Caller, plugin) || ReferenceEquals(function.Provider, plugin))))
            throw new InvalidOperationException("Original module still owns a live cross-image function borrow.");
    }
    internal void RequireNvseModuleSetColdState()
    {
        VerifyOwner(); RequireNvseEmptyCall();
        if (_nvseFunctionBorrows.Values.Any(function => function.Live || function.Entered) || _nvseModules.Count > 1)
            throw new NotSupportedException("Original module-set cold reconstruction requires current native globals, loader/callback order and pointer relocation; no whole mod save is verified.");
    }
}
