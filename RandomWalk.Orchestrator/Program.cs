using System.Diagnostics;
using System.Text.RegularExpressions;

// Resolve sibling project directories relative to this project
var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

string ProjectDir(string name) => Path.Combine(baseDir, name);

// --- Process registry ---
var processes = new Dictionary<int, ManagedProcess>();
int nextId = 1;

// --- Helpers ---
Process Spawn(string project, string arguments, Action<string>? onOutput = null)
{
    var p = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run -- {arguments}",
            WorkingDirectory = ProjectDir(project),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        },
        EnableRaisingEvents = true
    };

    p.OutputDataReceived += (_, e) =>
    {
        if (e.Data is null) return;
        onOutput?.Invoke(e.Data);
    };
    p.ErrorDataReceived += (_, e) => { /* suppress */ };
    p.Start();
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

void Register(int id, string type, string description, Process proc)
{
    processes[id] = new ManagedProcess(id, type, description, proc);
}

// Starts a replay and returns a Task that completes once the port is known
Task<int> StartReplay(string host, string from, string to)
{
    var id = nextId++;
    var desc = $"{host}  {from}→{to} mins ago";
    var tcs = new TaskCompletionSource<int>();

    var proc = Spawn("RandomWalk.Replay", $"{host} {from} {to}", output =>
    {
        if (!tcs.Task.IsCompleted)
        {
            var m = Regex.Match(output, @"tcp://\*:(\d+)");
            if (m.Success)
            {
                var port = int.Parse(m.Groups[1].Value);
                processes[id] = processes[id] with { Port = port };
                Console.WriteLine($"  [{id}] Replay ready on port {port}  ({desc})");
                Console.Write("> ");
                tcs.SetResult(port);
            }
        }
    });

    Register(id, "replay", desc, proc);
    Console.WriteLine($"  [{id}] Replay starting... ({desc})");
    return tcs.Task;
}

void StartDisplay(string source)
{
    var id = nextId++;
    var proc = Spawn("RandomWalk.Display", source);
    Register(id, "display", $"source: {source}", proc);
    Console.WriteLine($"  [{id}] Display window opening  (source: {source})");
}

void StartPlotter(string source)
{
    var id = nextId++;
    var proc = Spawn("RandomWalk.Plotter", source);
    Register(id, "plotter", $"source: {source}", proc);
    Console.WriteLine($"  [{id}] Plotter window opening  (source: {source})");
}

void StartCorrelation(IEnumerable<string> sources)
{
    var id = nextId++;
    var sourceList = string.Join(" ", sources);
    var proc = Spawn("RandomWalk.Correlation", sourceList);
    Register(id, "correlate", $"sources: {sourceList}", proc);
    Console.WriteLine($"  [{id}] Correlation window opening");
}

void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("  Commands:");
    Console.WriteLine("    generator [upProb] [variable]      Start the generator");
    Console.WriteLine("    replay <host> <fromMins> <toMins>  Start a replay, prints its port");
    Console.WriteLine("    display <host:port>                Open a display window");
    Console.WriteLine("    plotter <host:port>                Open a plotter window");
    Console.WriteLine("    correlate <host:port> [...]        Open a correlation heatmap window");
    Console.WriteLine("    demo [host]                        Run the full demo sequence");
    Console.WriteLine("    list                               Show all running processes");
    Console.WriteLine("    stop <id>                          Stop a process by ID");
    Console.WriteLine("    help                               Show this help");
    Console.WriteLine("    quit                               Stop everything and exit");
    Console.WriteLine();
}

void PrintList()
{
    if (processes.Count == 0)
    {
        Console.WriteLine("  No processes running.");
        return;
    }
    Console.WriteLine();
    Console.WriteLine($"  {"ID",-4} {"Type",-12} {"Description",-50} {"Status"}");
    Console.WriteLine($"  {new string('-', 80)}");
    foreach (var mp in processes.Values)
    {
        var status = mp.Process.HasExited ? "stopped" : "running";
        Console.WriteLine($"  {mp.Id,-4} {mp.Type,-12} {mp.Description,-50} {status}");
    }
    Console.WriteLine();
}

async Task RunDemo(string host)
{
    Console.WriteLine();
    Console.WriteLine("  ── Demo starting ──────────────────────────────────────────");
    Console.WriteLine();

    // Step 1: Replay A — 20 mins ago to 10 mins ago
    Console.WriteLine("  Step 1: Replay A  (20 mins ago → 10 mins ago)");
    var portA = await StartReplay(host, "20", "10");
    StartDisplay($"localhost:{portA}");
    StartPlotter($"localhost:{portA}");

    Console.WriteLine();
    await Task.Delay(2000);

    // Step 2: Replay B — 15 mins ago to 10 mins ago
    Console.WriteLine("  Step 2: Replay B  (15 mins ago → 10 mins ago)");
    var portB = await StartReplay(host, "15", "10");
    StartDisplay($"localhost:{portB}");
    StartPlotter($"localhost:{portB}");

    Console.WriteLine();
    await Task.Delay(2000);

    // Step 3: Correlation across both replays
    Console.WriteLine("  Step 3: Correlation across both replays");
    StartCorrelation([$"localhost:{portA}", $"localhost:{portB}"]);

    Console.WriteLine();
    Console.WriteLine("  ── Demo running ───────────────────────────────────────────");
    Console.WriteLine();
    Console.Write("> ");
}

// --- REPL ---
Console.WriteLine("Random Walk Orchestrator");
Console.WriteLine("Type 'help' for available commands.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(line)) continue;

    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLower();

    switch (cmd)
    {
        case "help":
            PrintHelp();
            break;

        case "generator":
        {
            var extraArgs = string.Join(" ", parts.Skip(1));
            var id = nextId++;
            var proc = Spawn("RandomWalk.Generator", extraArgs, output =>
            {
                Console.WriteLine($"  [gen] {output}");
            });
            Register(id, "generator", $"args: {(extraArgs.Length > 0 ? extraArgs : "default")}", proc);
            Console.WriteLine($"  [{id}] Generator started (PUB :5555  HIST :5556)");
            break;
        }

        case "replay":
        {
            if (parts.Length < 4)
            {
                Console.WriteLine("  Usage: replay <host> <fromMinsAgo> <toMinsAgo>");
                break;
            }
            _ = StartReplay(parts[1], parts[2], parts[3]);
            break;
        }

        case "display":
        {
            if (parts.Length < 2) { Console.WriteLine("  Usage: display <host:port>"); break; }
            StartDisplay(parts[1]);
            break;
        }

        case "plotter":
        {
            if (parts.Length < 2) { Console.WriteLine("  Usage: plotter <host:port>"); break; }
            StartPlotter(parts[1]);
            break;
        }

        case "correlate":
        {
            if (parts.Length < 2) { Console.WriteLine("  Usage: correlate <host:port> [host:port ...]"); break; }
            StartCorrelation(parts.Skip(1));
            break;
        }

        case "demo":
        {
            var host = parts.Length >= 2 ? parts[1] : "localhost";
            _ = RunDemo(host);
            break;
        }

        case "list":
            PrintList();
            foreach (var key in processes.Keys.Where(k => processes[k].Process.HasExited).ToList())
                processes.Remove(key);
            break;

        case "stop":
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out int stopId))
            {
                Console.WriteLine("  Usage: stop <id>");
                break;
            }
            if (!processes.TryGetValue(stopId, out var mp))
            {
                Console.WriteLine($"  No process with id {stopId}");
                break;
            }
            if (!mp.Process.HasExited)
            {
                mp.Process.Kill(entireProcessTree: true);
                Console.WriteLine($"  [{stopId}] Stopped.");
            }
            else
            {
                Console.WriteLine($"  [{stopId}] Already stopped.");
            }
            processes.Remove(stopId);
            break;
        }

        case "quit":
        case "exit":
            Console.WriteLine("  Stopping all processes...");
            foreach (var mp in processes.Values.Where(mp => !mp.Process.HasExited))
                mp.Process.Kill(entireProcessTree: true);
            Console.WriteLine("  Done. Goodbye.");
            return;

        default:
            Console.WriteLine($"  Unknown command '{cmd}'. Type 'help' for available commands.");
            break;
    }
}

record ManagedProcess(int Id, string Type, string Description, Process Process, int? Port = null);
