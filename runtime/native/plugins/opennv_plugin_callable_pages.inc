// Shared with the contributor parser and future genuine source methods.
// Include after State/Fatal and before the source-file/binary-family includes.
// One process generation retains its fixed-address reservations to closure.
std::unique_ptr<OriginalCallablePages> original_callable_pages;
std::uint64_t source_callables_acquire(const std::vector<CallableEntry>& calls) {
    if (!state->calls.empty()) throw Fatal(ERROR_BUSY, "Source callable publication overlaps an actual native call.");
    try {
        if (!original_callable_pages) original_callable_pages = std::make_unique<OriginalCallablePages>();
        return original_callable_pages->acquire(calls);
    }
    catch (const CallablePageFailure& error) { throw Fatal(error.code, error.what()); }
}
void source_callables_release(std::uint64_t lease) {
    if (!state->calls.empty() || !original_callable_pages) throw Fatal(ERROR_BUSY, "Source callable retirement lacks its idle generation owner.");
    try { original_callable_pages->release(lease); }
    catch (const CallablePageFailure& error) { throw Fatal(error.code, error.what()); }
}
void source_callables_close() {
    if (!state->calls.empty()) throw Fatal(ERROR_BUSY, "Source callable generation closure overlaps an actual native call.");
    if (!original_callable_pages) return;
    try { original_callable_pages->close(); original_callable_pages.reset(); }
    catch (const CallablePageFailure& error) { throw Fatal(error.code, error.what()); }
}
