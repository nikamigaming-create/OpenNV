namespace OpenNV.Runtime.Content;

/// <summary>Camera policy for the source appearance graph, independent of model names.</summary>
internal static class FalloutNpcAppearanceSelfView
{
    internal const uint SelfHeadLayer = 1u << 19;
    private const uint HeadSlots = 0x00017e03;

    internal static bool IsHead(FalloutNpcAppearancePart part) =>
        (part.BipedSlots & HeadSlots) != 0 || part.Role is "head" or "hair" or "ears" or "mouth" or
            "teeth-lower" or "teeth-upper" or "tongue" or "eye-left" or "eye-right" or "head-addon";

    internal static bool RequireWholePartHead(FalloutNpcAppearancePart part)
    {
        var head = IsHead(part);
        if (head && (part.BipedSlots & ~HeadSlots) != 0)
            throw new NotSupportedException("Combined head/non-head equipment needs a source partition visibility binding.");
        return head;
    }

    internal static uint CameraMask(uint authoredMask, bool selfEye) =>
        selfEye ? authoredMask & ~SelfHeadLayer : authoredMask;
}
