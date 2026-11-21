using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StockMonitor.Services;
using Syncfusion.UI.Xaml.Grid;

namespace StockMonitor.Behaviors;

/// <summary>
/// Attached behavior helpers for <see cref="SfDataGrid"/> columns to provide localized header tooltips.
/// </summary>
public static class GridColumnHelp
{
    /// <summary>
    /// Identifies the <c>ToolTipKey</c> attached dependency property for <see cref="GridColumnBase"/>.
    /// </summary>
    public static readonly DependencyProperty ToolTipKeyProperty =
        DependencyProperty.RegisterAttached(
            "ToolTipKey",
            typeof(string),
            typeof(GridColumnHelp),
            new PropertyMetadata(null, OnToolTipKeyChanged));

    /// <summary>
    /// Gets the localization resource key used for the column's header tooltip.
    /// </summary>
    /// <param name="obj">The target grid column.</param>
    /// <returns>The localization key, or <see langword="null"/> if not set.</returns>
    public static string? GetToolTipKey(DependencyObject obj) => (string?)obj.GetValue(ToolTipKeyProperty);

    /// <summary>
    /// Sets the localization resource key used for the column's header tooltip.
    /// </summary>
    /// <param name="obj">The target grid column.</param>
    /// <param name="value">The localization key defined in <see cref="LocalizationService"/>.</param>
    public static void SetToolTipKey(DependencyObject obj, string? value) => obj.SetValue(ToolTipKeyProperty, value);

    private static void OnToolTipKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GridColumnBase column || e.NewValue is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        column.ShowHeaderToolTip = true;
        column.HeaderToolTipTemplate = BuildToolTipTemplate(key);
    }

    private static DataTemplate BuildToolTipTemplate(string key)
    {
        FrameworkElementFactory tb = new(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        });
        tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        tb.SetValue(FrameworkElement.MaxWidthProperty, 320.0);

        DataTemplate template = new() { VisualTree = tb };
        template.Seal();
        return template;
    }
}
