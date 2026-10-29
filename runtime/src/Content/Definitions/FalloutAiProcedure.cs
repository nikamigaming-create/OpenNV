namespace OpenNV.Runtime.Content.Definitions;

// Public GetCurrentAIProcedure values, distinct from internal package kinds.
// https://geckwiki.com/index.php?title=GetCurrentAIProcedure
internal enum FalloutAiProcedure
{
    Travel = 0,
    Wander = 7,
    Sleep = 8,
    Eat = 10,
    GetUp = 21
}
