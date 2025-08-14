#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

#endregion

namespace KitopiaAvalonia.Converter.TaskEditor;

public class InterfacesToString : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "";

        if (value is List<string> list)
        {
            var stringBuilder = new StringBuilder();
            for (var i = 0; i < list.Count; i++)
            {
                stringBuilder.Append(list[i]); // 追加对象
                if (i < list.Count - 1) // 如果不是最后一个对象
                    stringBuilder.AppendLine(); // 添加换行符
            }

            return stringBuilder.ToString();
        }

        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !(bool)value;
    }
}