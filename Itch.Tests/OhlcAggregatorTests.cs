using Itch.Core;
using Xunit;

namespace Itch.Tests;

public sealed class OhlcAggregatorTests
{
    [Fact]
    public void EmitsClosedWindowAndFlushesFinalWindow()
    {
        var aggregator = new OhlcAggregator(1_000);

        Assert.Empty(aggregator.AddTrade(4, 100, 100, 2));
        Assert.Empty(aggregator.AddTrade(4, 900, 110, 3));
        var closed = Assert.Single(aggregator.AddTrade(4, 1_000, 90, 5));
        var final = Assert.Single(aggregator.Flush());

        Assert.Equal(new OhlcBar(4, 0, 100, 110, 100, 110, 5, 2), closed);
        Assert.Equal(new OhlcBar(4, 1_000, 90, 90, 90, 90, 5, 1), final);
    }

    [Fact]
    public void RejectsOutOfOrderWindowForSameLocate()
    {
        var aggregator = new OhlcAggregator(1_000);
        aggregator.AddTrade(4, 1_000, 100, 1);

        Assert.Throws<InvalidDataException>(() => aggregator.AddTrade(4, 999, 100, 1));
    }
}
