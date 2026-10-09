// Included inside the companion namespace. Windows still owns resolution,
// TLS and DllMain execution. This observer never handles an exception or
// substitutes a successful loader result.
struct LoaderExceptionObservation {
    DWORD code = 0, flags = 0, parameter_count = 0;
    std::uintptr_t instruction = 0, allocation = 0, stack = 0;
    DWORD memory_state = 0, memory_protection = 0, memory_type = 0;
    ULONG_PTR parameters[EXCEPTION_MAXIMUM_PARAMETERS]{};
};
struct LoaderInvocation;
thread_local LoaderInvocation* current_loader_invocation = nullptr;

struct LoaderInvocation {
    static constexpr std::size_t observation_capacity = 8;
    LoaderExceptionObservation observations[observation_capacity]{};
    LoaderExceptionObservation last{};
    std::uint64_t count = 0;
    bool count_overflow = false;
    void* registration = nullptr;

    static LONG CALLBACK observe(EXCEPTION_POINTERS* pointers) noexcept {
        auto* owner = current_loader_invocation;
        if (!owner || !pointers || !pointers->ExceptionRecord) return EXCEPTION_CONTINUE_SEARCH;
        const auto previous_error = GetLastError();
        const auto& exception = *pointers->ExceptionRecord;
        LoaderExceptionObservation observation{};
        observation.code = exception.ExceptionCode; observation.flags = exception.ExceptionFlags;
        observation.instruction = reinterpret_cast<std::uintptr_t>(exception.ExceptionAddress);
        observation.parameter_count = exception.NumberParameters;
        const auto bounded = exception.NumberParameters < EXCEPTION_MAXIMUM_PARAMETERS ?
            exception.NumberParameters : EXCEPTION_MAXIMUM_PARAMETERS;
        for (DWORD at = 0; at < bounded; ++at) observation.parameters[at] = exception.ExceptionInformation[at];
        if (pointers->ContextRecord) observation.stack = pointers->ContextRecord->Esp;
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(exception.ExceptionAddress, &memory, sizeof(memory))) {
            observation.allocation = reinterpret_cast<std::uintptr_t>(memory.AllocationBase);
            observation.memory_state = memory.State; observation.memory_protection = memory.Protect;
            observation.memory_type = memory.Type;
        }
        if (owner->count < observation_capacity) owner->observations[static_cast<std::size_t>(owner->count)] = observation;
        owner->last = observation;
        if (owner->count == std::numeric_limits<std::uint64_t>::max()) owner->count_overflow = true;
        else ++owner->count;
        SetLastError(previous_error);
        return EXCEPTION_CONTINUE_SEARCH;
    }
    LoaderInvocation() {
        if (current_loader_invocation) throw Fatal(ERROR_BUSY, "Windows loader observation already belongs to an entered invocation.");
        registration = AddVectoredExceptionHandler(1, observe);
        if (!registration) throw Fatal(ERROR_INVALID_STATE, "Windows loader exception observation could not enter its actual registration.");
        current_loader_invocation = this;
    }
    LoaderInvocation(const LoaderInvocation&) = delete;
    LoaderInvocation& operator=(const LoaderInvocation&) = delete;
    ~LoaderInvocation() {
        if (current_loader_invocation == this) current_loader_invocation = nullptr;
        if (registration) RemoveVectoredExceptionHandler(registration);
    }
    void finish() {
        if (current_loader_invocation != this || !registration)
            throw Fatal(ERROR_INVALID_STATE, "Windows loader observation lost its entered current-thread registration.");
        current_loader_invocation = nullptr;
        if (!RemoveVectoredExceptionHandler(registration))
            throw Fatal(ERROR_INVALID_STATE, "Windows loader exception observation registration did not retire.");
        registration = nullptr;
    }
    static void append(std::string& result, const LoaderExceptionObservation& observation) {
        char fields[256]{};
        const auto used = std::snprintf(fields, sizeof(fields),
            " code=0x%08lx flags=0x%08lx instruction=0x%08lx allocation=0x%08lx stack=0x%08lx state=0x%08lx protection=0x%08lx type=0x%08lx parameters=%lu",
            static_cast<unsigned long>(observation.code), static_cast<unsigned long>(observation.flags),
            static_cast<unsigned long>(observation.instruction), static_cast<unsigned long>(observation.allocation),
            static_cast<unsigned long>(observation.stack), static_cast<unsigned long>(observation.memory_state),
            static_cast<unsigned long>(observation.memory_protection), static_cast<unsigned long>(observation.memory_type),
            static_cast<unsigned long>(observation.parameter_count));
        if (used < 0 || static_cast<std::size_t>(used) >= sizeof(fields))
            throw Fatal(ERROR_INVALID_DATA, "Windows loader exception observation formatting exceeded its actual extent.");
        result.append(fields, static_cast<std::size_t>(used));
        const auto bounded = observation.parameter_count < EXCEPTION_MAXIMUM_PARAMETERS ?
            observation.parameter_count : EXCEPTION_MAXIMUM_PARAMETERS;
        for (DWORD at = 0; at < bounded; ++at) {
            char parameter[24]{};
            const auto length = std::snprintf(parameter, sizeof(parameter), " 0x%08lx", static_cast<unsigned long>(observation.parameters[at]));
            if (length < 0 || static_cast<std::size_t>(length) >= sizeof(parameter))
                throw Fatal(ERROR_INVALID_DATA, "Windows loader exception parameter formatting exceeded its actual extent.");
            result.append(parameter, static_cast<std::size_t>(length));
        }
    }
    std::string refusal(const char* prefix) const {
        auto result = std::string(prefix) + " Current-thread first-chance observations=" + std::to_string(count);
        if (count_overflow) result += " [count overflow]";
        const auto stored = count < observation_capacity ? static_cast<std::size_t>(count) : observation_capacity;
        for (std::size_t at = 0; at < stored; ++at) {
            result += " [" + std::to_string(at + 1); append(result, observations[at]); result += "]";
        }
        if (count > observation_capacity || count_overflow) {
            result += " [intermediate observations exceed retained extent; last"; append(result, last); result += "]";
        }
        return result;
    }
};
