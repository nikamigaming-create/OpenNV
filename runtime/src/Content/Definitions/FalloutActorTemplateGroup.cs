namespace OpenNV.Runtime.Content.Definitions;

// ACBS template-use flags. Layout provenance: xEdit wbDefinitionsFNV.pas,
// wbTemplateFlags / wbActorTemplateUseAIData.
internal enum FalloutActorTemplateGroup : ushort
{
    AiData = 1 << 4,
    BaseData = 1 << 7,
}
