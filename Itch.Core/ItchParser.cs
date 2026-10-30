using System.Buffers.Binary;
using System.Text;

namespace Itch.Core;

public static class ItchParser
{
    public static ItchMessage Parse(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty)
        {
            throw new InvalidDataException("ITCH message cannot be empty.");
        }

        var type = message[0];
        if (type is (byte)'S' or (byte)'W')
        {
            RequireLength(message, 8);
            return new ItchMessage(type, 0, ReadUInt48(message.Slice(1, 6)));
        }

        if (type == (byte)'B')
        {
            RequireLength(message, 9);
            return new ItchMessage(type, 0, 0);
        }

        if (message.Length < 11)
        {
            throw new InvalidDataException($"ITCH message type {(char)type} is shorter than its common header.");
        }

        var locate = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(1, 2));
        var timestamp = ReadUInt48(message.Slice(5, 6));

        return type switch
        {
            (byte)'A' => ParseAdd(message, type, locate, timestamp, hasAttribution: false),
            (byte)'F' => ParseAdd(message, type, locate, timestamp, hasAttribution: true),
            (byte)'P' => ParseTrade(message, type, locate, timestamp),
            (byte)'Q' => ParseCrossTrade(message, type, locate, timestamp),
            (byte)'E' => ParseExecution(message, type, locate, timestamp),
            (byte)'C' => ParseExecutionWithPrice(message, type, locate, timestamp),
            (byte)'X' => ParseCancel(message, type, locate, timestamp),
            (byte)'D' => ParseDelete(message, type, locate, timestamp),
            (byte)'R' => ParseStockDirectory(message, type, locate, timestamp),
            _ => new ItchMessage(type, locate, timestamp)
        };
    }

    private static ItchMessage ParseAdd(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp, bool hasAttribution)
    {
        RequireLength(message, hasAttribution ? 40 : 36);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            BinaryPrimitives.ReadUInt64BigEndian(message.Slice(11, 8)),
            message[19],
            BinaryPrimitives.ReadUInt32BigEndian(message.Slice(20, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(message.Slice(32, 4)));
    }

    private static ItchMessage ParseTrade(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 44);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            Shares: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(20, 4)),
            Price: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(32, 4)));
    }

    private static ItchMessage ParseCrossTrade(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 39);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            Shares: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(11, 4)),
            Price: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(23, 4)));
    }

    private static ItchMessage ParseExecution(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 31);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            BinaryPrimitives.ReadUInt64BigEndian(message.Slice(11, 8)),
            Shares: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(19, 4)));
    }

    private static ItchMessage ParseExecutionWithPrice(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 36);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            BinaryPrimitives.ReadUInt64BigEndian(message.Slice(11, 8)),
            Shares: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(19, 4)),
            Price: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(24, 4)),
            Printable: message[23] == (byte)'Y');
    }

    private static ItchMessage ParseCancel(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 23);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            BinaryPrimitives.ReadUInt64BigEndian(message.Slice(11, 8)),
            Shares: BinaryPrimitives.ReadUInt32BigEndian(message.Slice(19, 4)));
    }

    private static ItchMessage ParseDelete(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 19);
        return new ItchMessage(
            type,
            locate,
            timestamp,
            BinaryPrimitives.ReadUInt64BigEndian(message.Slice(11, 8)));
    }

    private static ItchMessage ParseStockDirectory(ReadOnlySpan<byte> message, byte type, ushort locate, ulong timestamp)
    {
        RequireLength(message, 39);
        var symbol = Encoding.ASCII.GetString(message.Slice(11, 8)).TrimEnd();
        return new ItchMessage(type, locate, timestamp, StockSymbol: symbol);
    }

    private static ulong ReadUInt48(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        foreach (var part in bytes)
        {
            value = (value << 8) | part;
        }

        return value;
    }

    private static void RequireLength(ReadOnlySpan<byte> message, int expected)
    {
        if (message.Length != expected)
        {
            throw new InvalidDataException(
                $"ITCH message type {(char)message[0]} has length {message.Length}; expected {expected}.");
        }
    }
}
