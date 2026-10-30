namespace Itch.Core;

public sealed class ItchProcessor
{
    private readonly OhlcAggregator _aggregator;
    private readonly Dictionary<ulong, OrderState> _orders = [];

    public ItchProcessor(ulong windowNanoseconds)
    {
        _aggregator = new OhlcAggregator(windowNanoseconds);
    }

    public int TrackedOrderCount => _orders.Count;

    public IReadOnlyList<OhlcBar> Process(ItchMessage message)
    {
        switch (message.Type)
        {
            case (byte)'A':
            case (byte)'F':
                if (!_orders.TryAdd(
                    message.OrderReference,
                    new OrderState(message.StockLocate, message.Price, message.Shares)))
                {
                    throw new InvalidDataException($"Duplicate live order reference {message.OrderReference}.");
                }

                return [];
            case (byte)'E':
                return Execute(message.OrderReference, message.Shares, message.TimestampNanoseconds, null, printable: true);
            case (byte)'C':
                return Execute(
                    message.OrderReference,
                    message.Shares,
                    message.TimestampNanoseconds,
                    message.Price,
                    message.Printable);
            case (byte)'X':
                AdjustOrder(message.OrderReference, message.Shares);
                return [];
            case (byte)'D':
                RemoveOrder(message.OrderReference);
                return [];
            case (byte)'P':
            case (byte)'Q':
                return _aggregator.AddTrade(
                    message.StockLocate,
                    message.TimestampNanoseconds,
                    message.Price,
                    message.Shares);
            default:
                return [];
        }
    }

    public IReadOnlyList<OhlcBar> Flush() => _aggregator.Flush();

    private IReadOnlyList<OhlcBar> Execute(
        ulong orderReference,
        uint shares,
        ulong timestampNanoseconds,
        uint? executionPrice,
        bool printable)
    {
        if (!_orders.TryGetValue(orderReference, out var order))
        {
            throw new InvalidDataException($"Execution references unknown order {orderReference}.");
        }

        if (shares > order.RemainingShares)
        {
            throw new InvalidDataException($"Execution exceeds remaining quantity for order {orderReference}.");
        }

        order.RemainingShares -= shares;
        if (order.RemainingShares == 0)
        {
            _orders.Remove(orderReference);
        }

        return printable
            ? _aggregator.AddTrade(order.StockLocate, timestampNanoseconds, executionPrice ?? order.Price, shares)
            : [];
    }

    private void AdjustOrder(ulong orderReference, uint shares)
    {
        if (!_orders.TryGetValue(orderReference, out var order) || shares > order.RemainingShares)
        {
            throw new InvalidDataException($"Cancel references an unknown order or exceeds its remaining quantity: {orderReference}.");
        }

        order.RemainingShares -= shares;
        if (order.RemainingShares == 0)
        {
            _orders.Remove(orderReference);
        }
    }

    private void RemoveOrder(ulong orderReference)
    {
        if (!_orders.Remove(orderReference))
        {
            throw new InvalidDataException($"Delete references unknown order {orderReference}.");
        }
    }

    private sealed class OrderState(ushort stockLocate, uint price, uint remainingShares)
    {
        public ushort StockLocate { get; } = stockLocate;
        public uint Price { get; } = price;
        public uint RemainingShares { get; set; } = remainingShares;
    }
}
