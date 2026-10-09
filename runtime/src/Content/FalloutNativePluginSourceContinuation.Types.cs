namespace OpenNV.Runtime.Content;

// These are original-source coordinates and mutable cursor state. Retail
// buffers, executable instructions, native pointers and caller allocations
// are never persistent launch inputs.
internal sealed record FalloutNativeBinarySourceState(string Plugin, string SourceSha256,
    string RuntimeSha256, string ConstructionOwner, uint BufferCapacity, uint BinaryOffset,
    uint BackendOffset, uint LogicalOffset, uint CachedSize, uint BufferBytes, uint BufferConsumed,
    string WrittenSha256, uint WrittenExtent, IReadOnlyList<FalloutNativeBinaryBufferRange> BufferSources,
    long Revision, bool Good);
internal sealed record FalloutNativeParserSourceState(string Plugin, string SourceSha256, long Revision,
    long? RecordHeaderOffset, uint? RawFormId, string? RecordHeaderSha256, string? BodySha256,
    uint DataOffset, uint ChunkType, uint ChunkBytes, uint BytesRead, bool AtEnd);
internal sealed record FalloutNativeLoadedSourceState(FalloutNativeParserSourceState Parser,
    FalloutNativeBinarySourceState Binary, FalloutNativeFileMetadataSnapshot Metadata, long Revision);
internal sealed record FalloutNativeSourceInput(string Plugin, string Sha256, int LoadOrder,
    long Bytes, IReadOnlyList<string> Masters);
internal sealed record FalloutNativeSourceContributor(string Plugin, string Sha256,
    FalloutNativeLoadedSourceState State);
internal sealed record FalloutNativeSourceCollectionState(string Schema, string RuntimeSha256,
    string? StackId, IReadOnlyList<FalloutNativeSourceInput> Selection,
    IReadOnlyList<FalloutNativeSourceContributor> Contributors);
internal sealed record FalloutNativeSourceModuleState(string LogicalPath, string Sha256,
    ulong CompletedSourceFileCalls, IReadOnlyList<FalloutNativeSourceModuleContributor> Contributors);
internal sealed record FalloutNativeSourceModuleContributor(string Key, string SourceOwner,
    string SourceSha256, string CurrentStateSha256, string StateSchema);
internal sealed record FalloutNativeSourceContinuationState(string Schema, string StackId,
    string RuntimeSha256, IReadOnlyList<FalloutNativeSourceModuleState> Modules,
    FalloutNativeSourceCollectionState? LoadedSources);
