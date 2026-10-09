#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdio.h>
#include <stdarg.h>
#include <stdlib.h>
#include <direct.h>
#include <share.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Authored public-ABI caller. The actual Windows CRT owns FILE, buffers,
// variadic arguments and return/error behavior; no game instructions run.
extern "C" int _fltused = 0;

namespace {
bool join_path(const opennv_domain::NvseCoreInterface* core, const char* leaf, char (&path)[1024]) {
    if (!core || !core->runtime_directory) return false;
    const char* directory = core->runtime_directory();
    if (!directory) return false;
    unsigned int at = 0;
    while (*directory) {
        if (at + 1 >= sizeof(path)) return false;
        path[at++] = *directory++;
    }
    while (*leaf) {
        if (at + 1 >= sizeof(path)) return false;
        path[at++] = *leaf++;
    }
    path[at] = 0;
    return true;
}
bool wide_path(const char* source, wchar_t (&destination)[1024]) {
    unsigned int at = 0;
    while (source[at]) {
        const unsigned char value = static_cast<unsigned char>(source[at]);
        if (value > 127 || at + 1 >= 1024) return false;
        destination[at++] = value;
    }
    destination[at] = 0;
    return true;
}
int format(FILE* stream, const char* pattern, ...) {
    va_list arguments;
    va_start(arguments, pattern);
    const int result = __stdio_common_vfprintf(0, stream, pattern, nullptr, arguments);
    va_end(arguments);
    return result;
}
}

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }

extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core,
    opennv_domain::NvsePluginInfo* info) {
    if (!info || !core || core->is_editor) return false;
    info->info_version = 1;
    info->name = "OpenNV authored public-ABI CRT fixture";
    info->version = 1;
    char path[1024];
    if (!join_path(core, "OpenNVCrtAuthored.cfg", path)) return false;
    FILE* stream = fopen(path, "rb");
    if (!stream) return false;
    unsigned char bytes[8];
    for (unsigned int index = 0; index < sizeof(bytes); ++index) bytes[index] = 0x5c;
    bool good = fread(bytes, 2, 4, stream) == 2;
    for (unsigned int index = 0; index < 5; ++index) good = good && bytes[index] == 'A' + index;
    for (unsigned int index = 5; index < sizeof(bytes); ++index) good = good && bytes[index] == 0x5c;
    good = good && feof(stream) != 0 && ferror(stream) == 0 && ftell(stream) == 5;
    clearerr(stream);
    good = good && feof(stream) == 0;
    rewind(stream);
    good = good && _ftelli64(stream) == 0 && _fseeki64(stream, 4, SEEK_SET) == 0;
    good = good && fread(bytes, 1, 1, stream) == 1 && bytes[0] == 'E';
    good = fclose(stream) == 0 && good;
    if (!good || !join_path(core, "OpenNVCrtAuthored.absent", path)) return false;
    stream = fopen(path, "rb");
    int error = 0;
    unsigned long dos = 0;
    if (stream) { (void)fclose(stream); return false; }
    return _get_errno(&error) == 0 && error != 0 && _get_doserrno(&dos) == 0 && dos != 0;
}

extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    char output[1024];
    if (!join_path(core, "OpenNVCrtAuthored.out", output)) return false;
    FILE* stream = nullptr;
    if (fopen_s(&stream, output, "wb") != 0 || !stream) return false;
    bool good = fwrite("DATA", 2, 2, stream) == 2 && fputc('\n', stream) == '\n';
    good = good && fputs("line\n", stream) >= 0 && format(stream, "format=%s:%d:%.2f\n", "tag", 7, 6.25) > 0;
    good = good && fflush(stream) == 0 && fseek(stream, 0, SEEK_SET) == 0 && fwrite("X", 1, 1, stream) == 1;
    good = fclose(stream) == 0 && good;
    if (!good) return false;
    stream = fopen(output, "ab");
    if (!stream) return false;
    good = fwrite("tail", 1, 4, stream) == 4;
    good = fclose(stream) == 0 && good;
    if (!good) return false;
    stream = _fsopen(output, "r+b", _SH_DENYNO);
    if (!stream) return false;
    unsigned char first = 0;
    good = fread(&first, 1, 1, stream) == 1 && first == 'X' && fseek(stream, 2, SEEK_SET) == 0;
    good = good && fwrite("Y", 1, 1, stream) == 1 && _fseeki64(stream, 0, SEEK_END) == 0 && _ftelli64(stream) > 4;
    good = fclose(stream) == 0 && good;
    wchar_t wide[1024];
    if (!good || !wide_path(output, wide)) return false;
    stream = _wfopen(wide, L"rb");
    if (!stream) return false;
    good = fread(&first, 1, 1, stream) == 1 && first == 'X';
    good = fclose(stream) == 0 && good;
    char configuration[1024];
    if (!good || !join_path(core, "OpenNVCrtAuthored.cfg", configuration)) return false;
    stream = fopen(configuration, "r+b");
    if (!stream) return false;
    good = fputc('Z', stream) == 'Z';
    good = fclose(stream) == 0 && good;
    if (!good || !DeleteFileA(configuration)) return false;
    stream = fopen(configuration, "rb");
    if (stream) { (void)fclose(stream); return false; }
    char directory[1024];
    if (!join_path(core, "OpenNVCrtAuthored-dir", directory) || _mkdir(directory) != 0) return false;
    if (!join_path(core, "OpenNVCrtAuthored-dir\\wide", directory) || !wide_path(directory, wide)) return false;
    return _wmkdir(wide) == 0;
}
