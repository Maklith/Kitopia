namespace Core.Utils.Ex;

public static class DictionaryEx
{
    public static Dictionary<TKey, int> AddOrIncrease<TKey>(this Dictionary<TKey, int> dictionary, TKey stringName)
    {
        if (!dictionary.TryAdd(stringName, 1)) dictionary[stringName]++;

        return dictionary;
    }

    public static Dictionary<TKey, int> DelOrDecrease<TKey>(this Dictionary<TKey, int> dictionary, TKey stringName)
    {
        if (dictionary.ContainsKey(stringName))
        {
            dictionary[stringName]--;
            if (dictionary[stringName] <= 0) dictionary.Remove(stringName);
        }

        return dictionary;
    }
}