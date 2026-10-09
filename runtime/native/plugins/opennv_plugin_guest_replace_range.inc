// Include inside GuestArena's public section. The full before/after bytes and
// retained native allocation authorize this bounded range; the writable window
// is retired before the native callback can acknowledge delivery.
void replace_read_only_range(std::uint64_t id, std::uint32_t offset, const void* before,
    const void* after, std::uint32_t count) {
    auto& region = find(id); extent(region, offset, count);
    auto* destination = static_cast<std::uint8_t*>(region.base) + offset;
    if (region.state_object || region.access != GuestAccess::read_only || std::memcmp(destination, before, count))
        throw GuestArenaFailure(ERROR_INVALID_DATA, "Native readonly range changed before its actual source publication.", true);
    DWORD previous = 0;
    if (!VirtualProtect(region.base, region.committed, PAGE_READWRITE, &previous))
        throw GuestArenaFailure(GetLastError(), "Native source range could not acquire its writable publication.", true);
    if (previous != PAGE_READONLY) {
        DWORD restored = 0;
        if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &restored))
            throw GuestArenaFailure(GetLastError(), "Unexpected native range protection could not retire.", true);
        throw GuestArenaFailure(ERROR_INVALID_DATA, "Native source range acquired unexpected protection.", true);
    }
    std::memcpy(destination, after, count); DWORD writable = 0;
    if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &writable)) {
        const auto first = GetLastError(); DWORD restored = 0;
        if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &restored))
            throw GuestArenaFailure(GetLastError(), "Failed native source range could not retire its writable window.", true);
        throw GuestArenaFailure(first, "Native source range publication failed readonly retirement.", true);
    }
    if (writable != PAGE_READWRITE || std::memcmp(destination, after, count))
        throw GuestArenaFailure(ERROR_INVALID_DATA, "Native source range differs from its complete readonly publication.", true);
    verify(region);
}
