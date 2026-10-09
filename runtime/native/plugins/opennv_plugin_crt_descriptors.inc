enum class CrtDescriptorOperation : std::uint32_t { file_number = 1, os_handle = 2, duplicate = 3, close = 4, read = 5, write = 6, seek = 7, length = 8, eof = 9 };
void crt_descriptor_emit(const CrtDescriptor& descriptor, CrtDescriptorOperation operation, std::int64_t result, int related,
    const CrtStatus& status, DWORD last) {
    Bytes request; request.put(2U); request.put(descriptor.provider); request.put(descriptor.route); request.put(address(descriptor.stream));
    request.put(descriptor.descriptor); request.put(address(descriptor.handle)); request.put(static_cast<std::uint32_t>(operation));
    request.put(result); request.put(related); crt_put_status(request, status); request.put(last);
    crt_owned_callback(crt_standard_callback, request);
}
CrtDescriptor crt_descriptor(int fd, bool write = false) {
    auto& owner = crt_standard_owner(); const auto found = owner.descriptors.find(fd);
    if (found == owner.descriptors.end() || found->second.close_entered || (write && !found->second.writable))
        throw Fatal(ERROR_INVALID_HANDLE, "Descriptor call has no actual live UCRT/private-access owner.");
    auto& provider = crt_provider(found->second.provider);
    const auto actual = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(fd));
    if (actual != found->second.handle || !crt_same_kernel_object(actual, found->second.identity))
        throw Fatal(ERROR_INVALID_HANDLE, "Original descriptor/kernel capability was closed/reused outside its actual owner.");
    if (!found->second.independent) (void)crt_stream(found->second.stream, write);
    return found->second;
}
int __cdecl crt_file_number(void* pointer) {
    return nvse_adapter([&] {
        const auto incoming = GetLastError(); const auto stream = crt_stream(pointer); auto& provider = crt_provider(stream.provider);
        SetLastError(incoming); const auto result = crt_export<int (__cdecl*)(void*)>(provider, "_fileno")(pointer);
        const auto last = GetLastError(); const auto status = crt_status(provider, pointer);
        if (result < 0) throw Fatal(ERROR_INVALID_HANDLE, "Actual live FILE returned an invalid descriptor; original error state is retained.");
        auto& owner = crt_standard_owner(); auto found = owner.descriptors.find(result);
        if (found == owner.descriptors.end()) {
            CrtDescriptor descriptor; descriptor.provider = stream.provider; descriptor.route = stream.route; descriptor.stream = pointer;
            descriptor.descriptor = result; descriptor.writable = stream.writable;
            descriptor.handle = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(result));
            found = owner.descriptors.emplace(result, descriptor).first;
            found->second.identity = crt_duplicate_identity(descriptor.handle);
            Bytes request; request.put(3U); request.put(descriptor.provider); request.put(descriptor.route); request.put(address(pointer));
            request.put(result); request.put(address(descriptor.handle)); request.put(stream.writable ? 1U : 0U);
            crt_owned_callback(crt_standard_callback, request);
        }
        if (found->second.stream != pointer || found->second.provider != stream.provider || found->second.route != stream.route)
            throw Fatal(ERROR_INVALID_DATA, "FILE descriptor aliases a different actual stream owner.");
        crt_descriptor_emit(found->second, CrtDescriptorOperation::file_number, result, 0, status, last);
        crt_restore_error(provider, status); SetLastError(last); return result;
    });
}
template<class Call> auto crt_descriptor_call(int fd, CrtDescriptorOperation operation, bool writable, Call call) {
    const auto incoming = GetLastError(); const auto descriptor = crt_descriptor(fd, writable); auto& provider = crt_provider(descriptor.provider);
    SetLastError(incoming); const auto result = call(provider); const auto last = GetLastError();
    const auto status = crt_status(provider, descriptor.independent ? nullptr : descriptor.stream);
    crt_descriptor_emit(descriptor, operation, static_cast<std::int64_t>(result), 0, status, last);
    crt_restore_error(provider, status); SetLastError(last); return result;
}
std::intptr_t __cdecl crt_os_handle(int fd) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::os_handle, false, [&](CrtProvider& provider) {
        return crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(fd);
    }); });
}
int __cdecl crt_duplicate_fd(int fd) {
    return nvse_adapter([&] {
        const auto incoming = GetLastError(); const auto source = crt_descriptor(fd); auto& provider = crt_provider(source.provider);
        SetLastError(incoming); const auto result = crt_export<int (__cdecl*)(int)>(provider, "_dup")(fd);
        const auto last = GetLastError(); const auto status = crt_status(provider, source.independent ? nullptr : source.stream);
        int handle = 0;
        if (result >= 0) {
            CrtDescriptor alias = source; alias.descriptor = result; alias.independent = true; alias.close_entered = false;
            alias.handle = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(result));
            alias.identity = INVALID_HANDLE_VALUE;
            // Publish ownership before validation/IPC can fail. A failure cannot
            // discard a genuinely duplicated provider descriptor.
            auto& descriptors = crt_standard_owner().descriptors;
            if (result < 3 || !descriptors.emplace(result, alias).second)
                throw Fatal(ERROR_INVALID_HANDLE, "Actual UCRT duplicate returned an already-owned descriptor.");
            auto& retained = descriptors.at(result); retained.identity = crt_duplicate_identity(retained.handle);
            if (!crt_same_kernel_object(source.handle, retained.handle)) throw Fatal(ERROR_INVALID_DATA, "UCRT _dup does not alias the same actual kernel object.");
            handle = static_cast<int>(address(retained.handle));
        }
        crt_descriptor_emit(source, CrtDescriptorOperation::duplicate, result, handle, status, last);
        crt_restore_error(provider, status); SetLastError(last); return result;
    });
}
int __cdecl crt_close_fd(int fd) {
    return nvse_adapter([&] {
        const auto incoming = GetLastError(); const auto descriptor = crt_descriptor(fd);
        if (!descriptor.independent) throw Fatal(ERROR_NOT_SUPPORTED, "Direct descriptor close cannot invalidate a borrowed FILE/global owner.");
        auto& retained = crt_standard_owner().descriptors.at(fd); retained.close_entered = true;
        auto& provider = crt_provider(descriptor.provider); SetLastError(incoming);
        const auto result = crt_export<int (__cdecl*)(int)>(provider, "_close")(fd); const auto last = GetLastError(); const auto status = crt_status(provider);
        if (result == 0) {
            if (retained.identity != INVALID_HANDLE_VALUE && !CloseHandle(retained.identity))
                throw Fatal(GetLastError(), "Closed descriptor retains an independently failed kernel identity reference.");
            crt_standard_owner().descriptors.erase(fd);
        }
        crt_descriptor_emit(descriptor, CrtDescriptorOperation::close, result, 0, status, last);
        crt_restore_error(provider, status); SetLastError(last);
        if (result != 0) throw Fatal(ERROR_INVALID_HANDLE, "Actual descriptor close failed; its entered prefix cannot replay.");
        return result;
    });
}
int __cdecl crt_read_fd(int fd, void* buffer, unsigned count) {
    return nvse_adapter([&] { if (count) expression_writable(buffer, count);
        return crt_descriptor_call(fd, CrtDescriptorOperation::read, false, [&](CrtProvider& provider) {
            return crt_export<int (__cdecl*)(int, void*, unsigned)>(provider, "_read")(fd, buffer, count);
        }); });
}
int __cdecl crt_write_fd(int fd, const void* buffer, unsigned count) {
    return nvse_adapter([&] { if (count && !buffer) throw Fatal(ERROR_INVALID_ADDRESS, "Descriptor write has no actual caller bytes.");
        return crt_descriptor_call(fd, CrtDescriptorOperation::write, true, [&](CrtProvider& provider) {
            return crt_export<int (__cdecl*)(int, const void*, unsigned)>(provider, "_write")(fd, buffer, count);
        }); });
}
long __cdecl crt_seek_fd(int fd, long offset, int origin) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::seek, false, [&](CrtProvider& provider) {
        return crt_export<long (__cdecl*)(int, long, int)>(provider, "_lseek")(fd, offset, origin);
    }); });
}
std::int64_t __cdecl crt_seek64_fd(int fd, std::int64_t offset, int origin) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::seek, false, [&](CrtProvider& provider) {
        return crt_export<std::int64_t (__cdecl*)(int, std::int64_t, int)>(provider, "_lseeki64")(fd, offset, origin);
    }); });
}
long __cdecl crt_length_fd(int fd) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::length, false, [&](CrtProvider& provider) {
        return crt_export<long (__cdecl*)(int)>(provider, "_filelength")(fd);
    }); });
}
std::int64_t __cdecl crt_length64_fd(int fd) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::length, false, [&](CrtProvider& provider) {
        return crt_export<std::int64_t (__cdecl*)(int)>(provider, "_filelengthi64")(fd);
    }); });
}
int __cdecl crt_eof_fd(int fd) {
    return nvse_adapter([&] { return crt_descriptor_call(fd, CrtDescriptorOperation::eof, false, [&](CrtProvider& provider) {
        return crt_export<int (__cdecl*)(int)>(provider, "_eof")(fd);
    }); });
}
void* crt_standard_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    static const Entry entries[] = {
        {"__acrt_iob_func", reinterpret_cast<void*>(crt_iob)}, {"_fileno", reinterpret_cast<void*>(crt_file_number)},
        {"_get_osfhandle", reinterpret_cast<void*>(crt_os_handle)}, {"_dup", reinterpret_cast<void*>(crt_duplicate_fd)},
        {"_close", reinterpret_cast<void*>(crt_close_fd)}, {"_read", reinterpret_cast<void*>(crt_read_fd)}, {"_write", reinterpret_cast<void*>(crt_write_fd)},
        {"_lseek", reinterpret_cast<void*>(crt_seek_fd)}, {"_lseeki64", reinterpret_cast<void*>(crt_seek64_fd)}, {"_eof", reinterpret_cast<void*>(crt_eof_fd)},
        {"_filelength", reinterpret_cast<void*>(crt_length_fd)}, {"_filelengthi64", reinterpret_cast<void*>(crt_length64_fd)},
    };
    for (const auto& entry : entries) if (name == entry.name) return entry.function; return nullptr;
}
