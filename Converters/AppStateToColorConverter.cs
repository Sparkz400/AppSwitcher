namespace AppSwitcher.Converters;

public class AppStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AppState state)
        {
            return state switch
            {
                AppState.Running => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ecc71")),
                AppState.Stopped => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95a5a6")),
                AppState.Crashed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e74c3c")),
                AppState.Restarting => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f39c12")),
                AppState.Unhealthy => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e67e22")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95a5a6"))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime startTime)
        {
            var elapsed = DateTime.Now - startTime;
            if (elapsed.TotalDays >= 1)
                return $"{(int)elapsed.TotalDays}d {elapsed.Hours:D2}:{elapsed.Minutes:D2}";
            return $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        return "-";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
