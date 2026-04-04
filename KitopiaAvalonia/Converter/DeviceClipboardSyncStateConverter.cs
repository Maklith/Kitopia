using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore;

namespace KitopiaAvalonia.Converter;

public sealed class DeviceClipboardSyncStateConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3)
        {
            return false;
        }

        var rowDevice = values[0] as DeviceModel;
        var targetDevice = values[1] as DeviceModel;
        var isSyncEnabled = values[2] as bool? ?? false;
        if (!isSyncEnabled || rowDevice is null || targetDevice is null)
        {
            return false;
        }

        return IsSameDevice(rowDevice, targetDevice);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool IsSameDevice(DeviceModel a, DeviceModel b)
    {
        if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(b.Id))
        {
            return string.Equals(a.Id, b.Id, StringComparison.Ordinal);
        }

        return a.Port > 0 &&
               b.Port > 0 &&
               a.Port == b.Port &&
               string.Equals(a.Address.ToString(), b.Address.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
