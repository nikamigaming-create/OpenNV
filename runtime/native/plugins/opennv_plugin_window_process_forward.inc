// Include in the domain's anonymous implementation namespace, after Frame/State
// and before opennv_plugin_nvse.inc; avoid a mismatched public-namespace linkage.
void* window_process_import_entry(const std::string&, const std::string&, void*);
void* window_process_dynamic_entry(HMODULE, const std::string&);
bool window_process_close_handle(HANDLE, BOOL&);
void window_process_dispatch(const Frame&);
void window_process_flush_detach(const Frame&);
void window_process_retire();
