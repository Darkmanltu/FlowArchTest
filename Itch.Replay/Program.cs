using System.Buffers.Binary;
using System.Text;
using Confluent.Kafka;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Itch.Replay <itch-file> <bootstrap-servers> <raw-topic>");
    return 2;
}

var config = new ProducerConfig
{
    BootstrapServers = args[1],
    EnableIdempotence = true,
    Acks = Acks.All
};

using var producer = new ProducerBuilder<byte[], byte[]>(config).Build();
var metadata = producer.GetMetadata(args[2], TimeSpan.FromSeconds(15));
var topicMetadata = metadata.Topics.SingleOrDefault(topic => topic.Topic == args[2]);
if (topicMetadata is null || topicMetadata.Error.Code != ErrorCode.NoError)
{
    throw new InvalidOperationException($"Could not read partition metadata for topic '{args[2]}'.");
}

await using (var input = File.OpenRead(args[0]))
{
    while (await ReadMessageAsync(input) is { } payload)
    {
        var key = payload[0] is (byte)'S' or (byte)'W' or (byte)'B'
            ? null
            : GetLocateKey(payload);
        var headers = new Headers
        {
            { "producer-timestamp-utc-ms", Int64Bytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) }
        };
        await producer.ProduceAsync(args[2], new Message<byte[], byte[]>
        {
            Key = key,
            Value = payload,
            Headers = headers
        });
    }
}

foreach (var partition in topicMetadata.Partitions)
{
    var headers = new Headers { { "itch-control", Encoding.ASCII.GetBytes("partition-end") } };
    await producer.ProduceAsync(
        new TopicPartition(args[2], partition.PartitionId),
        new Message<byte[], byte[]>
        {
            Value = [],
            Headers = headers
        });
}

producer.Flush(TimeSpan.FromSeconds(30));
return 0;

static byte[] GetLocateKey(ReadOnlySpan<byte> payload)
{
    if (payload.Length < 3)
    {
        throw new InvalidDataException("ITCH message is too short to contain Stock Locate.");
    }

    var key = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(key, BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2)));
    return key;
}

static byte[] Int64Bytes(long value)
{
    var bytes = new byte[sizeof(long)];
    BinaryPrimitives.WriteInt64BigEndian(bytes, value);
    return bytes;
}

static async Task<byte[]?> ReadMessageAsync(Stream stream)
{
    var high = stream.ReadByte();
    if (high < 0)
    {
        return null;
    }

    var low = stream.ReadByte();
    if (low < 0)
    {
        throw new EndOfStreamException("ITCH file ends inside a two-byte message length.");
    }

    var length = (high << 8) | low;
    if (length < 3)
    {
        throw new InvalidDataException($"Invalid ITCH message length: {length}.");
    }

    var payload = new byte[length];
    await stream.ReadExactlyAsync(payload);
    return payload;
}
