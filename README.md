# Random Walk

A suite of networked programs that generate, display, plot, replay, and correlate random walk data streams.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Generator (Task 1)                                     │
│  PUB  :5555  — live tick stream                         │
│  REP  :5556  — history queries                          │
│  DB   generator.db — all ticks stored to SQLite         │
└────────────┬────────────────────────┬───────────────────┘
             │ SUB                    │ REQ (history)
    ┌────────┼────────┐               ▼
    │        │        │          Replay (Task 4)
Display   Plotter  Correlation   PUB :<dynamic port>
(Task 2) (Task 3)  (Task 5)           │
                        │             │ SUB
                        └─────────────┘
                     (can consume live or replay streams)
```

All programs communicate over **TCP** using ZeroMQ, so any program can run on a different machine as long as they are on the same network.

---

## Prerequisites

- .NET 10 SDK

---

## Programs

### Task 1 — Generator

Generates a random walk starting at a random value between 20.00 and 80.00, updating every second with a step of ±0.01. Publishes each tick over the network and stores all ticks in a local SQLite database for later replay.

```bash
cd RandomWalk.Generator
dotnet run                      # 50% up / 50% down, fixed step 0.01
dotnet run 0.6                  # 60% up / 40% down
dotnet run 0.5 variable         # step is 0.01 below 60.00, 0.05 above 60.00
```

**Ports used:**
| Port | Purpose |
|------|---------|
| 5555 | PUB — broadcasts live ticks to any subscriber |
| 5556 | REP — answers history range queries from Replay |

---

### Task 2 — Display

Connects to a tick stream and displays the current value in the terminal, updating live with a colour-coded up/down arrow.

```bash
cd RandomWalk.Display
dotnet run                          # live stream from localhost
dotnet run 192.168.1.10             # live stream from another machine
dotnet run localhost 6001           # replay stream on port 6001
```

**Arguments:** `[host] [port]`
- `host` — hostname or IP of the stream source (default: `localhost`)
- `port` — ZMQ PUB port (default: `5555`)

---

### Task 3 — Plotter

Connects to a tick stream and serves a live timeseries chart in the browser, showing all values since the program started.

```bash
cd RandomWalk.Plotter
dotnet run                          # live stream from localhost, UI on :5200
dotnet run localhost:6001           # replay stream on port 6001
dotnet run localhost:6001 5201      # replay on 6001, web UI on port 5201
```

Then open **http://localhost:5200** (or whichever port) in a browser.

**Arguments:** `[host:port] [httpPort]`
- `host:port` — ZMQ source (default: `localhost:5555`)
- `httpPort`  — port for the web UI (default: `5200`)

---

### Task 4 — Replay

Fetches a historical time range from the generator and replays it tick-by-tick at the original 1-second cadence on its own PUB port. Multiple instances can run in parallel — each gets its own port.

```bash
cd RandomWalk.Replay
dotnet run localhost 20 10          # replay from 20 mins ago to 10 mins ago
dotnet run localhost 60 50          # replay from 60 mins ago to 50 mins ago
dotnet run localhost 20 10 6001     # same but on explicit port 6001
```

**Arguments:** `<generatorHost> <fromMinutesAgo> <toMinutesAgo> [pubPort]`
- `generatorHost`  — host running the generator
- `fromMinutesAgo` — start of the replay window (further back in time)
- `toMinutesAgo`   — end of the replay window (closer to now)
- `pubPort`        — port to publish on (default: auto-assigned, printed on startup)

The port is printed when the program starts:
```
Publishing replay on tcp://*:54321
Waiting 3s for subscribers to connect...
```

Start your Display or Plotter within those 3 seconds, pointing at the printed port.

#### Running multiple replays in parallel

```bash
# Terminal 1
dotnet run localhost 20 10          # → publishes on e.g. port 54321

# Terminal 2
dotnet run localhost 60 50          # → publishes on e.g. port 54892

# Terminal 3 — watch replay 1
cd ../RandomWalk.Display && dotnet run localhost 54321

# Terminal 4 — watch replay 2
cd ../RandomWalk.Plotter && dotnet run localhost:54892 5201
```

---

### Task 5 — Correlation

Subscribes to any number of streams simultaneously (live or replay), maintains a 60-tick rolling window per stream, and displays a live correlation heatmap in the browser.

```bash
cd RandomWalk.Correlation

# Single live stream
dotnet run 5300 tcp://localhost:5555

# Two live streams
dotnet run 5300 tcp://localhost:5555 tcp://192.168.1.10:5555

# Mix of live and replay
dotnet run 5300 tcp://localhost:5555 tcp://localhost:6001 tcp://localhost:6002
```

Then open **http://localhost:5300** in a browser.

**Arguments:** `[httpPort] [tcp://host:port ...]`
- `httpPort`         — port for the web UI (default: `5300`)
- `tcp://host:port`  — one or more ZMQ sources (default: `tcp://localhost:5555`)

---

## Orchestrator

The easiest way to manage everything is the **Orchestrator** — a single terminal where you start, monitor, and stop all programs with short commands.

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

> plotter localhost:5555
  [4] Plotter started → http://localhost:5200  (source: localhost:5555)

> plotter localhost:54321
  [5] Plotter started → http://localhost:5201  (source: localhost:54321)

> display localhost 54892
  [6] Display started (source: localhost:54892)

> correlate localhost:5555 localhost:54321 localhost:54892
  [7] Correlation started → http://localhost:5300

> open 4
  Opening http://localhost:5200

> list
  ID   Type         Description                                   URL/Port                       Status
  ----------------------------------------------------------------------------------------------------
  [1]  generator    args: default                                                                running
  [2]  replay       localhost  20→10 mins ago                     :54321                         running
  ...

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
| `display <host> [port]` | Start a terminal display |
| `plotter <host:port>` | Start a browser plotter (auto-assigns HTTP port) |
| `correlate <host:port> [...]` | Start a correlation heatmap (auto-assigns HTTP port) |
| `list` | Show all running processes |
| `stop <id>` | Stop a process by ID |
| `open <id>` | Open browser for a plotter or correlation |
| `quit` | Stop everything and exit |

---

## Typical workflow

```
Terminal 1  →  dotnet run (Generator)
Terminal 2  →  dotnet run (Display)         — open terminal display
Terminal 3  →  dotnet run (Plotter)         — open http://localhost:5200
Terminal 4  →  dotnet run localhost 20 10   (Replay)
Terminal 5  →  dotnet run localhost:54321   (Plotter on port 5201 pointed at replay)
Terminal 6  →  dotnet run 5300 tcp://localhost:5555 tcp://localhost:54321  (Correlation)
```

---

## Running across multiple machines

Replace `localhost` with the IP address of the machine running the target program. Make sure the relevant ports are reachable (not blocked by a firewall).

| Program     | Ports to open |
|-------------|--------------|
| Generator   | 5555, 5556   |
| Replay      | the port printed at startup |
| Plotter     | 5200 (or chosen httpPort) |
| Correlation | 5300 (or chosen httpPort) |
