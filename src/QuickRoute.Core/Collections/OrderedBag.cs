using System;
using System.Collections;
using System.Collections.Generic;

namespace QuickRoute.BusinessEntities.Collections
{
  /// <summary>
  /// A sorted collection that allows duplicates. Equal items keep their insertion order.
  /// </summary>
  public class OrderedBag<T> : ICollection<T>
  {
    private readonly List<T> items = new List<T>();
    private readonly IComparer<T> comparer;

    public OrderedBag() : this(Comparer<T>.Default)
    {
    }

    public OrderedBag(IComparer<T> comparer)
    {
      this.comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    }

    public T this[int index] => items[index];

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(T item)
    {
      items.Insert(UpperBound(item), item);
    }

    public void AddMany(IEnumerable<T> collection)
    {
      foreach (var item in collection) Add(item);
    }

    public void Clear() => items.Clear();

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    /// <summary>
    /// Returns the index of the first item that compares equal to the specified item, or -1.
    /// </summary>
    public int IndexOf(T item)
    {
      var index = LowerBound(item);
      return index < items.Count && comparer.Compare(items[index], item) == 0 ? index : -1;
    }

    /// <summary>
    /// Removes the first item that compares equal to the specified item.
    /// </summary>
    public bool Remove(T item)
    {
      var index = IndexOf(item);
      if (index < 0) return false;
      items.RemoveAt(index);
      return true;
    }

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int LowerBound(T item)
    {
      int low = 0, high = items.Count;
      while (low < high)
      {
        var middle = (low + high) / 2;
        if (comparer.Compare(items[middle], item) < 0) low = middle + 1;
        else high = middle;
      }
      return low;
    }

    private int UpperBound(T item)
    {
      int low = 0, high = items.Count;
      while (low < high)
      {
        var middle = (low + high) / 2;
        if (comparer.Compare(items[middle], item) <= 0) low = middle + 1;
        else high = middle;
      }
      return low;
    }
  }

  public struct Pair<TFirst, TSecond>
  {
    public Pair(TFirst first, TSecond second)
    {
      First = first;
      Second = second;
    }

    public TFirst First { get; set; }

    public TSecond Second { get; set; }
  }
}
