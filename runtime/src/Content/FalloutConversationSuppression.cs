namespace OpenNV.Runtime.Content;

internal static class FalloutConversationSuppression
{
    internal static float Sample(FalloutNumericGameSettings settings, Func<float> random)
    {
        var first = settings.Float("fAISocialTimerForConversationsMin");
        var second = settings.Float("fAISocialTimerForConversationsMax");
        if (!float.IsFinite(first) || !float.IsFinite(second))
            throw new InvalidDataException("Conversation timer settings are not finite.");
        var minimum = Math.Min(first, second);
        var maximum = Math.Max(first, second);
        if (minimum == maximum) return minimum;
        var draw = random();
        if (!float.IsFinite(draw) || draw < 0 || draw > 1)
            throw new InvalidDataException("Conversation timer draw is outside the unit interval.");
        var value = (float)(minimum + ((double)maximum - minimum) * draw);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Conversation timer sample is not finite.");
    }

    internal static float Advance(float remaining, double seconds)
    {
        if (!float.IsFinite(remaining) || !double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Conversation timer continuation is invalid.");
        // The source decrements through zero. Selection tests positivity;
        // elapsed negative values must not become a new sampled interval.
        var next = (float)(remaining - seconds);
        return float.IsFinite(next) ? next : throw new InvalidDataException("Conversation timer exceeded finite storage.");
    }
}
