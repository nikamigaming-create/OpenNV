namespace OpenNV.Runtime.Content.Definitions;

// ACBS actor-base flags. Metadata: xEdit wbDefinitionsFNV.pas, wbActorBaseFlags.
// These source identifiers do not own mutable gameplay state.
[Flags]
internal enum FalloutActorBaseFlags : uint
{
    Essential = 1 << 1,
    NotPushable = 1 << 27,
    Invulnerable = 1 << 30,
}
