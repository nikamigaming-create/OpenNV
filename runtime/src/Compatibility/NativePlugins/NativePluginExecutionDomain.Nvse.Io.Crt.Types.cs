namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginCrtProviderSelection(string Path, string Sha256, string SourceOwner,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Imports);
internal enum NativePluginCrtOperation : uint
{
    Open = 1, Close = 2, Read = 3, Write = 4, Seek = 5, Tell = 6, Rewind = 7,
    Flush = 8, PutCharacter = 9, PutString = 10, FormattedWrite = 11,
    EndOfFile = 12, Error = 13, ClearError = 14, MakeDirectory = 15,
    GetCharacter = 16, PushCharacter = 17, GetPosition = 18, SetPosition = 19,
    SetBuffer = 20, BufferCells = 21, Lock = 22, Unlock = 23,
}
internal sealed record NativePluginCrtStatus(int Errno, uint DosError, bool StreamStatusAvailable, int EndOfFile, int Error);
internal sealed record NativePluginCrtReceipt(ulong Sequence, ulong Generation, ulong Parent, ulong Provider,
    ulong Route, uint Stream, NativePluginCrtOperation Operation, ulong Argument, ulong Requested,
    long Result, NativePluginCrtStatus Status, bool StreamRetired, string DeclarationOwner);
internal sealed record NativePluginCrtStream(ulong Provider, ulong Route, uint Address, bool Writable,
    string Mode, string SourceOwner);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CrtProvider = 5, CrtRoute = 6, CrtOpened = 7, CrtEvent = 8, CrtPathResult = 9;
    private readonly Dictionary<ulong, (NativePluginCrtProviderSelection Selection, uint Module)> _crtProviders = [];
    private readonly Dictionary<ulong, NativePluginCrtStream> _crtStreams = [];
    private readonly Dictionary<ulong, (ulong Provider, string Mode, bool Writable)> _crtRoutes = [];
    internal IReadOnlyList<NativePluginCrtReceipt> NvseCrtReceipts => _privateIo?.CrtReceipts ?? [];
}
