# ITCH replay and OHLC prototype

The solution is split into the requested projects:

- `Itch.Core` — span-based ITCH parser, order state, and event-time OHLC aggregation. It has no Kafka or file I/O.
- `Itch.Monolith` — the single-process baseline: ITCH file → parser/processor → CSV.
- `Itch.Replay` — replays each framed ITCH message to Kafka topic `itch.raw`.
- `Itch.Consumer` — Kafka consumer group that uses the same Core processor and publishes JSON OHLC records to `itch.ohlc`.
- `Itch.Verify` — compares a baseline CSV against another CSV or a JSON-lines export of `itch.ohlc`.
- `Itch.Tests` — parser and window-aggregation tests.

## Assumptions in this first cut

- The input is an ITCH 5.0 binary stream framed as a two-byte, big-endian payload length followed by that many message bytes. The replay removes the framing bytes; each Kafka record value is one raw ITCH message. Validate this framing against the actual data file/specification before running experiments.
- `P`, `Q`, `A`, `F`, `E`, `C`, `X`, `D`, and `R` are decoded. Other messages are ignored by Core. ITCH system event `S`, MWCB status `W`, and broken trade `B` have no Stock Locate and are sent with a null Kafka key; all other records use the two-byte big-endian Stock Locate as the key. Broken-trade corrections are not yet reflected in OHLC.
- The order map is intentionally limited to add/execute/cancel/delete transitions. It is not a full order book. `P`/`Q` prints and printable `C` executions contribute trades; `E` uses the resting order price. Prices remain in ITCH's integer ten-thousandths-of-a-dollar units.
- Windows use nanoseconds since midnight from the ITCH timestamp. Windows advance independently per Stock Locate; a timestamp that moves backwards into a closed window fails explicitly. The partition-end marker flushes the last open windows.
- Kafka topics must exist before starting. Create `itch.raw` with the desired partition count. Configure `itch.ohlc` with `cleanup.policy=compact` so duplicate at-least-once output keys converge to the latest `(locate, window)` value.
- The replay producer uses idempotence and writes a UTC producer timestamp header. The consumer disables auto-commit and commits a partition only after its end marker has been processed and its last windows published. Deferring commits to the marker lets a restarted consumer rebuild its in-memory order map from the start; use a fresh group ID for each replay run. This is at-least-once, not exactly-once.
- For a fair latency experiment, define the same source-to-result interval in both modes (for example: message read by the monolith / replay producer timestamp header through OHLC result write). The header is included now; latency collection is not.

## Build and tests

```sh
dotnet test Itch.sln
dotnet build Itch.sln -c Release
```

## Run

Create the topics first, with `itch.raw` partitioned as required by the experiment and the compacted result topic:

```sh
kafka-topics --bootstrap-server localhost:9092 --create --if-not-exists \
  --topic itch.raw --partitions 8 --replication-factor 1
kafka-topics --bootstrap-server localhost:9092 --create --if-not-exists \
  --topic itch.ohlc --partitions 8 --replication-factor 1 \
  --config cleanup.policy=compact
```

Run the baseline:

```sh
dotnet run --project Itch.Monolith -- path/to/itch-file output/monolith.csv 60
```

Replay and consume (start the consumer before replay):

```sh
dotnet run --project Itch.Consumer -- localhost:9092 itch.raw itch.ohlc itch-v1 60
dotnet run --project Itch.Replay -- path/to/itch-file localhost:9092 itch.raw
```

Export `itch.ohlc` values as JSON lines (keys and headers excluded), then compare them with the baseline:

```sh
kafka-console-consumer --bootstrap-server localhost:9092 --topic itch.ohlc \
  --from-beginning --property print.key=false --property print.value=true \
  --property print.headers=false > output/stream.jsonl
dotnet run --project Itch.Verify -- output/monolith.csv output/stream.jsonl
```

The consumer group persists committed offsets. Use a fresh group ID when replaying the complete file for a new comparison. The JSON export may contain compacted-topic records in a different order or duplicate values before compaction; Verify sorts by Stock Locate and window and keeps the last JSON value for each key. Duplicate keys in CSV input are rejected.
