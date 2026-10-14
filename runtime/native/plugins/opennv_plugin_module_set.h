#pragma once
#include "opennv_plugin_nvse.h"
#include "opennv_plugin_module_diagnostic_crt.h"
#include <map>

namespace opennv_domain {
struct NvseModuleRecord {
    std::uint64_t id = 0;
    HMODULE image = nullptr;
    std::shared_ptr<NvseRuntime> runtime;
    std::uint32_t calls = 0, borrows = 0;
    bool supplemental = false, retired = false;
    std::unique_ptr<ModuleDiagnosticCrt> diagnostic;
};
struct NvseCallableBorrow {
    std::uint64_t id = 0, caller = 0, provider = 0;
    std::uint32_t pointer = 0, rva = 0, words = 0;
    Abi abi = Abi::thiscall_call;
    bool live = true, entered = false;
};
struct NvseModuleSet {
    std::map<std::uint64_t, std::unique_ptr<NvseModuleRecord>> modules;
    std::map<std::uint32_t, NvseModuleRecord*> images;
    std::map<std::uint64_t, NvseCallableBorrow> callables;
    std::uint64_t next_borrow = 0;
    std::uint64_t primary = 0;
    std::uint32_t live = 0;
    NvseTables* shared_tables = nullptr;
    std::uint32_t shared_tables_extent = 0;
};
}
