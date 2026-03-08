using System.Collections;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Random;

namespace BaseLib.Utils;

public class WeightedList<T> : IList<T>
{
    private readonly List<WeightedItem> _items = [];
    private int _totalWeight;
    
    public T GetRandom(Rng rng) {
        return GetRandom(rng, false);
    }

    public T GetRandom(Rng rng, bool remove)
    {
        if (Count == 0) throw new IndexOutOfRangeException("Attempted to roll on empty WeightedList");
        
        var roll = rng.NextInt(_totalWeight);
        var currentWeight = 0;

        WeightedItem? selected = null;
        foreach (var item in _items) {
            if (currentWeight + item.Weight >= roll) {
                selected = item;
                break;
            }
            currentWeight += item.Weight;
        }

        if (selected != null) {
            if (remove)
            {
                _items.Remove(selected);
                _totalWeight -= selected.Weight;
            }
            return selected.Val;
        }

        throw new Exception($"Roll {roll} failed to get a value in list of total weight {_totalWeight}");
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        return _items.Select(item => item.Val).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(T item)
    {
        Add(item, 1);
    }
    public void Add(T item, int weight) {
        _totalWeight += weight;
        _items.Add(new WeightedItem(item, weight));
    }

    public void Clear()
    {
        _items.Clear();
        _totalWeight = 0;
    }

    public bool Contains(T val)
    {
        return _items.Any(item => Equals(item.Val, val));
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _items.Select(item => item.Val).ToList().CopyTo(array, arrayIndex);
    }

    public bool Remove(T val)
    {
        var entry = _items.Find(item => Equals(item.Val, val));
        if (entry != null)
        {
            _items.Remove(entry);
            _totalWeight -= entry.Weight;
            return true;
        }

        return false;
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public int IndexOf(T val)
    {
        return _items.FirstIndex(item => Equals(item.Val, val));
    }

    public void Insert(int index, T item)
    {
        Insert(index, item, 1);
    }
    
    public void Insert(int index, T item, int weight)
    {
        _items.Insert(index, new WeightedItem(item, weight));
        _totalWeight += weight;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _totalWeight -= item.Weight;
    }

    public T this[int index]
    {
        get => _items[index].Val;
        set => _items[index].Val = value;
    }

    private class WeightedItem
    {
        public int Weight { get; }
        public T Val { get; set; }

        public WeightedItem(T val, int weight) {
            Weight = weight;
            Val = val;
        }
    }
}