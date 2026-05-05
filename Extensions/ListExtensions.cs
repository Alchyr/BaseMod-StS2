namespace BaseLib.Extensions;

public static class ListExtensions
{
    /// <summary>
    /// Insert an item into a list in an already sorted position with its default comparison. The list must also already be sorted.
    /// </summary>
    public static void InsertSorted<T>(this List<T> list, T item) where T : IComparable<T>
    {
        if (list.Count == 0 || list[^1].CompareTo(item) <= 0)
        {
            list.Add(item);
            return;
        }

        if (list[0].CompareTo(item) >= 0)
        {
            list.Insert(0, item);
            return;
        }

        int index = list.BinarySearch(item);
        if (index < 0)
            index = ~index;
        list.Insert(index, item);
    }

    /// <summary>
    /// Insert an item into a list in an already sorted position. The list must also already be sorted.
    /// </summary>
    public static void InsertSorted<T>(this List<T> list, T item, IComparer<T> comparer)
    {
        if (list.Count == 0 || comparer.Compare(list[^1], item) <= 0)
        {
            list.Add(item);
            return;
        }

        if (comparer.Compare(list[0], item) >= 0)
        {
            list.Insert(0, item);
            return;
        }

        int index = list.BinarySearch(item, comparer);
        if (index < 0)
            index = ~index;
        list.Insert(index, item);
    }
}