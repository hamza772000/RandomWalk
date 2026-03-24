# Random Walk

A suite of networked programs that generate, display, plot, replay, and correlate random walk data streams. All UI is native desktop via **Avalonia**, with live charts powered by **LiveCharts2**. Network communication uses **ZeroMQ (NetMQ)**, so any program can run on a different machine on the same network.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Generator                                              │
│  PUB  :5555  — live tick stream                         │
│  REP  :5556  — history queries                          │
│  DB   generator.db — all ticks stored to SQLite         │
└────────────┬────────────────────────┬───────────────────┘
             │ SUB                    │ REQ (history)
    ┌────────┼────────┐               ▼
    │        │        │            Replay
 Display  Plotter  Correlation   PUB :<dynamic port>
                        │             │ SUB
                        └─────────────┘
                     (can consume live or replay streams)
```

---

## Prerequisites

- .NET 10 SDK

---

## Programs

### Generator

Generates a random walk starting at a random value between 20.00 and 80.00, updating every second with a step of ±0.01. Publishes each tick over the network and stores all ticks in a local SQLite database for later replay. The database is cleared on every startup.

```bash
cd RandomWalk.Generator
dotnet run                      # 50% up / 50% down, fixed step 0.01
dotnet run 0.6                  # 60% up / 40% down
dotnet run 0.5 variable         # step is 0.01 below 60.00, 0.05 above 60.00
```

| Port | Purpose |
|------|---------|
| 5555 | PUB — broadcasts live ticks to any subscriber |
| 5556 | REP — answers history range queries from Replay |

---

### Display

Opens a native desktop window showing the current value, updating live with a colour-coded up/down arrow (green ▲ / red ▼). Can connect to the live generator or any replay instance.

```bash
cd RandomWalk.Display
dotnet run                      # live stream from localhost
dotnet run localhost:6001       # replay stream on port 6001
dotnet run 192.168.1.10:5555   # live stream from another machine
```

**Arguments:** `[host:port]`
- `host:port` — ZMQ source (default: `localhost:5555`)

---

### Plotter

Opens a native desktop window with a live scrolling timeseries chart (last 300 points), showing all values since the program started.

```bash
cd RandomWalk.Plotter
dotnet run                      # live stream from localhost
dotnet run localhost:6001       # replay stream on port 6001
dotnet run 192.168.1.10:5555   # live stream from another machine
```

**Arguments:** `[host:port]`
- `host:port` — ZMQ source (default: `localhost:5555`)

---

### Replay

Fetches a historical time range from the generator over the network, loads it into memory, then replays it tick-by-tick at the original 1-second cadence on its own PUB port. Multiple instances can run in parallel — each gets its own port.

```bash
cd RandomWalk.Replay
dotnet run localhost 20 10      # replay from 20 mins ago to 10 mins ago
dotnet run localhost 60 50      # replay from 60 mins ago to 50 mins ago
dotnet run localhost 20 10 6001 # same but on explicit port 6001
```

**Arguments:** `<generatorHost> <fromMinutesAgo> <toMinutesAgo> [pubPort]`
- `generatorHost`  — host running the generator
- `fromMinutesAgo` — start of the replay window (further back in time)
- `toMinutesAgo`   — end of the replay window (closer to now)
- `pubPort`        — port to publish on (default: auto-assigned, printed on startup)

The port is printed when the program starts — start your Display or Plotter within the 3-second window before playback begins:
```
Publishing replay on tcp://*:54321
Waiting 3s for subscribers to connect...
```

---

### Correlation

Opens a native desktop window with a live red/white/green heatmap. Subscribes to any number of streams simultaneously (live or replay), maintains a 60-tick rolling window per stream, and recalculates the full correlation matrix on every incoming tick.

```bash
cd RandomWalk.Correlation
dotnet run localhost:5555                                    # single live stream
dotnet run localhost:5555 localhost:54321                   # live + replay
dotnet run localhost:5555 localhost:54321 localhost:54892   # live + two replays
dotnet run 192.168.1.10:5555 localhost:54321               # cross-machine
```

**Arguments:** `<host:port> [host:port ...]`
- One or more ZMQ sources (default: `localhost:5555`)

**Reading the heatmap:**
- Each cell shows the Pearson correlation between two streams over the last 60 ticks
- `+1` (green) — streams move together
- `0` (white) — no relationship
- `-1` (red) — streams move opposite
- Diagonal is always `1.0` (each stream perfectly correlates with itself)

---

## Orchestrator

The easiest way to run everything is the **Orchestrator** — a single terminal where you start, monitor, and stop all programs with short commands. Each display, plotter, or correlation opens as its own native window automatically.

```bash
cd RandomWalk.Orchestrator
dotnet run
```

```
Random Walk Orchestrator
Type 'help' for available commands.

> generator
  [1] Generator started (PUB :5555  HIST :5556)

> replay localhost 20 10
  [2] Replay starting... (waiting for port)
  [2] Replay ready on port 54321  (localhost  20→10 mins ago)

> replay localhost 60 50
  [3] Replay ready on port 54892  (localhost  60→50 mins ago)

> display localhost:5555
  [4] Display window opening  (source: localhost:5555)

> plotter localhost:54321
  [5] Plotter window opening  (source: localhost:54321)

> correlate localhost:5555 localhost:54321 localhost:54892
  [6] Correlation window opening

> list
  ID   Type         Description                                    Status
  ------------------------------------------------------------------------
  [1]  generator    args: default                                  running
  [2]  replay       localhost  20→10 mins ago                      running
  [3]  replay       localhost  60→50 mins ago                      running
  [4]  display      source: localhost:5555                         running
  [5]  plotter      source: localhost:54321                        running
  [6]  correlate    sources: localhost:5555, localhost:54321, ...  running

> stop 3
  [3] Stopped.

> quit
  Stopping all processes...
  Done. Goodbye.
```

### Orchestrator commands

| Command | Description |
|---------|-------------|
| `generator [upProb] [variable]` | Start the generator |
| `replay <host> <fromMins> <toMins>` | Start a replay instance |
| `display <host:port>` | Open a display window |
| `plotter <host:port>` | Open a plotter window |
| `correlate <host:port> [...]` | Open a correlation heatmap window |
| `list` | Show all running processes |
| `stop <id>` | Stop a process by ID (closes its window) |
| `quit` | Stop everything and exit |

---

## Running across multiple machines

Replace `localhost` with the IP address of the machine running the target program. Ensure the relevant ports are reachable on the network.

| Program     | Ports to open        |
|-------------|----------------------|
| Generator   | 5555, 5556           |
| Replay      | port printed at startup |
