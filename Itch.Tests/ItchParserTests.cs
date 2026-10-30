using System.Buffers.Binary;
using Itch.Core;
using Xunit;

namespace Itch.Tests;

public sealed class ItchParserTests
{
    [Fact]
    public void ParsesTradeMessageBigEndianFields()
    {
        var payload = new byte[44];
        payload[0] = (byte)'P';
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), 0x1234);
        WriteUInt48(payload.AsSpan(5, 6), 123_456_789);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(20, 4), 250);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(32, 4), 1_234_500);

        var message = ItchParser.Parse(payload);

        Assert.Equal((byte)'P', message.Type);
        Assert.Equal((ushort)0x1234, message.StockLocate);
        Assert.Equal(123_456_789UL, message.TimestampNanoseconds);
        Assert.Equal(250U, message.Shares);
        Assert.Equal(1_234_500U, message.Price);
    }

    [Fact]
    public void RejectsWrongKnownMessageLength()
    {
        var payload = new byte[43];
        payload[0] = (byte)'P';

        var exception = Assert.Throws<InvalidDataException>(() => ItchParser.Parse(payload));

        Assert.Contains("expected 44", exception.Message);
    }

    [Fact]
    public void ParsesStockDirectorySymbol()
    {
        var payload = new byte[39];
        payload[0] = (byte)'R';
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), 7);
        "MSFT    "u8.CopyTo(payload.AsSpan(11, 8));

        var message = ItchParser.Parse(payload);

        Assert.Equal("MSFT", message.StockSymbol);
    }

    [Fact]
    public void ParsesSystemEventWithoutStockLocate()
    {
        var payload = new byte[8];
        payload[0] = (byte)'S';
        WriteUInt48(payload.AsSpan(1, 6), 123);

        var message = ItchParser.Parse(payload);

        Assert.Equal((byte)'S', message.Type);
        Assert.Equal((ushort)0, message.StockLocate);
        Assert.Equal(123UL, message.TimestampNanoseconds);
    }

    private static void WriteUInt48(Span<byte> destination, ulong value)
    {
        for (var index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }
}
