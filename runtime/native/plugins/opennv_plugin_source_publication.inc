// Included inside the existing first-party domain namespace, immediately before
// source_files.inc. Replies use the real correlated expression_host transport.
constexpr std::uint32_t source_file_output_ack_event = 0x423, source_file_publication_ack_event = 0x424;
void source_file_request_prefix(Bytes&, std::uint64_t, const SourceObject&);

std::vector<std::uint8_t> source_native_readback(const void* address, std::uint32_t count) {
    if (!count || count > max_payload - 64)
        throw Fatal(ERROR_INVALID_DATA, "Source native acknowledgement has no bounded actual extent.");
    std::vector<std::uint8_t> observed(count); SIZE_T actual = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), address, observed.data(), count, &actual) || actual != count)
        throw Fatal(ERROR_PARTIAL_COPY, "Source native acknowledgement cannot read its complete actual allocation.");
    return observed;
}

void source_publication_ack(std::uint64_t caller, const SourceObject& object, std::uint64_t publication) {
    const auto observed = source_native_readback(reinterpret_cast<const void*>(object.pointer), object.extent);
    Bytes acknowledgement; source_file_request_prefix(acknowledgement, caller, object);
    acknowledgement.put(publication); acknowledgement.put(object.extent);
    acknowledgement.data.insert(acknowledgement.data.end(), observed.begin(), observed.end());
    if (expression_scalar(source_file_publication_ack_event, acknowledgement) != object.extent)
        throw Fatal(ERROR_INVALID_DATA, "Source contributor image has no complete native publication acknowledgement.");
}

void source_output_ack(std::uint64_t caller, const SourceObject& object, std::uint64_t transfer,
    std::uint32_t at, const void* destination, std::uint32_t count) {
    const auto observed = source_native_readback(destination, count);
    Bytes acknowledgement; source_file_request_prefix(acknowledgement, caller, object);
    acknowledgement.put(transfer); acknowledgement.put(at); acknowledgement.put(count);
    acknowledgement.data.insert(acknowledgement.data.end(), observed.begin(), observed.end());
    if (expression_scalar(source_file_output_ack_event, acknowledgement) != count)
        throw Fatal(ERROR_INVALID_DATA, "Source caller destination has no complete native output acknowledgement.");
}
