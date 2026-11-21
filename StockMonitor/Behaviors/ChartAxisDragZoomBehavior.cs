using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Charts;

namespace StockMonitor.Behaviors;

/// <summary>
/// Attached behavior enabling horizontal drag-and-drop zooming directly on the X-axis title/label bar
/// of a <see cref="SfChart"/> with a live semi-transparent full-height selection rectangle overlay.
/// </summary>
public static class ChartAxisDragZoomBehavior
{
    private sealed class ChartDragSelectionAdorner : Adorner
    {
        private static readonly Brush FillBrush = new SolidColorBrush(Color.FromArgb(55, 56, 189, 248));
        private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromArgb(210, 56, 189, 248)), 1.5);

        private double _x1;
        private double _x2;

        static ChartDragSelectionAdorner()
        {
            FillBrush.Freeze();
            BorderPen.Freeze();
        }

        public ChartDragSelectionAdorner(UIElement adornedElement, double startX)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            _x1 = startX;
            _x2 = startX;
        }

        public void UpdateRange(double x1, double x2)
        {
            _x1 = x1;
            _x2 = x2;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (AdornedElement is not SfChart chart)
            {
                return;
            }

            double left = Math.Min(_x1, _x2);
            double width = Math.Abs(_x2 - _x1);
            double height = chart.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            Rect rect = new(left, 0, width, height);
            drawingContext.DrawRectangle(FillBrush, null, rect);
            drawingContext.DrawLine(BorderPen, new Point(left, 0), new Point(left, height));
            drawingContext.DrawLine(BorderPen, new Point(left + width, 0), new Point(left + width, height));
        }
    }

    private sealed class DragState
    {
        public bool IsDragging;
        public double StartX;
        public ChartDragSelectionAdorner? Adorner;
        public AdornerLayer? Layer;
    }

    private static readonly DependencyProperty DragStateProperty =
        DependencyProperty.RegisterAttached(
            "DragState",
            typeof(DragState),
            typeof(ChartAxisDragZoomBehavior),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <c>EnableXAxisDragZoom</c> attached dependency property.
    /// </summary>
    public static readonly DependencyProperty EnableXAxisDragZoomProperty =
        DependencyProperty.RegisterAttached(
            "EnableXAxisDragZoom",
            typeof(bool),
            typeof(ChartAxisDragZoomBehavior),
            new PropertyMetadata(false, OnEnableXAxisDragZoomChanged));

    /// <summary>
    /// Gets whether horizontal drag-zooming on the X-axis bar is enabled for the chart.
    /// </summary>
    /// <param name="obj">The target <see cref="SfChart"/>.</param>
    /// <returns><see langword="true"/> if enabled; otherwise <see langword="false"/>.</returns>
    public static bool GetEnableXAxisDragZoom(DependencyObject obj) => (bool)obj.GetValue(EnableXAxisDragZoomProperty);

    /// <summary>
    /// Sets whether horizontal drag-zooming on the X-axis bar is enabled for the chart.
    /// </summary>
    /// <param name="obj">The target <see cref="SfChart"/>.</param>
    /// <param name="value"><see langword="true"/> to enable X-axis bar drag-zooming.</param>
    public static void SetEnableXAxisDragZoom(DependencyObject obj, bool value) => obj.SetValue(EnableXAxisDragZoomProperty, value);

    private static void OnEnableXAxisDragZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfChart chart)
        {
            return;
        }

        chart.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        chart.PreviewMouseMove -= OnPreviewMouseMove;
        chart.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        chart.MouseLeave -= OnMouseLeave;

        if (e.NewValue is true)
        {
            chart.SetValue(DragStateProperty, new DragState());
            chart.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            chart.PreviewMouseMove += OnPreviewMouseMove;
            chart.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            chart.MouseLeave += OnMouseLeave;
        }
        else
        {
            chart.ClearValue(DragStateProperty);
        }
    }

    private static bool IsOverXAxisBar(SfChart chart, Point pos)
    {
        Rect clip = chart.SeriesClipRect;
        return clip.Width > 0
            && pos.Y >= clip.Bottom
            && pos.X >= clip.Left
            && pos.X <= clip.Right;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not SfChart chart || chart.GetValue(DragStateProperty) is not DragState state)
        {
            return;
        }

        Point pos = e.GetPosition(chart);
        if (!IsOverXAxisBar(chart, pos))
        {
            return;
        }

        state.IsDragging = true;
        state.StartX = pos.X;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(chart);
        if (layer != null)
        {
            state.Layer = layer;
            state.Adorner = new ChartDragSelectionAdorner(chart, pos.X);
            layer.Add(state.Adorner);
        }

        chart.CaptureMouse();
        chart.Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not SfChart chart || chart.GetValue(DragStateProperty) is not DragState state)
        {
            return;
        }

        Point pos = e.GetPosition(chart);
        if (state.IsDragging)
        {
            chart.Cursor = Cursors.SizeWE;
            Rect clip = chart.SeriesClipRect;
            if (clip.Width > 0 && state.Adorner != null)
            {
                double x1 = Math.Clamp(state.StartX, clip.Left, clip.Right);
                double x2 = Math.Clamp(pos.X, clip.Left, clip.Right);
                state.Adorner.UpdateRange(x1, x2);
            }
            return;
        }

        chart.Cursor = IsOverXAxisBar(chart, pos) ? Cursors.SizeWE : null;
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not SfChart chart || chart.GetValue(DragStateProperty) is not DragState state || !state.IsDragging)
        {
            return;
        }

        RemoveAdorner(state);
        state.IsDragging = false;
        if (chart.IsMouseCaptured)
        {
            chart.ReleaseMouseCapture();
        }

        chart.Cursor = null;
        Point endPos = e.GetPosition(chart);
        Rect clip = chart.SeriesClipRect;
        if (clip.Width <= 0 || chart.PrimaryAxis == null)
        {
            return;
        }

        double x1 = Math.Clamp(Math.Min(state.StartX, endPos.X), clip.Left, clip.Right);
        double x2 = Math.Clamp(Math.Max(state.StartX, endPos.X), clip.Left, clip.Right);
        if (x2 - x1 < 8.0)
        {
            return;
        }

        double relStart = (x1 - clip.Left) / clip.Width;
        double relEnd = (x2 - clip.Left) / clip.Width;

        double curPos = chart.PrimaryAxis.ZoomPosition;
        double curFactor = chart.PrimaryAxis.ZoomFactor <= 0 ? 1.0 : chart.PrimaryAxis.ZoomFactor;

        double newPos = Math.Clamp(curPos + relStart * curFactor, 0.0, 0.99);
        double newFactor = Math.Clamp((relEnd - relStart) * curFactor, 0.01, 1.0 - newPos);

        chart.PrimaryAxis.ZoomPosition = newPos;
        chart.PrimaryAxis.ZoomFactor = newFactor;
        e.Handled = true;
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is SfChart chart && chart.GetValue(DragStateProperty) is DragState state && !state.IsDragging)
        {
            chart.Cursor = null;
        }
    }

    private static void RemoveAdorner(DragState state)
    {
        if (state.Layer != null && state.Adorner != null)
        {
            state.Layer.Remove(state.Adorner);
        }

        state.Adorner = null;
        state.Layer = null;
    }
}
