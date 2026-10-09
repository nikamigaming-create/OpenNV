#pragma once
#include <windows.h>
#include <cstdint>

// First-party fault telemetry, not an NVSE or original game object layout.
// Copy only the Windows-provided record while the SEH filter owns its lifetime.
namespace opennv_domain {
constexpr std::uint32_t native_exception_parameters = 15;
static_assert(EXCEPTION_MAXIMUM_PARAMETERS == native_exception_parameters);
static_assert(sizeof(void*) == 4, "Native exception receipts require the actual x86 companion.");

struct NativeExceptionObservation {
    std::uint32_t captured = 0, code = 0, thread = 0;
    std::uint32_t record_present = 0, record_pointer = 0, record_code = 0;
    std::uint32_t record_flags = 0, nested_record_pointer = 0, exception_address = 0;
    std::uint32_t context_present = 0, context_flags = 0, control_present = 0, program_counter = 0;
    std::uint32_t declared_parameters = 0, copied_parameters = 0;
    std::uint32_t parameters[native_exception_parameters]{};
};

inline std::uint32_t exception_pointer(const void* value) noexcept {
    return static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(value));
}

inline int CaptureNativeException(EXCEPTION_POINTERS* pointers, std::uint32_t code,
    std::uint32_t* exception_code, NativeExceptionObservation* destination) noexcept {
    *destination = {};
    destination->captured = 1;
    destination->code = code;
    destination->thread = GetCurrentThreadId();
    *exception_code = code;
    if (pointers && pointers->ExceptionRecord) {
        const auto& record = *pointers->ExceptionRecord;
        destination->record_present = 1;
        destination->record_pointer = exception_pointer(pointers->ExceptionRecord);
        destination->record_code = record.ExceptionCode;
        destination->record_flags = record.ExceptionFlags;
        destination->nested_record_pointer = exception_pointer(record.ExceptionRecord);
        destination->exception_address = exception_pointer(record.ExceptionAddress);
        destination->declared_parameters = record.NumberParameters;
        destination->copied_parameters = record.NumberParameters > native_exception_parameters
            ? native_exception_parameters : record.NumberParameters;
        for (std::uint32_t index = 0; index < destination->copied_parameters; ++index)
            destination->parameters[index] = static_cast<std::uint32_t>(record.ExceptionInformation[index]);
    }
    if (pointers && pointers->ContextRecord) {
        const auto& context = *pointers->ContextRecord;
        destination->context_present = 1;
        destination->context_flags = context.ContextFlags;
        if ((context.ContextFlags & CONTEXT_CONTROL) == CONTEXT_CONTROL) {
            destination->control_present = 1;
            destination->program_counter = context.Eip;
        }
    }
    // Preserve the existing fatal-generation policy. This never resumes an
    // original instruction or treats an exception as a successful call.
    return EXCEPTION_EXECUTE_HANDLER;
}
}
