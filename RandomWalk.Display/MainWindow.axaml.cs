using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using RandomWalk.Common;

namespace RandomWalk.Display;

public partial class MainWindow : Window
{
    private double? _prev;

    public MainWindow(string source)
    {
        InitializeComponent();

        var parts = source.Split(':');
        var host = parts[0];
        var port = int.TryParse(parts[1], out int p) ? p : Ports.GeneratorPub;

        Title = $"Display — {source}";
        LabelText.Text = port == Ports.GeneratorPub ? "LIVE FEED" : $"REPLAY :{port}";

        Task.Run(() => SubscribeLoop(host, port));
    }

    private void SubscribeLoop(string host, int port)
    {
        using var sub = new SubscriberSocket();
        sub.Connect($"tcp://{host}:{port}");
        sub.SubscribeToAnyTopic();

        while (true)
        {
            if (!sub.TryReceiveFrameString(TimeSpan.FromMilliseconds(500), out var msg) || msg is null)
                continue;

            var tick = JsonConvert.DeserializeObject<TickMessage>(msg);
            if (tick is null) continue;

            Dispatcher.UIThread.Post(() => UpdateUi(tick));
        }
    }

    private void UpdateUi(TickMessage tick)
    {
        var time = DateTimeOffset.FromUnixTimeMilliseconds(tick.TimestampMs).ToLocalTime();
        var up = _prev is null ? (bool?)null : tick.Value > _prev ? true : tick.Value < _prev ? false : null;

        IBrush color = up is null ? Brushes.White : up.Value ? new SolidColorBrush(Color.Parse("#69ff9d")) : new SolidColorBrush(Color.Parse("#ff6b6b"));

        ValueText.Text = tick.Value.ToString("F2");
        ValueText.Foreground = color;
        ArrowText.Text = up is null ? "─" : up.Value ? "▲" : "▼";
        ArrowText.Foreground = color;
        TimeText.Text = time.ToString("HH:mm:ss");

        _prev = tick.Value;
    }
}
