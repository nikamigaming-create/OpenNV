namespace OpenNV.Runtime.Content;

internal static class FalloutRagdollDriveSource
{
    internal static IReadOnlyList<string> Settings { get; } = Array.AsReadOnly(new[]
    {
        "fHierarchyGain:RagdollAnim", "fVelocityDamping:RagdollAnim", "fAccelerationGain:RagdollAnim",
        "fVelocityGain:RagdollAnim", "fPositionGain:RagdollAnim", "fPositionMaxLinearVelocity:RagdollAnim",
        "fPositionMaxAngularVelocity:RagdollAnim", "fSnapGain:RagdollAnim", "fSnapMaxLinearVelocity:RagdollAnim",
        "fSnapMaxAngularVelocity:RagdollAnim", "fSnapMaxLinearDistance:RagdollAnim", "fSnapMaxAngularDistance:RagdollAnim"
    });
    internal static IReadOnlyList<uint> Defaults { get; } = Array.AsReadOnly(new[]
    { .17f, 0f, 1f, .6f, .05f, 1.4f, 1.8f, .1f, .3f, .3f, .03f, .1f }.Select(BitConverter.SingleToUInt32Bits).ToArray());
}
