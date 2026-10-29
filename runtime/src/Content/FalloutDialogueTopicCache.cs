namespace OpenNV.Runtime.Content;

// Source declarations only. Selection and all gameplay state stay with callers.
internal sealed class FalloutDialogueTopicCache(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, FalloutDialogueTopic> _topics = [];

    internal FalloutDialogueTopic Read(FalloutFormKey form)
    {
        if (!_topics.TryGetValue(form, out var topic))
            _topics.Add(form, topic = FalloutDialogueTopic.Read(records, form));
        return topic;
    }
}
