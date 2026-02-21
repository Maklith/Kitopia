using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.CustomScenario;
using NodifyM.Avalonia.Controls;
using NodifyM.Avalonia.ViewModelBase;

namespace KitopiaAvalonia;

public class FlowToDirectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectorItem connectionViewModelBase)
        {
            var connectorType = connectionViewModelBase.ConnectorType;
            switch (connectorType)
            {
                case ConnectorType.Input:
                    return ConnectionDirection.Backward;
                case ConnectorType.Output:
                    return ConnectionDirection.Forward;
                default:
                    return ConnectionDirection.Backward;
            }
            
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionDirection dir)
        {
            return dir == ConnectionDirection.Forward ? ConnectorViewModelBase.ConnectorFlow.Output : ConnectorViewModelBase.ConnectorFlow.Input;
        }

        return value;
    }
}