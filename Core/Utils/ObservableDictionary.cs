using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Core.Utils;

public class ObservableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IList<KeyValuePair<TKey, TValue>>, IList,
    INotifyCollectionChanged,
    INotifyPropertyChanged
{
    private int _index;

    public new KeyCollection Keys => base.Keys;

    public new ValueCollection Values => base.Values;

    public new TValue this[TKey key]
    {
        get => GetValue(key);
        set => SetValue(key, value);
    }

    public bool IsFixedSize { get; }

    object? IList.this[int index]
    {
        get => GetByIndex(index);
        set => SetIndexValue(index, value);
    }


    public int IndexOf(object? value)
    {
        if (value is KeyValuePair<TKey, TValue> keyValuePair) return IndexOf(keyValuePair.Key);

        return -1;
    }

    public bool Contains(object? value)
    {
        throw new NotImplementedException();
    }

    public void Insert(int index, object? value)
    {
        throw new NotImplementedException();
    }

    public void Remove(object? value)
    {
        throw new NotImplementedException();
    }

    public int Add(object? value)
    {
        throw new NotImplementedException();
    }

    public bool IsReadOnly { get; }

    public new int Count => base.Count;

    public KeyValuePair<TKey, TValue> this[int index]
    {
        get => GetByIndex(index);
        set => SetIndexValue(index, value);
    }

    public int IndexOf(KeyValuePair<TKey, TValue> item)
    {
        return IndexOf(item.Key);
    }

    public void Insert(int index, KeyValuePair<TKey, TValue> item)
    {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotImplementedException();
    }


    public new void Clear()
    {
        base.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged("Keys");
        OnPropertyChanged("Values");
        OnPropertyChanged("Count");
    }


    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public KeyValuePair<TKey, TValue> GetByIndex(int index)
    {
        if (index >= 0)
            foreach (var source1 in this)
            {
                if (index == 0) return source1;

                --index;
            }

        return default;
    }

    public new void Add(TKey key, TValue value)
    {
        TryAdd(key, value);
        OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, FindPair(key), _index));
        OnPropertyChanged("Keys");
        OnPropertyChanged("Values");
        OnPropertyChanged("Count");
    }


    public new bool Remove(TKey key)
    {
        var pair = FindPair(key);
        if (base.Remove(key))
        {
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, pair, _index));
            OnPropertyChanged("Keys");
            OnPropertyChanged("Values");
            OnPropertyChanged("Count");
            return true;
        }

        return false;
    }

    protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (CollectionChanged != null) CollectionChanged(this, e);
    }

    protected void OnPropertyChanged(string propertyName)
    {
        if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    #region private方法

    private void SetIndexValue(int index, TValue value)
    {
        try
        {
            var pair = GetByIndex(index);
            SetValue(pair.Key, value);
        }
        catch (Exception)
        {
        }
    }

    private void SetIndexValue(int index, object value)
    {
        if (value is not KeyValuePair<TKey, TValue> keyValuePair)
            return;
        try
        {
            var pair = GetByIndex(index);
            SetValue(pair.Key, keyValuePair.Value);
        }
        catch (Exception)
        {
        }
    }

    private void SetIndexValue(int index, KeyValuePair<TKey, TValue> value)
    {
        try
        {
            var pair = GetByIndex(index);
            SetValue(pair.Key, value.Value);
        }
        catch (Exception)
        {
        }
    }

    private TValue GetValue(TKey key)
    {
        if (ContainsKey(key))
            return base[key];
        else
            return default;
    }

    public void SetValueWithoutNotify(TKey key, TValue value)
    {
        if (ContainsKey(key))
            base[key] = value;
        else
            Add(key, value);
    }

    private void SetValue(TKey key, TValue value)
    {
        if (ContainsKey(key))
        {
            var pair = FindPair(key);
            var index = _index;
            base[key] = value;
            var newpair = FindPair(key);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newpair,
                pair, index));
            OnPropertyChanged("Values");
            OnPropertyChanged("Item[]");
        }
        else
        {
            Add(key, value);
        }
    }

    private KeyValuePair<TKey, TValue> FindPair(TKey key)
    {
        _index = 0;
        foreach (var item in this)
        {
            if (item.Key.Equals(key)) return item;

            _index++;
        }

        return default;
    }

    private int IndexOf(TKey key)
    {
        var index = 0;
        foreach (var item in this)
        {
            if (item.Key.Equals(key)) return index;

            index++;
        }

        return -1;
    }

    #endregion
}