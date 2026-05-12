using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PluginCore;

namespace KitopiaAvalonia.Converter.SearchWindow;

public class ItemNameMatchCtr : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var searchViewItem = values.Count > 1 ? values[1] as SearchViewItem : null;
        if (searchViewItem is not SearchViewItem str) return new InlineCollection();

        InlineCollection list = new();
        if (str.PinyinItem == null|| str.PinyinItem.Length!= str.ItemDisplayName?.Length)
        {
            list.Add(new Run(str.ItemDisplayName));
            return list;
        }


        for (var i = 0; i < str.PinyinItem.Length; i++)
        {
            var inline = new Run(str.ItemDisplayName[i].ToString());
            if (str.PinyinItem[i])
                inline.Foreground = Brushes.OrangeRed;

            list.Add(inline);
        }

        return list;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
