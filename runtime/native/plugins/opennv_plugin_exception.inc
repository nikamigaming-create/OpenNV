// Included after the first-party Fatal and Bytes owners, in their namespace.
// Addresses are carried only by the private fault capsule, never its text reason.
void native_exception_raise(const CallReceipt& receipt, const std::string& stage,
    std::uint32_t entry, std::uint32_t prior_callback_code = 0,
    const char* prior_callback_reason = nullptr, bool prior_callback_truncated = false) {
    if (!receipt.exception.captured) {
        if (receipt.exception_code)
            throw Fatal(ERROR_INVALID_DATA, "Guarded native exception lost its Windows context receipt in " + stage + ".");
        return;
    }
    char code[11]{};
    std::snprintf(code, sizeof(code), "0x%08lx", static_cast<unsigned long>(receipt.exception.code));
    auto reason = "Native call failed in " + stage + " (exception code " + code + ").";
    if (prior_callback_code)
        reason += " Source callback failure (" + std::to_string(prior_callback_code) + "): " +
            (prior_callback_reason && *prior_callback_reason ? prior_callback_reason : "NVSE callback ownership failed.");
    if (prior_callback_truncated) reason += " [Native callback diagnostics truncated.]";
    // Windows permits a caller-supplied zero software exception code. The fault
    // envelope still needs a nonzero transport failure; the actual code remains
    // unchanged in the capsule and product reason.
    Fatal failure(prior_callback_code ? prior_callback_code :
        receipt.exception.code ? receipt.exception.code : ERROR_UNHANDLED_EXCEPTION, reason);
    failure.exception = receipt.exception;
    failure.exception_stage = stage;
    failure.invocation_entry = entry;
    failure.prior_callback_code = prior_callback_code;
    if (prior_callback_code && prior_callback_reason) failure.prior_callback_reason = prior_callback_reason;
    failure.prior_callback_truncated = prior_callback_truncated;
    throw failure;
}

void remember_native_exception(std::exception_ptr& retained, const Fatal& failure) noexcept {
    // The public adapter cannot unwind C++ across the original ABI. Retain a
    // genuinely caught inner fault before its existing first-party abort raises
    // another SEH exception in the enclosing guarded invocation.
    if (failure.exception.captured && !retained) retained = std::current_exception();
}

void native_exception_bytes(Bytes& bytes, const Fatal& failure) {
    const auto& observed = failure.exception;
    bytes.put(observed.captured);
    if (!observed.captured) return;
    bytes.put(1U); // Private capsule schema, separate from public NVSE versions.
    bytes.put(observed.thread);
    bytes.text(failure.exception_stage);
    bytes.put(failure.invocation_entry);
    bytes.put(observed.code);
    bytes.put(observed.record_present);
    bytes.put(observed.record_pointer);
    bytes.put(observed.record_code);
    bytes.put(observed.record_flags);
    bytes.put(observed.nested_record_pointer);
    bytes.put(observed.exception_address);
    bytes.put(observed.context_present);
    bytes.put(observed.context_flags);
    bytes.put(observed.control_present);
    bytes.put(observed.program_counter);
    bytes.put(observed.declared_parameters);
    bytes.put(observed.copied_parameters);
    for (std::uint32_t index = 0; index < observed.copied_parameters; ++index)
        bytes.put(observed.parameters[index]);
    bytes.put(failure.prior_callback_code);
    bytes.text(failure.prior_callback_reason);
    bytes.put(failure.prior_callback_truncated ? 1U : 0U);
}
