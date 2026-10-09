using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// All fields, including reserved runtime words and native pointer targets,
// need actual source/runtime owners. Missing fields cannot default to zero.
internal enum NativeNvseSourceClass : uint { Script = 1, ModInfo = 2, Quest = 3 }
internal sealed record NativeNvseScriptVariable(ReadOnlyMemory<byte> ScalarBytes, ReadOnlyMemory<byte> Name);
internal sealed record NativeNvseScriptReference(ReadOnlyMemory<byte> Name, NativeNvseSourceObject? Form, uint Variable);
internal sealed record NativeNvseScriptSnapshot(uint FormId, uint Flags, ReadOnlyMemory<byte> FormRuntimeBytes,
    ReadOnlyMemory<byte> Info, ReadOnlyMemory<byte>? Text, ReadOnlyMemory<byte> Code,
    uint RuntimeWord, float DelayCounter, float SecondsPassed, NativeNvseSourceObject? Quest,
    IReadOnlyList<NativeNvseSourceObject> Contributors, IReadOnlyList<NativeNvseScriptReference> References,
    IReadOnlyList<NativeNvseScriptVariable> Variables, ReadOnlyMemory<byte> EditorId, string RuntimeFieldOwner);

internal abstract class NativeNvseSourceObjectAuthority
{
    internal abstract string SourceOwner { get; }
    internal abstract string SourceSha256 { get; }
    internal abstract void RequireCurrent();
    internal abstract IDisposable RetainSource();
}

internal abstract class NativeNvseScriptAuthority : NativeNvseSourceObjectAuthority
{
    internal abstract string CodeSha256 { get; }
    internal abstract NativeNvseScriptSnapshot Read();
}
internal sealed record NativeNvseDataField(int Offset, ReadOnlyMemory<byte> Bytes, string Owner);
internal sealed record NativeNvseDataSnapshot(NativeNvseSourceClass Class, uint FormId, int Extent,
    string LayoutOwner, IReadOnlyList<NativeNvseDataField> Fields, ReadOnlyMemory<byte> EditorId,
    IReadOnlyList<NativeNvseSourceObject> Dependencies);
internal abstract class NativeNvseDataAuthority : NativeNvseSourceObjectAuthority
{
    internal abstract NativeNvseDataSnapshot Read();
}

internal sealed class NativeNvseSourceObject
{
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal ulong Id { get; }
    internal uint FormId { get; }
    internal NativeNvseSourceClass Class { get; }
    internal uint Address => Image.Address;
    internal NativePluginGuestAllocation Image { get; set; }
    internal NativePluginGuestAllocation Metadata { get; set; }
    internal NativePluginGuestAllocation? Code { get; set; }
    internal NativeNvseSourceObjectAuthority Authority { get; }
    internal IDisposable SourceLease { get; }
    internal string PublishedSha256 { get; set; }
    internal ImmutableArray<NativeNvseSourceObject> Dependencies { get; set; }
    internal uint Dependents { get; set; }
    internal uint Calls { get; set; }
    internal bool Retired { get; set; }
    internal bool Staged { get; set; }

    internal NativeNvseSourceObject(ulong generation, ulong module, ulong id, uint formId,
        NativePluginGuestAllocation image, NativePluginGuestAllocation metadata, NativePluginGuestAllocation? code,
        NativeNvseSourceObjectAuthority authority, IDisposable lease, IEnumerable<NativeNvseSourceObject> dependencies, string publishedSha256,
        NativeNvseSourceClass sourceClass = NativeNvseSourceClass.Script)
    {
        Generation = generation; Module = module; Id = id; FormId = formId;
        Image = image; Metadata = metadata; Code = code; Authority = authority; SourceLease = lease; Class = sourceClass;
        PublishedSha256 = publishedSha256;
        Dependencies = ImmutableArray.CreateRange(dependencies.Distinct());
    }
}

internal sealed record NativeNvseObjectMethodReceipt(ulong Callback, ulong Caller, ulong Object, uint Method, uint Result);
