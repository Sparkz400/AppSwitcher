using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AppSwitcher.Converters
{
    public class ShapeToGeometryConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not string shape || values[1] is not double size)
                return null;

            return CreateGeometry(shape.ToLower(), size);
        }

        private Geometry CreateGeometry(string shape, double size)
        {
            return shape switch
            {
                "circle" => new EllipseGeometry(new Rect(0, 0, size, size)),
                "square" => new RectangleGeometry(new Rect(0, 0, size, size)),
                "roundedsquare" => new RectangleGeometry(new Rect(0, 0, size, size), size * 0.25, size * 0.25),
                "diamond" => CreatePolygon(size, 4, -Math.PI / 2),
                "pentagon" => CreatePolygon(size, 5, -Math.PI / 2),
                "octagon" => CreatePolygon(size, 8, -Math.PI / 8),
                "star" => CreateStar(size, 5),
                _ => CreatePolygon(size, 6, -Math.PI / 2 + Math.PI / 6), // Hexagon
            };
        }

        private PathGeometry CreatePolygon(double size, int sides, double startAngle)
        {
            var center = new Point(size / 2, size / 2);
            var radius = size / 2;
            var poly = new PolyLineSegment();
            var startPoint = new Point();

            for (int i = 0; i < sides; i++)
            {
                double angle = (i * 2 * Math.PI / sides) + startAngle;
                var p = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                if (i == 0) startPoint = p;
                else poly.Points.Add(p);
            }
            poly.Points.Add(startPoint);

            var fig = new PathFigure { StartPoint = startPoint, IsClosed = true };
            fig.Segments.Add(poly);
            return new PathGeometry(new[] { fig });
        }

        private PathGeometry CreateStar(double size, int points)
        {
            var center = new Point(size / 2, size / 2);
            var outerR = size / 2;
            var innerR = size / 4.5;
            var poly = new PolyLineSegment();
            var startPoint = new Point();
            int totalPoints = points * 2;

            for (int i = 0; i < totalPoints; i++)
            {
                double angle = (i * Math.PI / points) - (Math.PI / 2);
                double r = (i % 2 == 0) ? outerR : innerR;
                var p = new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
                if (i == 0) startPoint = p;
                else poly.Points.Add(p);
            }
            poly.Points.Add(startPoint);

            var fig = new PathFigure { StartPoint = startPoint, IsClosed = true };
            fig.Segments.Add(poly);
            return new PathGeometry(new[] { fig });
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToExpanderIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? "▼" : "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Returns Visible when the bound IconStyle string matches the ConverterParameter, Collapsed otherwise.
    /// Usage: Visibility="{Binding Settings.IconStyle, Converter={StaticResource IconStyleVis}, ConverterParameter=Hamburger}"
    /// </summary>
    public class IconStyleToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string current && parameter is string expected)
                return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}