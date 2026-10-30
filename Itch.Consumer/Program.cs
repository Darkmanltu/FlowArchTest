using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Itch.Core;

if (args.Length != 5 ||
    !ulong.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var windowSeconds) ||
    windowSeconds == 0)
{
    Console.Error.WriteLine(
        "Usage: Itch.Consumer <bootstrap-servers> <raw-topic> <output-topic> <group-id> <window-seconds>");
    return 2;
}

var windowNanoseconds = checked(windowSeconds * 1_000_000_000);
var consumerConfig = new ConsumerConfig
{
    BootstrapServers = args[0],
    GroupId = args[3],
    EnableAutoCommit = false,
    AutoOffsetReset = AutoOffsetReset.Earliest
};
var producerConfig = new ProducerConfig
{
    BootstrapServers = args[0],
    EnableIdempotence = true,
    Acks = Acks.All
};

using var consumer = new ConsumerBuilder<byte[], byte[]>(consumerConfig).Build();
using var producer = new ProducerBuilder<byte[], byte[]>(producerConfig).Build();
consumer.Subscribe(args[1]);

var processors = new Dictionary<TopicPartition, ItchProcessor>();
var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    while (!cancellation.IsCancellationRequested)
    {
        var record = consumer.Consume(cancellation.Token);
        var partition = record.TopicPartition;
        if (!processors.TryGetValue(partition, out var processor))
        {
            processor = new ItchProcessor(windowNanoseconds);
            processors.Add(partition, processor);
        }

        if (HasPartitionEndMarker(record.Message.Headers))
        {
            await PublishAsync(producer, args[2], processor.Flush());
            consumer.Commit(record);
            continue;
        }
        var parsed = ItchParser.Parse(record.Message.Value);
        await PublishAsync(producer, args[2], processor.Process(parsed));
        await PublishAsync(producer, args[2], processor.Process(parsed));
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
}
finally
{
    consumer.Close();
    producer.Flush(TimeSpan.FromSeconds(30));
}

return 0;

static bool HasPartitionEndMarker(Headers? headers)
{
    if (headers is null)
    {
        return false;
    }

    foreach (var header in headers)
    {
        if (header.Key == "itch-control" &&
            Encoding.ASCII.GetString(header.GetValueBytes()) == "partition-end")
        {
            return true;
        }
    }

    return false;
}

static async Task PublishAsync(IProducer<byte[], byte[]> producer, string topic, IReadOnlyList<OhlcBar> bars)
{
    foreach (var bar in bars)
    {
        var key = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{bar.StockLocate}:{bar.WindowStartNanoseconds}"));
        var value = JsonSerializer.SerializeToUtf8Bytes(bar);
        await producer.ProduceAsync(topic, new Message<byte[], byte[]> { Key = key, Value = value });
    }
}
