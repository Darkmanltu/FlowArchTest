using System.Globalization;
using Itch.Core;

if (args.Length != 3 ||
    !ulong.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var windowSeconds) ||
    windowSeconds == 0)
{
    Console.Error.WriteLine("Usage: Itch.Monolith <itch-file> <output.csv> <window-seconds>");
    return 2;
}

var windowNanoseconds = checked(windowSeconds * 1_000_000_000);
var processor = new ItchProcessor(windowNanoseconds);
var bars = new List<OhlcBar>();

await using (var input = File.OpenRead(args[0]))
{
    while (await ReadMessageAsync(input) is { } messageBytes)
    {
        bars.AddRange(processor.Process(ItchParser.Parse(messageBytes)));
    }
}

bars.AddRange(processor.Flush());
bars.Sort(static (left, right) =>
{
    var locateComparison = left.StockLocate.CompareTo(right.StockLocate);
    return locateComparison != 0
        ? locateComparison
        : left.WindowStartNanoseconds.CompareTo(right.WindowStartNanoseconds);
});

await using var output = new StreamWriter(args[1], append: false, new System.Text.UTF8Encoding(false));
await output.WriteLineAsync(CsvOutput.Header);
foreach (var bar in bars)
{
    await output.WriteLineAsync(CsvOutput.Format(bar));
}

return 0;

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

internal static class CsvOutput
{
    public const string Header = "stock_locate,window_start_ns,open,high,low,close,volume,trade_count";

    public static string Format(OhlcBar bar) => string.Join(',',
        bar.StockLocate.ToString(CultureInfo.InvariantCulture),
        bar.WindowStartNanoseconds.ToString(CultureInfo.InvariantCulture),
        bar.Open.ToString(CultureInfo.InvariantCulture),
        bar.High.ToString(CultureInfo.InvariantCulture),
        bar.Low.ToString(CultureInfo.InvariantCulture),
        bar.Close.ToString(CultureInfo.InvariantCulture),
        bar.Volume.ToString(CultureInfo.InvariantCulture),
        bar.TradeCount.ToString(CultureInfo.InvariantCulture));
}
