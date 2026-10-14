#pragma once
#include <cstdint>
#include "opennv_plugin_domain.h"
namespace opennv_domain {
constexpr std::uint32_t module_read_io_callback = 33;
constexpr std::uint32_t module_read_io_buffer_limit = max_payload / 4;
}
