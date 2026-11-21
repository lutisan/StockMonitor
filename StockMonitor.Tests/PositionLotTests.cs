using StockMonitor.Models;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="PositionLot"/> verifying the 3-tier price editing/clearing contract,
/// manual lock flags, broker snapshot invalidation, and derived status/profitability flags.
/// </summary>
public sealed class PositionLotTests
{
    /// <summary>
    /// Verifies that entering a positive value into <see cref="PositionLot.EditableOpenPrice"/>
    /// locks <see cref="PositionLot.IsOpenPriceManual"/> and clears cached XTB broker purchase values
    /// so subsequent calculations use the user's manual open price.
    /// </summary>
    [Fact]
    public void EditableOpenPrice_WhenSetToPositiveValue_LocksManualFlagAndClearsBrokerPurchaseSnapshot()
    {
        // Arrange: lot imported from XTB with broker purchase snapshot
        PositionLot lot = new()
        {
            OpenPrice = 150m,
            IsOpenPriceManual = false,
            BrokerPurchaseValueEur = 1400m,
            OpenConversionRate = 0.93m
        };

        // Act: user manually overrides the open price
        lot.EditableOpenPrice = 162.50m;

        // Assert: manual lock is enabled and stale broker purchase overrides are cleared
        Assert.Equal(162.50m, lot.OpenPrice);
        Assert.Equal(162.50m, lot.EditableOpenPrice);
        Assert.True(lot.IsOpenPriceManual);
        Assert.Null(lot.BrokerPurchaseValueEur);
        Assert.Null(lot.OpenConversionRate);
    }

    /// <summary>
    /// Verifies that clearing <see cref="PositionLot.EditableOpenPrice"/> (setting to null)
    /// resets <see cref="PositionLot.OpenPrice"/> to zero, unlocks <see cref="PositionLot.IsOpenPriceManual"/>,
    /// and clears XTB broker purchase values so Yahoo Finance historical price will be fetched.
    /// </summary>
    [Fact]
    public void EditableOpenPrice_WhenClearedToNull_UnlocksManualFlagAndResetsForOnlineFetch()
    {
        // Arrange: lot with manually locked open price
        PositionLot lot = new()
        {
            OpenPrice = 180m,
            IsOpenPriceManual = true,
            BrokerPurchaseValueEur = 1700m,
            OpenConversionRate = 0.94m
        };

        // Act: user clears the text box
        lot.EditableOpenPrice = null;

        // Assert: price and lock are cleared so next save/sync fetches from Yahoo
        Assert.Equal(0m, lot.OpenPrice);
        Assert.Null(lot.EditableOpenPrice);
        Assert.False(lot.IsOpenPriceManual);
        Assert.Null(lot.BrokerPurchaseValueEur);
        Assert.Null(lot.OpenConversionRate);
    }

    /// <summary>
    /// Verifies that setting <see cref="PositionLot.EditableCloseOrCurrentPrice"/> on a closed lot
    /// updates both <see cref="PositionLot.ClosePrice"/> and <see cref="PositionLot.CurrentMarketPrice"/>,
    /// locks <see cref="PositionLot.IsClosePriceManual"/>, and clears XTB sale snapshot fields.
    /// </summary>
    [Fact]
    public void EditableCloseOrCurrentPrice_OnClosedLot_LocksManualClosePrice()
    {
        // Arrange: closed lot with XTB sale values
        PositionLot lot = new()
        {
            OpenDate = new DateTime(2025, 1, 10),
            CloseDate = new DateTime(2025, 6, 15),
            ClosePrice = 200m,
            CurrentMarketPrice = 200m,
            IsClosePriceManual = false,
            BrokerSaleOrCurrentValueEur = 1850m,
            CloseConversionRate = 0.925m
        };

        // Act: user manually overrides the close price
        lot.EditableCloseOrCurrentPrice = 215m;

        // Assert: both close and current prices reflect the override, locked as manual
        Assert.Equal(215m, lot.ClosePrice);
        Assert.Equal(215m, lot.CurrentMarketPrice);
        Assert.Equal(215m, lot.EditableCloseOrCurrentPrice);
        Assert.True(lot.IsClosePriceManual);
        Assert.Null(lot.BrokerSaleOrCurrentValueEur);
        Assert.Null(lot.CloseConversionRate);
    }

    /// <summary>
    /// Verifies that clearing <see cref="PositionLot.EditableCloseOrCurrentPrice"/> unlocks the close/current price
    /// and resets both <see cref="PositionLot.ClosePrice"/> and <see cref="PositionLot.CurrentMarketPrice"/>
    /// so online sync will recalculate the price automatically.
    /// </summary>
    [Fact]
    public void EditableCloseOrCurrentPrice_WhenClearedToNull_UnlocksAndClearsPrices()
    {
        // Arrange: open lot with manual current price
        PositionLot lot = new()
        {
            CurrentMarketPrice = 240m,
            IsClosePriceManual = true,
            BrokerSaleOrCurrentValueEur = 2200m
        };

        // Act: user clears the current price
        lot.EditableCloseOrCurrentPrice = null;

        // Assert: unlocked and reset
        Assert.Null(lot.ClosePrice);
        Assert.Equal(0m, lot.CurrentMarketPrice);
        Assert.Null(lot.EditableCloseOrCurrentPrice);
        Assert.False(lot.IsClosePriceManual);
        Assert.Null(lot.BrokerSaleOrCurrentValueEur);
    }

    /// <summary>
    /// Verifies that <see cref="PositionLot.Status"/>, <see cref="PositionLot.IsClosed"/>,
    /// <see cref="PositionLot.IsInProfit"/>, and <see cref="PositionLot.IsInLoss"/> react accurately
    /// to changes in <see cref="PositionLot.CloseDate"/> and <see cref="PositionLot.TotalProfitBase"/>.
    /// </summary>
    [Fact]
    public void DerivedStatusAndProfitFlags_ReflectCloseDateAndTotalProfitBase()
    {
        PositionLot lot = new();

        // Initially open and flat
        Assert.Equal(PositionStatus.Open, lot.Status);
        Assert.Equal("Open", lot.StatusText);
        Assert.False(lot.IsClosed);
        Assert.False(lot.IsInProfit);
        Assert.False(lot.IsInLoss);

        // Positive profit marks IsInProfit
        lot.TotalProfitBase = 125.50m;
        Assert.True(lot.IsInProfit);
        Assert.False(lot.IsInLoss);

        // Negative profit marks IsInLoss
        lot.TotalProfitBase = -42.10m;
        Assert.False(lot.IsInProfit);
        Assert.True(lot.IsInLoss);

        // Setting CloseDate marks Closed
        lot.CloseDate = new DateTime(2025, 11, 1);
        Assert.Equal(PositionStatus.Closed, lot.Status);
        Assert.Equal("Closed", lot.StatusText);
        Assert.True(lot.IsClosed);
    }
}
