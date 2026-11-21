using System.Windows;
using System.Windows.Media;
using StockMonitor.Models;

namespace StockMonitor.Behaviors;

/// <summary>
/// Lightweight, high-performance column sparkline control that strictly colors positive values green
/// and negative values red around a zero baseline.
/// </summary>
public sealed class ProfitSparklineControl : FrameworkElement
{
    private static readonly Brush PositiveBrush = CreateFrozenBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
    private static readonly Brush NegativeBrush = CreateFrozenBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red
    private static readonly Pen ZeroAxisPen = CreateFrozenPen(Color.FromArgb(0x66, 0x94, 0xA3, 0xB8), 1.0);

    /// <summary>
    /// Identifies the <see cref="ItemsSource"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<SparklinePoint>),
            typeof(ProfitSparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Gets or sets the sequence of <see cref="SparklinePoint"/> values to render.
    /// </summary>
    public IEnumerable<SparklinePoint>? ItemsSource
    {
        get => (IEnumerable<SparklinePoint>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 2 || height <= 2 || ItemsSource == null)
        {
            return;
        }

        List<SparklinePoint> points = ItemsSource as List<SparklinePoint> ?? ItemsSource.ToList();
        int count = points.Count;
        if (count == 0)
        {
            return;
        }

        double maxVal = 0.0;
        double minVal = 0.0;
        foreach (SparklinePoint pt in points)
        {
            if (pt.ProfitBase > maxVal)
            {
                maxVal = pt.ProfitBase;
            }

            if (pt.ProfitBase < minVal)
            {
                minVal = pt.ProfitBase;
            }
        }

        double range = maxVal - minVal;
        const double padY = 2.0;
        double plotHeight = Math.Max(1.0, height - 2.0 * padY);

        // Compute Y coordinate of the zero baseline
        double zeroY = range > 1e-9
            ? padY + (maxVal / range) * plotHeight
            : height / 2.0;

        // Draw subtle zero baseline
        double snappedZeroY = Math.Round(zeroY) + 0.5;
        dc.DrawLine(ZeroAxisPen, new Point(0, snappedZeroY), new Point(width, snappedZeroY));

        const double gap = 2.0;
        double totalGap = gap * Math.Max(0, count - 1);
        double barWidth = Math.Max(2.0, (width - totalGap) / count);

        for (int i = 0; i < count; i++)
        {
            double val = points[i].ProfitBase;
            double x = i * (barWidth + gap);
            if (x >= width)
            {
                break;
            }

            double clampedBarWidth = Math.Min(barWidth, width - x);
            double barHeight = range > 1e-9
                ? Math.Abs(val) / range * plotHeight
                : 0.0;

            if (Math.Abs(val) > 1e-6 && barHeight < 2.0)
            {
                barHeight = 2.0;
            }

            if (barHeight <= 0.0)
            {
                continue;
            }

            if (val >= 0.0)
            {
                double top = Math.Max(padY, zeroY - barHeight);
                double h = Math.Max(1.0, zeroY - top);
                dc.DrawRectangle(PositiveBrush, null, new Rect(x, top, clampedBarWidth, h));
            }
            else
            {
                double bottom = Math.Min(height - padY, zeroY + barHeight);
                double h = Math.Max(1.0, bottom - zeroY);
                dc.DrawRectangle(NegativeBrush, null, new Rect(x, zeroY, clampedBarWidth, h));
            }
        }
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        Pen pen = new(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
