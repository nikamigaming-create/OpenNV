using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// A peer borrows actual code/data exports from the already retained provider.
// This is independent of the primary's FILE/path policy and C++ object state.
internal sealed class NativePluginModuleCppSource
{
    internal NativePluginCppRuntimeSelection Provider { get; }
    private readonly NativePluginImageDeclaration _image;
    private readonly HashSet<string> _exports;
    private NativePluginModuleCppSource(NativePluginImageDeclaration image, NativePluginCppRuntimeSelection provider)
    { _image = image; Provider = provider; _exports = provider.Exports.Select(row => row.Name).ToHashSet(StringComparer.Ordinal); }
    internal static NativePluginModuleCppSource? Read(NativePluginImageDeclaration image)
    {
        var imports = image.Imports.Where(row => row.Library == NativePluginCppRuntimeImports.Library).ToArray();
        if (imports.Length == 0) return null;
        if (imports.Any(row => row.Name is null || !NativePluginCppRuntimeImports.Owns(row.Library, row.Name)))
            throw new NotSupportedException("Additional C++ import lacks its exact public x86 signature/code-data source owner.");
        var provider = NativePluginCppRuntimeImports.Read(image.Path, image.Sha256) ??
            throw new InvalidDataException("Additional C++ imports lost their selected original provider.");
        if (imports.Any(row => row.Name == NativePluginCppRuntimeImports.ErrorStream))
            throw new NotSupportedException("Additional C++ standard output requires its independent module-bound stream policy.");
        if (imports.Any(row => row.Name == NativePluginCppRuntimeImports.LocaleIdConversion)) RequireLocaleId(provider);
        return new(image, provider);
    }
    internal bool Owns(string library, string name) => library == NativePluginCppRuntimeImports.Library && _exports.Contains(name);
    internal void Check(NativePluginImageDeclaration image, NativePluginPrivateIo? io = null)
    {
        if (!_image.Path.Equals(image.Path, StringComparison.OrdinalIgnoreCase) || !_image.Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !_image.Imports.SequenceEqual(image.Imports)) throw new InvalidDataException("Additional C++ source belongs to another actual image/import graph.");
        if (io is null) return;
        io.RequireCppSourcesCurrent();
        var actual = io.CppProvider ?? throw new NotSupportedException("Additional C++ imports require the genuine existing shared provider lifetime.");
        if (!actual.Path.Equals(Provider.Path, StringComparison.OrdinalIgnoreCase) || actual.Sha256 != Provider.Sha256 ||
            !actual.UcrtPath.Equals(Provider.UcrtPath, StringComparison.OrdinalIgnoreCase) || actual.UcrtSha256 != Provider.UcrtSha256)
            throw new NotSupportedException("Additional C++ imports select a different actual shared object/UCRT provider.");
    }
    private static void RequireLocaleId(NativePluginCppRuntimeSelection provider)
    {
        var declaration = NativePluginImageDeclarations.Read(provider.Path, dll: true);
        if (declaration.Sha256 != provider.Sha256) throw new InvalidDataException("Locale id conversion changed its retained provider source.");
        var method = declaration.Exports.Single(row => row.Name == NativePluginCppRuntimeImports.LocaleIdConversion);
        var constructor = declaration.Exports.Single(row => row.Name == "??0_Lockit@std@@QAE@H@Z");
        var destructor = declaration.Exports.Single(row => row.Name == "??1_Lockit@std@@QAE@XZ");
        var counter = declaration.Exports.Single(row => row.Name == "?_Id_cnt@id@locale@std@@0HA");
        using var input = NativeNvseHostSource.Lease(provider.Path, provider.Sha256, dll: true);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var code = pe.GetSectionData(checked((int)method.Rva)).GetContent(0, 55).ToArray();
        Need(0, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x83, 0x3e, 0]);
        Need(12, [0x6a, 0, 0x8d, 0x4d, 0xfc]);
        Need(22, [0x83, 0x3e, 0, 0x75, 0x0d, 0xa1]);
        Need(32, [0x40, 0xa3]); Need(38, [0x89, 6, 0x8d, 0x4d, 0xfc]);
        Need(48, [0x8b, 6, 0x5e, 0x8b, 0xe5, 0x5d, 0xc3]);
        if (counter.Executable || method.Forwarded || constructor.Forwarded || destructor.Forwarded ||
            !method.Executable || !constructor.Executable || !destructor.Executable ||
            Relative(17) != constructor.Rva || Relative(43) != destructor.Rva ||
            Word(28) != pe.PEHeaders.PEHeader!.ImageBase + counter.Rva || Word(34) != Word(28) ||
            code[10] != 0x75 || code[11] != 0x24)
            throw new NotSupportedException("Locale id conversion lost its actual thiscall/lock/current-counter/return association.");
        return;
        void Need(int at, byte[] bytes)
        { if (!code.AsSpan(at, bytes.Length).SequenceEqual(bytes)) throw new NotSupportedException("Actual locale id conversion source shape is unowned."); }
        uint Relative(int at) => code[at] == 0xe8 ? checked((uint)((long)method.Rva + at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)))) :
            throw new NotSupportedException("Locale id conversion changed its actual provider child.");
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(at));
    }
}
