using System.Reflection.PortableExecutable;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// A bounded source producer selects a diagnostic path. It does not grant the
// additional image the primary module's writable-path or standard-FILE policy.
internal sealed class NativePluginModuleDiagnosticCrtSource
{
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string StackSha256 { get; }
    internal string Family => "PE32/module-diagnostic/fsopen-write/ordered-null-retries";
    internal uint QueryRva { get; }
    internal uint ProducerRva { get; }
    internal uint StreamRva { get; }
    internal uint NameRva { get; }
    internal uint ModeRva { get; }
    internal uint FirstReturnRva { get; }
    internal uint RetryReturnRva { get; }
    internal int Sharing { get; }
    internal uint RetryLimit { get; }
    internal string Name { get; }
    internal string Mode { get; }
    private readonly NativePluginImageImport[] _imports;
    private NativePluginModuleDiagnosticCrtSource(NativePluginImageDeclaration image, string stack,
        uint query, uint producer, uint stream, uint name, uint mode, int sharing, uint retries, string filename, string access)
    {
        Path = image.Path; Sha256 = image.Sha256; StackSha256 = stack.ToUpperInvariant(); QueryRva = query; ProducerRva = producer;
        StreamRva = stream; NameRva = name; ModeRva = mode; FirstReturnRva = producer + 39; RetryReturnRva = producer + 109;
        Sharing = sharing; RetryLimit = retries; Name = filename; Mode = access; _imports = image.Imports.ToArray();
    }
    internal static NativePluginModuleDiagnosticCrtSource? Read(NativePluginImageDeclaration image,
        string stack, NativePluginModuleConstructionSource construction)
    {
        construction.Check(image, stack);
        if (!image.Imports.Any(row => NativePluginCrtImports.StreamLibraries.Contains(row.Library) && row.Name == "_fsopen")) return null;
        var query = image.Exports.Single(row => row.Name == "NVSEPlugin_Query").Rva;
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var source = new NativePluginConstructorImage(pe, input, image);
        if (!source.Read(query, 4, true).AsSpan().SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0xe8 })) return null;
        var producer = source.Call(query + 3);
        if (!source.Read(producer, 9, true).AsSpan().SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x81, 0xec, 4, 4, 0, 0 })) return null;
        return Reduce(image, stack, source, query, producer);
    }
    private static NativePluginModuleDiagnosticCrtSource Reduce(NativePluginImageDeclaration image, string stack,
        NativePluginConstructorImage source, uint query, uint producer)
    {
        source.Need(producer + 9, [0xa1], "diagnostic stack cookie");
        source.Need(producer + 14, [0x33, 0xc5, 0x89, 0x45, 0xfc, 0x53, 0x6a, 0x20, 0xbb], "diagnostic sharing/mode operands");
        var mode = source.Address(producer, 23); var name = source.Push(producer + 28);
        source.Need(producer + 27, [0x53], "diagnostic mode argument");
        var open = source.ImportAt(producer + 33, "_fsopen");
        if (!NativePluginCrtImports.StreamLibraries.Contains(open.Library)) throw new NotSupportedException("Diagnostic FILE has no actual UCRT import.");
        source.Need(producer + 39, [0x83, 0xc4, 0x0c, 0xa3], "diagnostic actual FILE publication");
        var stream = source.Address(producer, 43); _ = source.Field(stream, 4, 0, "diagnostic-FILE");
        source.Need(producer + 47, [0x85, 0xc0, 0x75, 0x4c, 0x56, 0x33, 0xf6, 0x56, 0x68], "diagnostic null/retry branch");
        if (source.Address(producer, 56) != name) throw new NotSupportedException("Diagnostic retry changed its declared filename.");
        var format = source.Push(producer + 60);
        source.Need(producer + 65, [0x8d, 0x85, 0xfc, 0xfb, 0xff, 0xff, 0x68, 0, 4, 0, 0, 0x50], "diagnostic retry stack buffer");
        var formatter = source.Call(producer + 77);
        source.Need(producer + 82, [0x6a, 0x20, 0x8d, 0x85, 0xfc, 0xfb, 0xff, 0xff, 0xc7, 5], "diagnostic retry clear");
        if (source.Address(producer, 92) != stream || NativePluginConstructorImage.Word(source.Read(producer + 96, 4), 0) != 0 ||
            source.Address(producer, 105) != open.SlotRva)
            throw new NotSupportedException("Diagnostic retries lost their same FILE/import lifetime.");
        source.Need(producer + 100, [0x53, 0x50, 0x46], "diagnostic next original attempt");
        source.Need(producer + 103, [0xff, 0x15], "diagnostic retry imported child");
        source.Need(producer + 109, [0x83, 0xc4, 0x20, 0xa3], "diagnostic retry actual result store");
        if (source.Address(producer, 113) != stream) throw new NotSupportedException("Diagnostic retry result moved to another global.");
        source.Need(producer + 117, [0x85, 0xc0, 0x75, 5, 0x83, 0xfe], "diagnostic retry termination");
        var retries = source.Read(producer + 123, 1)[0];
        if (retries is 0 or > 32) throw new NotSupportedException("Diagnostic retries lack a bounded source limit.");
        source.Need(producer + 124, [0x72, 0xb8, 0x5e, 0x8b, 0x4d, 0xfc, 0x33, 0xcd, 0x5b], "diagnostic return and cookie argument");
        var cookie = source.Call(producer + 133);
        source.Need(producer + 138, [0xc9, 0xc3], "diagnostic producer return");
        source.Need(cookie, [0x3b, 0x0d], "diagnostic cookie comparison");
        if (source.Address(cookie, 2) != source.Address(producer, 10)) throw new NotSupportedException("Diagnostic cookie changed its actual stack owner.");
        source.Need(cookie + 6, [0x75, 1, 0xc3], "diagnostic cookie returned branch");
        source.Need(formatter, [0x55, 0x8b, 0xec, 0x8d, 0x45, 0x14, 0x50, 0x6a, 0], "diagnostic genuine variadic cursor");
        source.Need(formatter + 9, [0xff, 0x75, 0x10, 0xff, 0x75, 0x0c, 0xff, 0x75, 8], "diagnostic formatter arguments");
        var options = source.Call(formatter + 18); _ = source.Getter(options);
        source.Need(formatter + 23, [0xff, 0x70, 4, 0xff, 0x30], "diagnostic current formatter options");
        var printing = source.ImportAt(formatter + 28, "__stdio_common_vsprintf_s");
        source.Need(formatter + 34, [0x83, 0xc9, 0xff, 0x83, 0xc4, 0x1c, 0x85, 0xc0, 0x0f, 0x48, 0xc1, 0x5d, 0xc3], "diagnostic formatter result");
        var filename = Text(source, name); var access = Text(source, mode);
        if (access != "w" || Text(source, format) != "%s%d" || filename.Length > 96 ||
            !filename.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || filename.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not ('.' or '_' or '-')) ||
            filename.StartsWith(".", StringComparison.Ordinal))
            throw new NotSupportedException("Diagnostic producer has no bounded relative log/write declaration.");
        var imports = new HashSet<string>(StringComparer.Ordinal) { open.Library + "!" + open.Name, printing.Library + "!" + printing.Name };
        var writes = new HashSet<uint>();
        NativePluginConstructorFootprint.Check(source, new(producer, 140, new HashSet<uint> { formatter, cookie }), imports, writes);
        NativePluginConstructorFootprint.Check(source, new(formatter, 47, new HashSet<uint> { options }), imports, writes);
        NativePluginConstructorFootprint.Check(source, new(options, 6, new HashSet<uint>()), imports, writes);
        NativePluginConstructorFootprint.Check(source, new(cookie, 14, new HashSet<uint>(), new Dictionary<uint, bool> { [cookie + 6] = false }), imports, writes);
        if (!writes.SetEquals([stream])) throw new NotSupportedException("Diagnostic producer has an independent source data writer.");
        return new(image, stack, query, producer, stream, name, mode, 0x20, retries, filename, access);
    }
    private static string Text(NativePluginConstructorImage source, uint rva)
    {
        var text = new StringBuilder();
        for (uint at = 0; at < 128; ++at)
        {
            var value = source.Read(checked(rva + at), 1)[0];
            if (value == 0) return text.ToString();
            if (value is < 0x20 or > 0x7e) throw new NotSupportedException("Diagnostic source operand is not bounded ASCII.");
            text.Append((char)value);
        }
        throw new NotSupportedException("Diagnostic source operand lacks a bounded terminator.");
    }
    internal string AttemptName(uint attempt) => attempt == 0 ? Name : attempt <= RetryLimit
        ? Name + (attempt - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw new NotSupportedException("Diagnostic open exceeded its genuine source retry limit.");
    internal void Check(NativePluginImageDeclaration image, string stack)
    {
        if (!System.IO.Path.GetFullPath(Path).Equals(System.IO.Path.GetFullPath(image.Path), StringComparison.OrdinalIgnoreCase) ||
            !Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) || !StackSha256.Equals(stack, StringComparison.OrdinalIgnoreCase) ||
            !_imports.SequenceEqual(image.Imports)) throw new InvalidDataException("Diagnostic CRT source changed its current module/stack/import identity.");
    }
}
