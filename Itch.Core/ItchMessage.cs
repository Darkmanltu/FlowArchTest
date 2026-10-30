namespace Itch.Core;

public readonly record struct ItchMessage(
    byte Type,
    ushort StockLocate,
    ulong TimestampNanoseconds,
    ulong OrderReference = 0,
    byte Side = 0,
    uint Shares = 0,
    uint Price = 0,
    string? StockSymbol = null,
    bool Printable = true);
