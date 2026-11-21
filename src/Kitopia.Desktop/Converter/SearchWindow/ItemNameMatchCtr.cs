using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kitopia.Feature.Localization;
using PluginCore;

namespace Kitopia.Desktop.Converter.SearchWindow;

public class ItemNameMatchCtr : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var searchViewItem = values.Count > 1 ? values[1] as SearchViewItem : null;
        if (searchViewItem is not SearchViewItem str) return new InlineCollection();

        InlineCollection list = new();
        if (str.PinyinItem == null || str.PinyinItem.Length == 0 || str.PinyinItem.Length != str.ItemDisplayName?.Length)
        {
            list.Add(new Run(Lang.Get(str.ItemDisplayName)));
            return list;
        }


        var spanStart = 0;
        var currentMatched = str.PinyinItem[0];
        for (var i = 1; i <= str.PinyinItem.Length; i++)
        {
            if (i < str.PinyinItem.Length && str.PinyinItem[i] == currentMatched) continue;
            var inline = new Run(str.ItemDisplayName[spanStart..i]);
            if (currentMatched) inline.Foreground = Brushes.OrangeRed;
            list.Add(inline);
            if (i < str.PinyinItem.Length)
            {
                spanStart = i;
                currentMatched = str.PinyinItem[i];
            }
        }

        return list;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
