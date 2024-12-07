using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PluginCore;

namespace Kitopia.Converter.SearchWindow;

public class ItemNameMatchCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var searchViewItem =
            ((Control)((Binding)parameter).DefaultAnchor.Target).DataContext as SearchViewItem;
        if (searchViewItem is not SearchViewItem str) return new InlineCollection();

        InlineCollection list = new();
        if (str.PinyinItem == null || str.PinyinItem.CharMatchResults == null || str.PinyinItem.SplitWords == null ||
            str.PinyinItem.CharMatchResults.Length == 0 ||
            str.PinyinItem.CharMatchResults.Length / 2 != str.PinyinItem.SplitWords.Length
           )
        {
            list.Add(new Run(str.ItemDisplayName));
            return list;
        }


        for (var i = 0; i < str.PinyinItem.SplitWords.Length; i++)
        {
            var inline = new Run(str.PinyinItem.SplitWords[i]);
            if (str.PinyinItem.CharMatchResults[i + str.PinyinItem.SplitWords.Length])
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