struct ModuleCppSource {
    std::wstring path;
    std::string sha;
    std::vector<CppRuntimeExport> exports;
    std::map<std::string, std::size_t> index;
    HMODULE image = nullptr;
};
ModuleCppSource module_cpp_read(Reader& reader) {
    ModuleCppSource result; const auto count = reader.get<std::uint32_t>();
    if (!count) return result;
    if (count > 128) throw Fatal(ERROR_INVALID_DATA, "Additional C++ source exceeds its bounded public export declaration.");
    result.path = path_from_utf8(reader.text()); result.sha = reader.text();
    if (result.path.empty() || result.sha.size() != 64) throw Fatal(ERROR_INVALID_DATA, "Additional C++ source lost its actual provider identity.");
    std::set<std::string> names;
    for (std::uint32_t at = 0; at < count; ++at) {
        const auto name = reader.text(); const auto rva = reader.get<std::uint32_t>(), code = reader.get<std::uint32_t>();
        if (name.empty() || !rva || code > 1 || !names.insert(name).second)
            throw Fatal(ERROR_INVALID_DATA, "Additional C++ source has an unknown/repeated public code-data export.");
        result.index.emplace(name, result.exports.size()); result.exports.push_back({name, rva, code != 0, nullptr, false});
    }
    return result;
}
void module_cpp_prepare(ModuleCppSource& source) {
    if (source.exports.empty()) return;
    if (!state || !state->io || !state->io->cpp || !state->io->crt || !state->calls.empty() || GetCurrentThreadId() != state->thread)
        throw Fatal(ERROR_NOT_SUPPORTED, "Additional C++ source requires its existing actual shared object/CRT lifetime.");
    auto& provider = *state->io->cpp;
    if (!provider.retained || !provider.published || provider.retired || provider.retiring ||
        provider.path != source.path || provider.sha != source.sha || GetModuleHandleW(L"msvcp140.dll") != provider.retained)
        throw Fatal(ERROR_INVALID_DATA, "Additional C++ source is not the already mapped retained original provider.");
    std::array<wchar_t, 32768> mapped{};
    const auto length = GetModuleFileNameW(provider.retained, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!length || length >= mapped.size()) throw Fatal(GetLastError(), "Additional C++ provider mapped file identity is unavailable.");
    crt_require_provider_file(source.path, mapped.data()); source.image = provider.retained;
    // Build an index over the same actual provider table once. No peer object,
    // shadow provider or FILE table is constructed by this borrow.
    std::map<std::string, std::size_t> existing;
    for (std::size_t at = 0; at < provider.exports.size(); ++at)
        if (!existing.emplace(provider.exports[at].name, at).second) throw Fatal(ERROR_INVALID_DATA, "Shared C++ provider repeats an export lifetime.");
    for (auto& item : source.exports) {
        auto* pointer = reinterpret_cast<void*>(GetProcAddress(provider.retained, item.name.c_str()));
        if (!pointer || static_cast<std::uint64_t>(address(provider.retained)) + item.rva != address(pointer))
            throw Fatal(ERROR_INVALID_DATA, "Additional C++ source export differs from the exact current provider address/RVA.");
        MEMORY_BASIC_INFORMATION region{};
        if (VirtualQuery(pointer, &region, sizeof(region)) != sizeof(region) || region.AllocationBase != provider.retained ||
            region.State != MEM_COMMIT || region.Protect & (PAGE_NOACCESS | PAGE_GUARD))
            throw Fatal(ERROR_INVALID_ADDRESS, "Additional C++ export has no actual retained image lifetime.");
        const auto protection = region.Protect & 0xffU;
        const bool code = protection == PAGE_EXECUTE || protection == PAGE_EXECUTE_READ || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
        if (code != item.code) throw Fatal(ERROR_INVALID_DATA, "Additional C++ code/data kind changed from its exact source ABI.");
        item.actual = pointer;
        const auto found = existing.find(item.name);
        if (found != existing.end()) {
            const auto& old = provider.exports[found->second];
            if (old.rva != item.rva || old.code != item.code || old.actual != item.actual)
                throw Fatal(ERROR_INVALID_DATA, "Additional C++ borrow attempts to replace an existing shared export.");
        } else { existing.emplace(item.name, provider.exports.size()); provider.exports.push_back(item); }
    }
}
void* module_cpp_import_entry(const ModuleCppSource& source, const std::string& library, const std::string& name, void* original) {
    if (library != "msvcp140.dll") return nullptr;
    const auto found = source.index.find(name);
    if (found == source.index.end() || !source.image || source.exports[found->second].actual != original)
        throw Fatal(ERROR_INVALID_DATA, "Additional C++ IAT lost its actual selected public export borrow.");
    if (name == cpp_open_wide) return reinterpret_cast<void*>(cpp_fiopen_w);
    if (name == cpp_open_narrow) return reinterpret_cast<void*>(cpp_fiopen_a);
    return original; // Preserve actual code/data and all original C++ ABIs.
}
void module_cpp_bytes(Bytes& result, const ModuleCppSource& source) {
    result.put(address(source.image)); result.put(static_cast<std::uint32_t>(source.exports.size()));
    for (const auto& item : source.exports) {
        result.text(item.name); result.put(item.rva); result.put(address(item.actual)); result.put(item.code ? 1U : 0U);
    }
}
void* module_runtime_import_entry(const std::string& library, const std::string& name, void* original) {
    const bool ucrt = library == "ucrtbase.dll" || library.compare(0, 15, "api-ms-win-crt-") == 0;
    if (!ucrt) return nullptr;
    if (!state->io || !state->io->crt || state->io->crt->providers.size() != 1 || !state->io->crt->providers[0]->retained)
        throw Fatal(ERROR_NOT_SUPPORTED, "Additional CRT import lost its existing actual shared provider.");
    auto& provider = *state->io->crt->providers[0];
    HMODULE actual = nullptr;
    if (!original || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(original), &actual) || actual != provider.retained ||
        reinterpret_cast<void*>(GetProcAddress(provider.retained, name.c_str())) != original)
        throw Fatal(ERROR_INVALID_DATA, "Additional CRT export escaped the genuine shared provider lifetime.");
    // All admitted FILE signatures have existing typed wrappers. Their peer
    // guard fails before touching the original SDK and remains sticky through
    // catch-and-continue in the original caller. Pure/runtime ABIs stay real.
    if (auto* entry = crt_entry(name)) return entry;
    if (auto* entry = crt_standard_entry(name)) return entry;
    return original;
}
