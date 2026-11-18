using System.Windows.Controls;

namespace StockMonitor.Views;

/// <summary>
/// Tab 2 view displaying KPI summary tiles, allocation doughnut charts, and the per-lot financial summary grid.
/// </summary>
public partial class SummaryView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SummaryView"/> class.
    /// </summary>
    public SummaryView()
    {
        InitializeComponent();
    }
}
