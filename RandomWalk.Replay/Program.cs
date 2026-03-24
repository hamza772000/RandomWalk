using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using RandomWalk.Common;

// Usage: dotnet run <generatorHost> <fromMinutesAgo> <toMinutesAgo> [pubPort]
//   generatorHost  : host running the generator, e.g. "localhost"
//   fromMinutesAgo : start of replay window, e.g. 20
//   toMinutesAgo   : end of replay window, e.g. 10
//   pubPort        : port this replay instance publishes on (default: auto-pick in 6000–6999)
if (args.Length < 3)
{
    Console.WriteLine("Usage: dotnet run <generatorHost> <fromMinutesAgo> <toMinutesAgo> [pubPort]");
    Console.WriteLine("Example: dotnet run localhost 20 10");
    return;
}

string genHost = args[0];
double fromMinsAgo = double.Parse(args[1]);
double toMinsAgo = double.Parse(args[2]);

int pubPort = args.Length >= 4 && int.TryParse(args[3], out int pp) ? pp : 0;
if (pubPort == 0)
{
    // Find a free port in range 6000–6999
    using var tmp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    tmp.Start();
    pubPort = ((System.Net.IPEndPoint)tmp.LocalEndpoint).Port;
    tmp.Stop();
}

long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
long fromMs = now - (long)(fromMinsAgo * 60 * 1000);
long toMs   = now - (long)(toMinsAgo   * 60 * 1000);

Console.WriteLine($"Querying history from generator at tcp://{genHost}:{Ports.GeneratorHistory}");
Console.WriteLine($"Window: {fromMinsAgo} mins ago → {toMinsAgo} mins ago");

// --- Fetch history from generator ---
List<TickMessage> ticks;
using (var req = new RequestSocket())
{
    req.Connect($"tcp://{genHost}:{Ports.GeneratorHistory}");
    var query = JsonConvert.SerializeObject(new { from = fromMs, to = toMs });
    req.SendFrame(query);
    if (!req.TryReceiveFrameString(TimeSpan.FromSeconds(10), out var response) || response is null)
    {
        Console.WriteLine("No response from generator. Is it running?");
        return;
    }
    ticks = JsonConvert.DeserializeObject<List<TickMessage>>(response) ?? [];
}

Console.WriteLine($"Fetched {ticks.Count} ticks.");
if (ticks.Count == 0)
{
    Console.WriteLine("No data in that time range.");
    return;
}

Console.WriteLine($"Publishing replay on tcp://*:{pubPort}");
Console.WriteLine("Waiting 3s for subscribers to connect...");

using var pub = new PublisherSocket();
pub.Bind($"tcp://*:{pubPort}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Wait for subscribers to connect before sending the first tick
await Task.Delay(3000, cts.Token).ContinueWith(_ => { });
Console.WriteLine("Starting replay. Press Ctrl+C to stop.\n");

try
{
    for (int i = 0; i < ticks.Count && !cts.IsCancellationRequested; i++)
    {
        var tick = ticks[i];

        // Replay at original 1-second cadence
        long delayMs = i == 0 ? 0 : ticks[i].TimestampMs - ticks[i - 1].TimestampMs;
        if (delayMs > 0)
            await Task.Delay((int)Math.Min(delayMs, 5000), cts.Token);

        pub.SendFrame(JsonConvert.SerializeObject(tick));
        Console.WriteLine($"[{i + 1}/{ticks.Count}] {DateTimeOffset.FromUnixTimeMilliseconds(tick.TimestampMs):HH:mm:ss}  {tick.Value:F2}");
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("\nReplay finished.");
