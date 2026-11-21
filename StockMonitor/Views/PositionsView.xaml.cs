using System.Windows.Controls;

namespace StockMonitor.Views;

/// <summary>
/// Tab 1 view hosting the stock price chart, selected lot details sidebar, full-width Yahoo Finance page, and grouped positions grid.
/// </summary>
public partial class PositionsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PositionsView"/> class.
    /// </summary>
    public PositionsView()
    {
        InitializeComponent();
    }
}
