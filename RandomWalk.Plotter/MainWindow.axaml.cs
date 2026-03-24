using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using RandomWalk.Common;
using SkiaSharp;

namespace RandomWalk.Plotter;

public partial class MainWindow : Window
{
    private const int MaxPoints = 300;
    private readonly ObservableCollection<DateTimePoint> _values = [];

    public ISeries[] Series { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    public MainWindow(string source)
    {
        var parts = source.Split(':');
        var host = parts[0];
        int port = int.TryParse(parts[1], out int p) ? p : Ports.GeneratorPub;

        Series =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _values,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColor.Parse("#a0c4ff"), 2),
                LineSmoothness = 0
            }
        ];

        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromSeconds(1), d => d.ToString("HH:mm:ss"))
            {
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#aaaaaa")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#333333"))
            }
        ];

        YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#aaaaaa")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#333333"))
            }
        ];

        DataContext = this;
        InitializeComponent();

        Title = $"Plotter — {source}";
        SourceLabel.Text = port == Ports.GeneratorPub ? "LIVE" : $"REPLAY :{port}";

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

            Dispatcher.UIThread.Post(() =>
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(tick.TimestampMs).LocalDateTime;
                _values.Add(new DateTimePoint(time, tick.Value));
                if (_values.Count > MaxPoints)
                    _values.RemoveAt(0);

                CurrentValue.Text = tick.Value.ToString("F2");
            });
        }
    }
}
