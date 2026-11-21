using System.Globalization;
using System.Windows.Data;

namespace StockMonitor.Behaviors;

/// <summary>
/// Formats numeric values using a space (<c>' '</c>) as the thousands separator (e.g., <c>12 345.67</c>).
/// </summary>
public sealed class SpaceNumberConverter : IValueConverter
{
    internal static readonly NumberFormatInfo SpaceNumberFormat = CreateSpaceNumberFormat();

    private static NumberFormatInfo CreateSpaceNumberFormat()
    {
        NumberFormatInfo nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        nfi.NumberGroupSeparator = " ";
        return nfi;
    }

    /// <summary>
    /// Formats a numeric value using space as the thousands separator.
    /// </summary>
    public static string FormatNumber(double value, string format = "N2")
    {
        return value.ToString(format, SpaceNumberFormat);
    }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string format = parameter is string fmt && !string.IsNullOrWhiteSpace(fmt) ? fmt : "N2";
        return value switch
        {
            double d => d.ToString(format, SpaceNumberFormat),
            decimal m => m.ToString(format, SpaceNumberFormat),
            float f => f.ToString(format, SpaceNumberFormat),
            int i => i.ToString(format, SpaceNumberFormat),
            long l => l.ToString(format, SpaceNumberFormat),
            IFormattable formattable => formattable.ToString(format, SpaceNumberFormat),
            _ => value?.ToString() ?? string.Empty
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
