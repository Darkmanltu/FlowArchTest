using System.Globalization;
using System.Text.Json;
using Itch.Core;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Itch.Verify <baseline.csv> <candidate.csv-or-jsonl>");
    return 2;
}

var baseline = ReadRows(args[0]);
var candidate = ReadRows(args[1]);
if (!baseline.SequenceEqual(candidate))
{
    var mismatch = Enumerable.Range(0, Math.Max(baseline.Count, candidate.Count))
        .First(index => index >= baseline.Count || index >= candidate.Count || baseline[index] != candidate[index]);
    Console.Error.WriteLine(
        $"Outputs differ at sorted row {mismatch + 1}. Baseline: " +
        $"{(mismatch < baseline.Count ? baseline[mismatch] : "<missing>")}; candidate: " +
        $"{(mismatch < candidate.Count ? candidate[mismatch] : "<missing>")}");
    return 1;
}

Console.WriteLine($"Outputs match: {baseline.Count} (stock locate, window) rows.");
return 0;

static List<string> ReadRows(string path)
{
    using var reader = new StreamReader(path);
    var firstLine = reader.ReadLine();
    if (firstLine is null)
    {
        throw new InvalidDataException($"Input '{path}' is empty.");
    }

    var rows = new List<string>();
    var isJsonLines = firstLine != "stock_locate,window_start_ns,open,high,low,close,volume,trade_count";
    if (isJsonLines)
    {
        var latestByKey = new Dictionary<(ushort Locate, ulong Window), OhlcBar>();
        AddJson(firstLine);
        while (reader.ReadLine() is { } jsonLine)
        {
            AddJson(jsonLine);
        }

        rows.AddRange(latestByKey.Values.Select(Format));

        void AddJson(string jsonLine)
        {
            var bar = ParseJson(jsonLine, path);
            latestByKey[(bar.StockLocate, bar.WindowStartNanoseconds)] = bar;
        }
    }
    else
    {
        while (reader.ReadLine() is { } line)
        {
            rows.Add(line);
        }
    }

    var keys = new HashSet<(ushort Locate, ulong Window)>();
    foreach (var line in rows)
    {
        var columns = line.Split(',');
        if (columns.Length != 8 ||
            !ushort.TryParse(columns[0], NumberStyles.None, CultureInfo.InvariantCulture, out var locate) ||
            !ulong.TryParse(columns[1], NumberStyles.None, CultureInfo.InvariantCulture, out var window) ||
            !uint.TryParse(columns[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !uint.TryParse(columns[3], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !uint.TryParse(columns[4], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !uint.TryParse(columns[5], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !ulong.TryParse(columns[6], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !ulong.TryParse(columns[7], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException($"Invalid OHLC CSV row in '{path}': {line}");
        }

        if (!keys.Add((locate, window)) && !isJsonLines)
        {
            throw new InvalidDataException($"Duplicate locate/window key in '{path}': {locate}/{window}.");
        }
    }

    return rows
        .OrderBy(row => ushort.Parse(row.Split(',')[0], CultureInfo.InvariantCulture))
        .ThenBy(row => ulong.Parse(row.Split(',')[1], CultureInfo.InvariantCulture))
        .ToList();
}

static OhlcBar ParseJson(string line, string path)
{
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var properties = new[]
        {
            nameof(OhlcBar.StockLocate),
            nameof(OhlcBar.WindowStartNanoseconds),
            nameof(OhlcBar.Open),
            nameof(OhlcBar.High),
            nameof(OhlcBar.Low),
            nameof(OhlcBar.Close),
            nameof(OhlcBar.Volume),
            nameof(OhlcBar.TradeCount)
        };
        if (root.ValueKind != JsonValueKind.Object ||
            properties.Any(property => !root.TryGetProperty(property, out _)))
        {
            throw new InvalidDataException($"JSON row in '{path}' is not an OHLC record: {line}");
        }

        return JsonSerializer.Deserialize<OhlcBar>(line);
    }
    catch (JsonException exception)
    {
        throw new InvalidDataException($"Invalid OHLC JSON row in '{path}': {line}", exception);
    }
}

static string Format(OhlcBar bar) => string.Join(',',
    bar.StockLocate.ToString(CultureInfo.InvariantCulture),
    bar.WindowStartNanoseconds.ToString(CultureInfo.InvariantCulture),
    bar.Open.ToString(CultureInfo.InvariantCulture),
    bar.High.ToString(CultureInfo.InvariantCulture),
    bar.Low.ToString(CultureInfo.InvariantCulture),
    bar.Close.ToString(CultureInfo.InvariantCulture),
    bar.Volume.ToString(CultureInfo.InvariantCulture),
    bar.TradeCount.ToString(CultureInfo.InvariantCulture));
