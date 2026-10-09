int __cdecl crt_fgetc(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::get_character, false, 0, 0,
        [&](CrtProvider& provider) { return crt_export<int (__cdecl*)(void*)>(provider, "fgetc")(pointer); }); });
}
int __cdecl crt_getc(void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::get_character, false, 0, 0,
        [&](CrtProvider& provider) { return crt_export<int (__cdecl*)(void*)>(provider, "getc")(pointer); }); });
}
int __cdecl crt_ungetc(int character, void* pointer) {
    return nvse_adapter([&] { return crt_member(pointer, CrtOperation::push_character, false,
        static_cast<std::uint64_t>(static_cast<std::int64_t>(character)), 0,
        [&](CrtProvider& provider) { return crt_export<int (__cdecl*)(int, void*)>(provider, "ungetc")(character, pointer); }); });
}
int __cdecl crt_fgetpos(void* pointer, std::int64_t* position) {
    return nvse_adapter([&] {
        expression_writable(position, sizeof(std::int64_t)); auto& value = crt_support_state(pointer); DWORD last = 0;
        const auto result = crt_member(pointer, CrtOperation::get_position, false, address(position), sizeof(std::int64_t), [&](CrtProvider& provider) {
            const auto actual = crt_export<int (__cdecl*)(void*, std::int64_t*)>(provider, "fgetpos")(pointer, position); last = GetLastError(); return actual;
        });
        crt_support_report_preserving_error(value, CrtSupportKind::position, result, last, result == 0,
            result == 0 ? static_cast<std::uint64_t>(nvse_copy(position)) : 0);
        SetLastError(last); return result;
    });
}
int __cdecl crt_fsetpos(void* pointer, const std::int64_t* position) {
    return nvse_adapter([&] {
        const auto bits = static_cast<std::uint64_t>(nvse_copy(position)); auto& value = crt_support_state(pointer); DWORD last = 0;
        const auto result = crt_member(pointer, CrtOperation::set_position, false, address(position), sizeof(std::int64_t), [&](CrtProvider& provider) {
            const auto actual = crt_export<int (__cdecl*)(void*, const std::int64_t*)>(provider, "fsetpos")(pointer, position); last = GetLastError(); return actual;
        });
        crt_support_report_preserving_error(value, CrtSupportKind::position, result, last, result == 0, result == 0 ? bits : 0);
        SetLastError(last); return result;
    });
}
int __cdecl crt_setvbuf(void* pointer, char* buffer, int mode, std::size_t size) {
    return nvse_adapter([&] {
        auto& value = crt_support_state(pointer);
        // _IONBF ignores both supplied storage arguments according to the
        // public contract. Other modes forward actual retained caller memory.
        if ((mode != _IONBF && mode != _IOFBF && mode != _IOLBF) ||
            (mode != _IONBF && (size < 2 || size > static_cast<std::size_t>(std::numeric_limits<int>::max()))))
            throw Fatal(ERROR_NOT_SUPPORTED, "Invalid setvbuf mode/size requires its original parameter-handler owner.");
        const auto selected = mode == _IONBF ? CrtBufferLease{} : crt_support_buffer(buffer, size);
        DWORD last = 0;
        const auto result = crt_member(pointer, CrtOperation::set_buffer, false,
            static_cast<std::uint64_t>(static_cast<std::int64_t>(mode)), size, [&](CrtProvider& provider) {
            const auto actual = crt_export<int (__cdecl*)(void*, char*, int, std::size_t)>(provider, "setvbuf")(pointer, buffer, mode, size);
            last = GetLastError();
            if (!actual) { value.buffer = selected; value.base_cell = nullptr; value.pointer_cell = nullptr; value.count_cell = nullptr; }
            return actual;
        });
        crt_support_report_preserving_error(value, CrtSupportKind::buffer, result, last); SetLastError(last); return result;
    });
}
int __cdecl crt_get_buffer_pointers(void* pointer, char*** base_cell, char*** pointer_cell, int** count_cell) {
    return nvse_adapter([&] {
        if (base_cell) expression_writable(base_cell, sizeof(char**));
        if (pointer_cell) expression_writable(pointer_cell, sizeof(char**));
        if (count_cell) expression_writable(count_cell, sizeof(int*));
        auto& value = crt_support_state(pointer); DWORD last = 0;
        const auto result = crt_member(pointer, CrtOperation::buffer_cells, false, address(base_cell), address(pointer_cell), [&](CrtProvider& provider) {
            const auto actual = crt_export<int (__cdecl*)(void*, char***, char***, int**)>(provider, "_get_stream_buffer_pointers")(
                pointer, base_cell, pointer_cell, count_cell); last = GetLastError();
            if (!actual) {
                if (base_cell) value.base_cell = nvse_copy(base_cell);
                if (pointer_cell) value.pointer_cell = nvse_copy(pointer_cell);
                if (count_cell) value.count_cell = nvse_copy(count_cell);
            }
            return actual;
        });
        crt_support_validate(pointer); crt_support_report_preserving_error(value, CrtSupportKind::cells, result, last); SetLastError(last); return result;
    });
}
void __cdecl crt_lock_file(void* pointer) {
    nvse_adapter([&] {
        auto& value = crt_support_state(pointer);
        if (value.lock_depth == std::numeric_limits<std::uint32_t>::max()) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Actual FILE lock nesting exhausted.");
        DWORD last = 0;
        (void)crt_member(pointer, CrtOperation::lock, false, value.lock_depth, 0, [&](CrtProvider& provider) {
            crt_export<void (__cdecl*)(void*)>(provider, "_lock_file")(pointer); last = GetLastError(); ++value.lock_depth; return 0;
        });
        crt_support_report_preserving_error(value, CrtSupportKind::lock, 0, last); SetLastError(last);
    });
}
void __cdecl crt_unlock_file(void* pointer) {
    nvse_adapter([&] {
        auto& value = crt_support_state(pointer);
        if (!value.lock_depth) throw Fatal(ERROR_INVALID_STATE, "Actual FILE unlock has no matching caller-thread lock.");
        DWORD last = 0;
        (void)crt_member(pointer, CrtOperation::unlock, false, value.lock_depth, 0, [&](CrtProvider& provider) {
            crt_export<void (__cdecl*)(void*)>(provider, "_unlock_file")(pointer); last = GetLastError(); --value.lock_depth; return 0;
        });
        crt_support_report_preserving_error(value, CrtSupportKind::unlock, 0, last); SetLastError(last);
    });
}
void* crt_extended_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    static const Entry entries[] = {
        {"fgetc", reinterpret_cast<void*>(crt_fgetc)}, {"getc", reinterpret_cast<void*>(crt_getc)},
        {"ungetc", reinterpret_cast<void*>(crt_ungetc)}, {"fgetpos", reinterpret_cast<void*>(crt_fgetpos)},
        {"fsetpos", reinterpret_cast<void*>(crt_fsetpos)}, {"setvbuf", reinterpret_cast<void*>(crt_setvbuf)},
        {"_get_stream_buffer_pointers", reinterpret_cast<void*>(crt_get_buffer_pointers)},
        {"_lock_file", reinterpret_cast<void*>(crt_lock_file)}, {"_unlock_file", reinterpret_cast<void*>(crt_unlock_file)},
        {"getenv_s", reinterpret_cast<void*>(crt_getenv_s)}, {"_putenv_s", reinterpret_cast<void*>(crt_putenv_s)},
    };
    for (const auto& entry : entries) if (name == entry.name) return entry.function; return nullptr;
}
