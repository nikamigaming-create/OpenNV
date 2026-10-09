namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginCppRuntimeExport(string Name, uint Rva, bool Executable);
internal sealed record NativePluginCppRuntimeSelection(string Path, string Sha256, string SourceOwner,
    IReadOnlyList<NativePluginCppRuntimeExport> Exports, string UcrtPath, string UcrtSha256);

// These are public x86 MSVC decorated interfaces. Their actual implementation,
// objects, vtables, locale facets and exception machinery remain in the exact
// selected Microsoft provider. OpenNV never recreates a C++ class layout.
internal static class NativePluginCppRuntimeImports
{
    internal const string Library = "msvcp140.dll";
    internal const string OpenWide = "?_Fiopen@std@@YAPAU_iobuf@@PB_WHH@Z";
    internal const string OpenNarrow = "?_Fiopen@std@@YAPAU_iobuf@@PBDHH@Z";
    internal const string ErrorStream = "?cerr@std@@3V?$basic_ostream@DU?$char_traits@D@std@@@1@A";
    internal static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        OpenWide, OpenNarrow, ErrorStream,
        "??0?$basic_ostream@DU?$char_traits@D@std@@@std@@QAE@PAV?$basic_streambuf@DU?$char_traits@D@std@@@1@_N@Z",
        "?flush@?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV12@XZ",
        "?in@?$codecvt@DDU_Mbstatet@@@std@@QBEHAAU_Mbstatet@@PBD1AAPBDPAD3AAPAD@Z",
        "?out@?$codecvt@DDU_Mbstatet@@@std@@QBEHAAU_Mbstatet@@PBD1AAPBDPAD3AAPAD@Z",
        "??1?$basic_ios@DU?$char_traits@D@std@@@std@@UAE@XZ",
        "??1?$basic_streambuf@DU?$char_traits@D@std@@@std@@UAE@XZ",
        "?_Lock@?$basic_streambuf@DU?$char_traits@D@std@@@std@@UAEXXZ",
        "?_Unlock@?$basic_streambuf@DU?$char_traits@D@std@@@std@@UAEXXZ",
        "?showmanyc@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAE_JXZ",
        "?uflow@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAEHXZ",
        "?xsgetn@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAE_JPAD_J@Z",
        "?xsputn@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAE_JPBD_J@Z",
        "?setbuf@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAEPAV12@PAD_J@Z",
        "?sync@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAEHXZ",
        "?imbue@?$basic_streambuf@DU?$char_traits@D@std@@@std@@MAEXABVlocale@2@@Z",
        "??1?$basic_ostream@DU?$char_traits@D@std@@@std@@UAE@XZ",
        "??6?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV01@P6AAAV01@AAV01@@Z@Z",
        "??6?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV01@I@Z",
        "??6?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV01@_J@Z",
        "?put@?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV12@D@Z",
        "?write@?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV12@PBD_J@Z",
        "?good@ios_base@std@@QBE_NXZ", "?always_noconv@codecvt_base@std@@QBE_NXZ",
        "??1facet@locale@std@@MAE@XZ", "?_Getcvt@_Locinfo@std@@QBE?AU_Cvtvec@@XZ",
        "?id@?$numpunct@D@std@@2V0locale@2@A", "?_Gettrue@_Locinfo@std@@QBEPBDXZ",
        "?_Getfalse@_Locinfo@std@@QBEPBDXZ", "?_Getlconv@_Locinfo@std@@QBEPBUlconv@@XZ",
        "??6?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEAAV01@N@Z",
        "??0?$basic_iostream@DU?$char_traits@D@std@@@std@@QAE@PAV?$basic_streambuf@DU?$char_traits@D@std@@@1@@Z",
        "??1?$basic_iostream@DU?$char_traits@D@std@@@std@@UAE@XZ", "?_Xbad_function_call@std@@YAXXZ",
        "?_Throw_Cpp_error@std@@YAXH@Z", "?_Syserror_map@std@@YAPBDH@Z", "_Mtx_lock", "_Mtx_unlock",
        "?uncaught_exceptions@std@@YAHXZ", "?_Xinvalid_argument@std@@YAXPBD@Z", "?_Id_cnt@id@locale@std@@0HA",
        "?sputn@?$basic_streambuf@DU?$char_traits@D@std@@@std@@QAE_JPBD_J@Z",
        "?sputc@?$basic_streambuf@DU?$char_traits@D@std@@@std@@QAEHD@Z",
        "??0?$basic_ios@DU?$char_traits@D@std@@@std@@IAE@XZ", "?widen@?$basic_ios@DU?$char_traits@D@std@@@std@@QBEDD@Z",
        "?setstate@?$basic_ios@DU?$char_traits@D@std@@@std@@QAEXH_N@Z",
        "?_Getcat@?$codecvt@DDU_Mbstatet@@@std@@SAIPAPBVfacet@locale@2@PBV42@@Z",
        "?unshift@?$codecvt@DDU_Mbstatet@@@std@@QBEHAAU_Mbstatet@@PAD1AAPAD@Z",
        "?_Osfx@?$basic_ostream@DU?$char_traits@D@std@@@std@@QAEXXZ",
        "?_Init@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEXPAPAD0PAH001@Z",
        "?_Init@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEXXZ",
        "??0?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAE@XZ",
        "?getloc@?$basic_streambuf@DU?$char_traits@D@std@@@std@@QBE?AVlocale@2@XZ",
        "?setw@std@@YA?AU?$_Smanip@_J@1@_J@Z", "?id@?$codecvt@DDU_Mbstatet@@@std@@2V0locale@2@A",
        "?_Winerror_map@std@@YAHH@Z",
        "?flags@ios_base@std@@QBEHXZ", "?width@ios_base@std@@QBE_JXZ", "?width@ios_base@std@@QAE_J_J@Z",
        "?eback@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBEPADXZ",
        "?gptr@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBEPADXZ",
        "?pptr@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBEPADXZ",
        "?egptr@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBEPADXZ",
        "?gbump@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEXH@Z",
        "?setg@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEXPAD00@Z",
        "?epptr@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBEPADXZ",
        "?_Gndec@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEPADXZ",
        "?_Gninc@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEPADXZ",
        "?_Gnavail@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBE_JXZ",
        "?pbump@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEXH@Z",
        "?_Pninc@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IAEPADXZ",
        "?_Getgloballocale@locale@std@@CAPAV_Locimp@12@XZ",
        "?tie@?$basic_ios@DU?$char_traits@D@std@@@std@@QBEPAV?$basic_ostream@DU?$char_traits@D@std@@@2@XZ",
        "?rdbuf@?$basic_ios@DU?$char_traits@D@std@@@std@@QBEPAV?$basic_streambuf@DU?$char_traits@D@std@@@2@XZ",
        "?imbue@?$basic_ios@DU?$char_traits@D@std@@@std@@QAE?AVlocale@2@ABV32@@Z",
        "?fill@?$basic_ios@DU?$char_traits@D@std@@@std@@QBEDXZ",
        "??0?$basic_istream@DU?$char_traits@D@std@@@std@@QAE@PAV?$basic_streambuf@DU?$char_traits@D@std@@@1@_N@Z",
        "??1?$basic_istream@DU?$char_traits@D@std@@@std@@UAE@XZ",
        "?read@?$basic_istream@DU?$char_traits@D@std@@@std@@QAEAAV12@PAD_J@Z",
        "?gcount@?$basic_istream@DU?$char_traits@D@std@@@std@@QBE_JXZ",
        "?classic@locale@std@@SAABV12@XZ", "?_Xlength_error@std@@YAXPBD@Z", "?_Xbad_alloc@std@@YAXXZ",
        "?uncaught_exception@std@@YA_NXZ", "??1_Lockit@std@@QAE@XZ",
        "?_Pnavail@?$basic_streambuf@DU?$char_traits@D@std@@@std@@IBE_JXZ", "??0_Lockit@std@@QAE@H@Z",
    };
    private static readonly IReadOnlySet<string> Data = new HashSet<string>(StringComparer.Ordinal)
    { ErrorStream, "?id@?$numpunct@D@std@@2V0locale@2@A", "?id@?$codecvt@DDU_Mbstatet@@@std@@2V0locale@2@A", "?_Id_cnt@id@locale@std@@0HA" };
    internal static bool Owns(string library, string name) => library.Equals(Library, StringComparison.OrdinalIgnoreCase) && Names.Contains(name);
    internal static bool IsFileImport(string library, string name) => library.Equals(Library, StringComparison.OrdinalIgnoreCase) && name is OpenWide or OpenNarrow;
    internal static string? Owner(string library, string name) => Owns(library, name) && !IsFileImport(library, name)
        ? "actual-selected-x86-MSVC-object-export:" + library + "!" + name : null;
    internal static NativePluginCppRuntimeSelection? Read(string modulePath, string moduleSha256)
    {
        var module = NativePluginImageDeclarations.Read(modulePath, dll: true);
        if (!module.Sha256.Equals(moduleSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("C++ source selection changed its original module bytes.");
        var names = module.Imports.Where(row => row.Library.Equals(Library, StringComparison.OrdinalIgnoreCase) && row.Name is not null && Names.Contains(row.Name))
            .Select(row => row.Name!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0) return null;
        if (module.DelayImports) throw new NotSupportedException("C++ delay-entry ownership is absent.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), Library);
        var provider = NativePluginImageDeclarations.Read(path, dll: true);
        var ucrtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "ucrtbase.dll");
        var ucrt = NativePluginImageDeclarations.Read(ucrtPath, dll: true);
        if (!provider.Imports.Any(row => (row.Library is "ucrtbase.dll" or "api-ms-win-crt-stdio-l1-1-0.dll") && row.Name == "__acrt_iob_func"))
            throw new NotSupportedException("Selected C++ provider lacks its genuine UCRT standard-stream dependency.");
        var exports = names.Select(name =>
        {
            var declaration = provider.Exports.SingleOrDefault(row => row.Name == name)
                ?? throw new NotSupportedException("Selected C++ provider omits original imported export: " + name);
            if (declaration.Forwarded || declaration.Executable == Data.Contains(name) || declaration.Rva == 0)
                throw new NotSupportedException("C++ code/global export kind differs from its exact public contract: " + name);
            return new NativePluginCppRuntimeExport(name, declaration.Rva, declaration.Executable);
        }).ToArray();
        return new(path, provider.Sha256, "actual-selected-MSVC-public-object-runtime:" + provider.Sha256, exports, ucrtPath, ucrt.Sha256);
    }
}
