using Itch.Core;
using Xunit;

namespace Itch.Tests;

public sealed class ItchProcessorTests
{
    [Fact]
    public void UsesRestingOrderPriceForExecutionAndTracksRemainingShares()
    {
        var processor = new ItchProcessor(1_000);
        processor.Process(new ItchMessage(
            (byte)'A',
            12,
            100,
            OrderReference: 9001,
            Shares: 10,
            Price: 250_000));

        Assert.Empty(processor.Process(new ItchMessage(
            (byte)'E',
            12,
            200,
            OrderReference: 9001,
            Shares: 4)));
        Assert.Equal(1, processor.TrackedOrderCount);

        var bar = Assert.Single(processor.Flush());

        Assert.Equal((ushort)12, bar.StockLocate);
        Assert.Equal(250_000U, bar.Open);
        Assert.Equal(4UL, bar.Volume);
        Assert.Equal(1UL, bar.TradeCount);
    }
}
