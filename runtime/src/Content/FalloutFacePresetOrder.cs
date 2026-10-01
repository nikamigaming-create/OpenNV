namespace OpenNV.Runtime.Content;

// Preset names are not unique across a combined installation. The source name
// sort is unstable: a small partition chooses the first strict maximum, while
// a large partition uses the upper middle element and excludes equal pivots.
// Keep those tie semantics independently of FormID order and .NET's sorter.
internal static class FalloutFacePresetOrder
{
    internal static void Sort<T>(T[] values, Comparison<T> compare)
    {
        if (values.Length < 2) return;
        var pending = new Stack<(int First, int Last)>();
        pending.Push((0, values.Length - 1));
        while (pending.TryPop(out var range))
        {
            var (first, last) = range;
            if (last - first < 8)
            {
                for (var end = last; end > first; end--)
                {
                    var largest = first;
                    for (var index = first + 1; index <= end; index++)
                        if (compare(values[index], values[largest]) > 0) largest = index;
                    Swap(largest, end);
                }
                continue;
            }
            var pivot = first + (last - first + 1) / 2;
            Order(first, pivot); Order(first, last); Order(pivot, last);
            var left = first; var right = last;
            while (true)
            {
                if (left < pivot)
                    do { left++; } while (left < pivot && compare(values[left], values[pivot]) <= 0);
                if (left >= pivot)
                    do { left++; } while (left <= last && compare(values[left], values[pivot]) <= 0);
                do { right--; } while (right > pivot && compare(values[right], values[pivot]) > 0);
                if (left > right) break;
                Swap(left, right);
                if (pivot == right) pivot = left;
            }
            right++;
            if (pivot < right)
                do { right--; } while (right > pivot && compare(values[right], values[pivot]) == 0);
            if (right <= pivot)
                do { right--; } while (right > first && compare(values[right], values[pivot]) == 0);
            if (first < right) pending.Push((first, right));
            if (left < last) pending.Push((left, last));
        }
        void Order(int first, int second) { if (compare(values[first], values[second]) > 0) Swap(first, second); }
        void Swap(int first, int second) => (values[first], values[second]) = (values[second], values[first]);
    }
}
