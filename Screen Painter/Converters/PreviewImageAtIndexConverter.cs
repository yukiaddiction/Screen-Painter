using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Screen_Painter.Converters;

public class PreviewImageAtIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> paths)
            return null;

        if (!TryParseIndex(parameter, out int index))
            return null;

        if (index < 0 || index >= paths.Count)
            return null;

        return paths[index];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryParseIndex(object? parameter, out int index)
    {
        index = -1;
        return parameter switch
        {
            int i => Assign(i, out index),
            string s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out index),
            _ => false
        };
    }

    private static bool Assign(int value, out int index)
    {
        index = value;
        return true;
    }
}

public class PreviewCellVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> paths)
            return false;

        if (!int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            return false;

        return index >= 0 && index < paths.Count;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
