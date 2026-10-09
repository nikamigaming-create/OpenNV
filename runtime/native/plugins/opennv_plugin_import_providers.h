#pragma once
#include "opennv_plugin_import_provider.h"
#include <map>
#include <memory>
#include <string>
#include <vector>

namespace opennv_domain {
enum class OriginalLoaderPhase : std::uint32_t { prepared = 1, entering = 2, mapped = 3, failed = 4, retired = 5 };
struct ImportProviderSourceExport { std::string name; std::uint32_t rva; };
struct ImportProviderOriginalSlot { std::string name; std::uint32_t rva; };
struct ImportedProvider {
    std::uint32_t kind = 0;
    std::wstring path;
    std::string library, sha;
    std::vector<ImportProviderSourceExport> exports;
    std::vector<ImportProviderOriginalSlot> slots;
    HMODULE image = nullptr;
    ImportProviderBind bind = nullptr;
    ImportProviderInspect inspect = nullptr;
    ImportProviderUnbind unbind = nullptr;
    ImportProviderSnapshot last{};
    bool bound = false, loader_released = false;
};
struct ImportProvidersRuntime {
    std::uint64_t attempt = 0, entered_call = 0;
    std::wstring original_path;
    std::string original_sha;
    OriginalLoaderPhase phase = OriginalLoaderPhase::prepared;
    std::vector<std::unique_ptr<ImportedProvider>> providers;
};
}
