#pragma once
#include <cstdint>
#include <string>

namespace opennv_domain {
struct ModuleDiagnosticCrt {
    std::string sha, stack, family, name, mode;
    std::uint32_t query = 0, producer = 0, stream = 0, name_rva = 0, mode_rva = 0;
    std::uint32_t first_return = 0, retry_return = 0, retry_limit = 0, attempt = 0;
    int sharing = 0;
    bool bound = false, entered = false, previous_returned = false;
    std::uint32_t previous_file = 0;
};
struct CrtOpenProducer { std::uint32_t caller = 0, cell = 0, value = 0, attempt = 0; int sharing = 0; };
}
