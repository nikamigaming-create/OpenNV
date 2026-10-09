template<class Call> auto crt_member(void* pointer, CrtOperation operation, bool writable,
    std::uint64_t argument, std::uint64_t requested, Call call) {
    const auto incoming = GetLastError(); CrtSupportCall entered(pointer); crt_support_validate(pointer);
    const auto stream = crt_stream(pointer, writable); auto& provider = crt_provider(stream.provider);
    SetLastError(incoming); const auto result = call(provider); const auto last = GetLastError(); const auto status = crt_status(provider, pointer);
    crt_emit(CrtEvent{stream.provider, stream.route, address(pointer), operation, argument, requested,
        static_cast<std::int64_t>(result), false, status});
    crt_restore_error(provider, status); SetLastError(last); return result;
}
std::uint64_t crt_transfer_extent(std::size_t size, std::size_t count) {
    const auto extent = static_cast<std::uint64_t>(size) * count;
    if (extent > std::numeric_limits<std::uint32_t>::max())
        throw Fatal(ERROR_NOT_SUPPORTED, "CRT overflow/invalid-parameter transfer behavior has no admitted caller extent.");
    return extent;
}
std::size_t __cdecl crt_fread(void* destination, std::size_t size, std::size_t count, void* pointer) {
    return nvse_adapter([&] {
        const auto extent = crt_transfer_extent(size, count);
        if (extent) expression_writable(destination, static_cast<std::uint32_t>(extent));
        return crt_member(pointer, CrtOperation::read, false, size, count, [&](CrtProvider& provider) {
            return crt_export<std::size_t (__cdecl*)(void*, std::size_t, std::size_t, void*)>(provider, "fread")(destination, size, count, pointer);
        });
    });
}
std::size_t __cdecl crt_fwrite(const void* source_data, std::size_t size, std::size_t count, void* pointer) {
    return nvse_adapter([&] {
        const auto extent = crt_transfer_extent(size, count);
        // The same source CRT consumes its caller's actual native buffer.
        // Return values count complete elements; partial-element byte behavior
        // is deliberately not manufactured from those element counts.
        if (extent && !source_data) throw Fatal(ERROR_INVALID_ADDRESS, "CRT write has an absent actual native input extent.");
        return crt_member(pointer, CrtOperation::write, true, size, count, [&](CrtProvider& provider) {
            return crt_export<std::size_t (__cdecl*)(const void*, std::size_t, std::size_t, void*)>(provider, "fwrite")(source_data, size, count, pointer);
        });
    });
}
int __cdecl crt_fseek(void* pointer, long offset, int origin) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::seek, false,
        static_cast<std::uint64_t>(static_cast<std::int64_t>(offset)), static_cast<std::uint64_t>(origin), [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(void*, long, int)>(provider, "fseek")(pointer, offset, origin);
    }); });
}
int __cdecl crt_fseeki64(void* pointer, std::int64_t offset, int origin) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::seek, false,
        static_cast<std::uint64_t>(offset), static_cast<std::uint64_t>(origin), [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(void*, std::int64_t, int)>(provider, "_fseeki64")(pointer, offset, origin);
    }); });
}
long __cdecl crt_ftell(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::tell, false, 0, 0, [&](CrtProvider& provider) {
        return crt_export<long (__cdecl*)(void*)>(provider, "ftell")(pointer);
    }); });
}
std::int64_t __cdecl crt_ftelli64(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::tell, false, 0, 0, [&](CrtProvider& provider) {
        return crt_export<std::int64_t (__cdecl*)(void*)>(provider, "_ftelli64")(pointer);
    }); });
}
void __cdecl crt_rewind(void* pointer) {
    nvse_adapter([&] { (void)crt_member(pointer, CrtOperation::rewind, false, 0, 0, [&](CrtProvider& provider) {
        crt_export<void (__cdecl*)(void*)>(provider, "rewind")(pointer); return 0;
    }); });
}
int __cdecl crt_fflush(void* pointer) {
    return nvse_adapter([&] {
        if (!pointer) throw Fatal(ERROR_NOT_SUPPORTED, "CRT global/stdstream flush ownership is absent.");
        return crt_member(pointer, CrtOperation::flush, false, 0, 0, [&](CrtProvider& provider) {
            return crt_export<int (__cdecl*)(void*)>(provider, "fflush")(pointer);
        });
    });
}
int __cdecl crt_fputc(int character, void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::put_character, true,
        static_cast<std::uint64_t>(static_cast<std::int64_t>(character)), 0, [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(int, void*)>(provider, "fputc")(character, pointer);
    }); });
}
int __cdecl crt_fputs(const char* text, void* pointer) {
    return nvse_adapter([&] {
        const auto source = nvse_string(text, false);
        return crt_member(pointer, CrtOperation::put_string, true, 0, source.size(), [&](CrtProvider& provider) {
            return crt_export<int (__cdecl*)(const char*, void*)>(provider, "fputs")(text, pointer);
        });
    });
}
int __cdecl crt_common_vfprintf(std::uint64_t options, void* pointer, const char* format, void* locale, char* arguments) {
    return nvse_adapter([&]() -> int {
        if (locale) throw Fatal(ERROR_NOT_SUPPORTED, "CRT nonnull locale lifetime/caller owner is absent.");
        const auto source = nvse_string(format, false);
        // va_list is the genuine x86 caller cursor passed to this public UCRT
        // entry. No guessed token list or first-party formatting replacement.
        return crt_member(pointer, CrtOperation::formatted_write, true, options, source.size(), [&](CrtProvider& provider) {
            return crt_export<int (__cdecl*)(std::uint64_t, void*, const char*, void*, char*)>(provider,
                "__stdio_common_vfprintf")(options, pointer, format, locale, arguments);
        });
    });
}
int __cdecl crt_feof(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::eof, false, 0, 0, [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(void*)>(provider, "feof")(pointer);
    }); });
}
int __cdecl crt_ferror(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::error, false, 0, 0, [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(void*)>(provider, "ferror")(pointer);
    }); });
}
void __cdecl crt_clearerr(void* pointer) {
    nvse_adapter([&] { (void)crt_member(pointer, CrtOperation::clear_error, false, 0, 0, [&](CrtProvider& provider) {
        crt_export<void (__cdecl*)(void*)>(provider, "clearerr")(pointer); return 0;
    }); });
}
int __cdecl crt_fclose(void* pointer) {
    return nvse_adapter([&] {
        const auto incoming = GetLastError(); CrtSupportCall entered(pointer, true); crt_support_validate(pointer, true);
        const auto stream = crt_stream(pointer); auto& provider = crt_provider(stream.provider);
        // fclose invalidates its actual FILE even if a pending flush fails.
        // Remove it before a receipt can reenter; never call fclose twice.
        const auto close_entry = crt_export<int (__cdecl*)(void*)>(provider, "fclose");
        SetLastError(incoming); const auto result = close_entry(pointer); const auto last = GetLastError();
        crt_owner().streams.erase(address(pointer)); const auto status = crt_status(provider);
        const CrtEvent event{stream.provider, stream.route, address(pointer), CrtOperation::close, 0, 0, result, true, status};
        if (result) crt_owner().failed_closes.push_back(event);
        crt_emit(event); crt_support_closed(pointer, result, last); crt_restore_error(provider, status); SetLastError(last); return result;
    });
}
void* crt_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    static const Entry entries[] = {
        {"fopen", reinterpret_cast<void*>(crt_fopen_a)}, {"_wfopen", reinterpret_cast<void*>(crt_fopen_w)},
        {"fopen_s", reinterpret_cast<void*>(crt_fopen_s_a)}, {"_wfopen_s", reinterpret_cast<void*>(crt_fopen_s_w)},
        {"_fsopen", reinterpret_cast<void*>(crt_fsopen_a)}, {"_wfsopen", reinterpret_cast<void*>(crt_fsopen_w)},
        {"fclose", reinterpret_cast<void*>(crt_fclose)}, {"fread", reinterpret_cast<void*>(crt_fread)}, {"fwrite", reinterpret_cast<void*>(crt_fwrite)},
        {"fseek", reinterpret_cast<void*>(crt_fseek)}, {"_fseeki64", reinterpret_cast<void*>(crt_fseeki64)},
        {"ftell", reinterpret_cast<void*>(crt_ftell)}, {"_ftelli64", reinterpret_cast<void*>(crt_ftelli64)}, {"rewind", reinterpret_cast<void*>(crt_rewind)},
        {"fflush", reinterpret_cast<void*>(crt_fflush)}, {"fputc", reinterpret_cast<void*>(crt_fputc)}, {"fputs", reinterpret_cast<void*>(crt_fputs)},
        {"__stdio_common_vfprintf", reinterpret_cast<void*>(crt_common_vfprintf)}, {"feof", reinterpret_cast<void*>(crt_feof)},
        {"ferror", reinterpret_cast<void*>(crt_ferror)}, {"clearerr", reinterpret_cast<void*>(crt_clearerr)},
        {"_mkdir", reinterpret_cast<void*>(crt_mkdir_a)}, {"_wmkdir", reinterpret_cast<void*>(crt_mkdir_w)},
    };
    for (const auto& entry : entries) if (name == entry.name) return entry.function; return crt_extended_entry(name);
}
