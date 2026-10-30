namespace Itch.Core;

public readonly record struct OhlcBar(
    ushort StockLocate,
    ulong WindowStartNanoseconds,
    uint Open,
    uint High,
    uint Low,
    uint Close,
    ulong Volume,
    ulong TradeCount);

public sealed class OhlcAggregator
{
    private readonly ulong _windowNanoseconds;
    private readonly Dictionary<ushort, BarState> _current = [];

    public OhlcAggregator(ulong windowNanoseconds)
    {
        if (windowNanoseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowNanoseconds), "Window duration must be positive.");
        }

        _windowNanoseconds = windowNanoseconds;
    }

    public IReadOnlyList<OhlcBar> AddTrade(ushort locate, ulong timestampNanoseconds, uint price, uint shares)
    {
        var windowStart = timestampNanoseconds / _windowNanoseconds * _windowNanoseconds;
        if (!_current.TryGetValue(locate, out var state))
        {
            _current.Add(locate, new BarState(windowStart, price, shares));
            return [];
        }

        if (windowStart < state.WindowStartNanoseconds)
        {
            throw new InvalidDataException(
                $"Out-of-order event for stock locate {locate}: window {windowStart} follows {state.WindowStartNanoseconds}.");
        }

        if (windowStart == state.WindowStartNanoseconds)
        {
            state.Add(price, shares);
            return [];
        }

        var completed = state.ToBar(locate);
        _current[locate] = new BarState(windowStart, price, shares);
        return [completed];
    }

    public IReadOnlyList<OhlcBar> Flush()
    {
        var result = _current
            .Select(pair => pair.Value.ToBar(pair.Key))
            .OrderBy(bar => bar.StockLocate)
            .ThenBy(bar => bar.WindowStartNanoseconds)
            .ToArray();
        _current.Clear();
        return result;
    }

    private sealed class BarState(ulong windowStart, uint price, uint shares)
    {
        public ulong WindowStartNanoseconds { get; } = windowStart;
        private uint Open { get; } = price;
        private uint High { get; set; } = price;
        private uint Low { get; set; } = price;
        private uint Close { get; set; } = price;
        private ulong Volume { get; set; } = shares;
        private ulong TradeCount { get; set; } = 1;

        public void Add(uint price, uint shares)
        {
            High = Math.Max(High, price);
            Low = Math.Min(Low, price);
            Close = price;
            Volume = checked(Volume + shares);
            TradeCount = checked(TradeCount + 1);
        }

        public OhlcBar ToBar(ushort locate) =>
            new(locate, WindowStartNanoseconds, Open, High, Low, Close, Volume, TradeCount);
    }
}
