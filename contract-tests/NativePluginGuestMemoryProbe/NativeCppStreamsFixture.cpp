#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <fstream>
#include <iostream>
#include <locale>
#include <stdio.h>
#include <io.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// First-party /MD caller. Windows constructs the real Microsoft runtime and
// this caller's actual C++ objects. The fixture contains no game assets/code.
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || !core->nvse_version || !core->runtime_version || core->is_editor) return false;
    info->info_version = 1; info->name = "OpenNV actual C++ streams"; info->version = 1; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!core || !core->runtime_directory) return false;
    const std::string directory = core->runtime_directory();
    const std::string source = directory + "OpenNVCppAuthored.cfg", output = directory + "OpenNVCppAuthored.out";
    {
        std::ifstream input_stream(source, std::ios::binary);
        char bytes[5]{}; input_stream.read(bytes, sizeof(bytes));
        if (input_stream.gcount() != static_cast<std::streamsize>(sizeof(bytes)) || std::string(bytes, sizeof(bytes)) != "ABCDE") return false;
        std::ofstream output_stream(output, std::ios::binary | std::ios::app);
        output_stream.imbue(std::locale::classic());
        output_stream << "cpp:" << 37U << ':' << 125LL << ':' << 2.5 << '\n';
        output_stream.flush(); if (!output_stream.good()) return false;
        input_stream.close(); output_stream.close();
    }
    FILE* standard = __acrt_iob_func(2);
    if (!standard || standard != __acrt_iob_func(2)) return false;
    const int descriptor = _fileno(standard);
    if (descriptor != 2 || _get_osfhandle(descriptor) == -1) return false;
    const int alias = _dup(descriptor);
    if (alias < 3 || _write(alias, "descriptor\n", 11) < 0) return false;
    if (_close(alias)) return false;
    if (fputs("FILE\n", standard) < 0 || fflush(standard)) return false;
    std::cerr << "cerr:" << 91U << '\n'; std::cerr.flush();
    return std::cerr.good();
}
