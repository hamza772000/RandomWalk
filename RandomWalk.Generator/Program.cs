using Microsoft.Data.Sqlite;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using RandomWalk.Common;
// Usage: dotnet run [upProbability] [variable]
//   upProbability: 0.0–1.0, default 0.5
//   variable: pass "variable" to enable step-size-as-function (optional 1.2)
double upProbability = 0.5;
if (args.Length >= 1 && double.TryParse(args[0], out double p))
    upProbability = p;

double StepSize(double value) =>
    args.Length >= 2 && args[1] == "variable"
        ? (value >= 60.0 ? 0.05 : 0.01)
        : 0.01;

// --- SQLite setup ---
const string DbPath = "generator.db";
using var db = new SqliteConnection($"Data Source={DbPath}");
db.Open();
using (var cmd = db.CreateCommand())
{
    cmd.CommandText = "DROP TABLE IF EXISTS ticks; CREATE TABLE ticks (timestamp_ms INTEGER, value REAL)";
    cmd.ExecuteNonQuery();
}

void Store(long tsMs, double value)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "INSERT INTO ticks(timestamp_ms, value) VALUES($ts, $v)";
    cmd.Parameters.AddWithValue("$ts", tsMs);
    cmd.Parameters.AddWithValue("$v", value);
    cmd.ExecuteNonQuery();
}

// --- Initial value: random between 20.00 and 80.00 ---
var rng = new Random();
double current = Math.Round(20.0 + rng.NextDouble() * 60.0, 2);
Console.WriteLine($"Starting value : {current:F2}");
Console.WriteLine($"Up probability : {upProbability:P0}");
Console.WriteLine($"PUB  tcp://*:{Ports.GeneratorPub}");
Console.WriteLine($"HIST tcp://*:{Ports.GeneratorHistory}");
Console.WriteLine("Press Ctrl+C to stop.\n");

using var pubSocket = new PublisherSocket();
pubSocket.Bind($"tcp://*:{Ports.GeneratorPub}");

using var repSocket = new ResponseSocket();
repSocket.Bind($"tcp://*:{Ports.GeneratorHistory}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// History query handler on a background thread
var historyTask = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        if (!repSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(500), out var msg) || msg is null)
            continue;

        try
        {
            var req = JsonConvert.DeserializeAnonymousType(msg, new { from = 0L, to = 0L })!;
            var ticks = new List<TickMessage>();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT timestamp_ms, value FROM ticks " +
                "WHERE timestamp_ms >= $f AND timestamp_ms <= $t " +
                "ORDER BY timestamp_ms";
            cmd.Parameters.AddWithValue("$f", req.from);
            cmd.Parameters.AddWithValue("$t", req.to);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ticks.Add(new TickMessage(reader.GetInt64(0), reader.GetDouble(1)));

            repSocket.SendFrame(JsonConvert.SerializeObject(ticks));
        }
        catch
        {
            repSocket.SendFrame("[]");
        }
    }
});

// Main loop: tick every second, publish and store
try
{
    while (!cts.IsCancellationRequested)
    {
        var tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Store(tsMs, current);

        var tick = new TickMessage(tsMs, current);
        pubSocket.SendFrame(JsonConvert.SerializeObject(tick));
        Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss}]  {current:F2}");

        await Task.Delay(1000, cts.Token);

        var step = StepSize(current);
        current = Math.Round(current + (rng.NextDouble() < upProbability ? step : -step), 2);
    }
}
catch (OperationCanceledException) { }

await historyTask;
Console.WriteLine("\nGenerator stopped.");
