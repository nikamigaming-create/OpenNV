namespace OpenNV.Runtime.Content;

internal interface IFalloutAbilityScriptLifetime
{
    void Synchronize();
    void RequireStarted(FalloutFormKey spell, IReadOnlyList<FalloutAbilityScript> scripts);
}
