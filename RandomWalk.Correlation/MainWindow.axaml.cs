using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using System.Collections.ObjectModel;

namespace RandomWalk.Correlation;

public partial class MainWindow : Window
{
    private const int WindowSize = 60;
    private readonly List<string> _labels;
    private readonly List<List<double>> _windows;
    private readonly ObservableCollection<WeightedPoint> _heatData = [];

    public ISeries[] Series { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    public MainWindow(List<string> sources)
    {
        _labels = sources.Select((s, i) => $"S{i + 1}:{s.Split(':')[1]}").ToList();
        _windows = sources.Select(_ => new List<double>()).ToList();

        Series =
        [
            new HeatSeries<WeightedPoint>
            {
                Values = _heatData,
                HeatMap = new LiveChartsCore.Drawing.LvcColor[]
                {
                    new(215, 48, 39),   // red   = -1
                    new(255, 255, 255), // white =  0
                    new(26, 152, 80),   // green = +1
                },
                DataLabelsSize = 14,
                DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                DataLabelsFormatter = p => p.Model?.Weight is double w ? w.ToString("F2") : "N/A"
            }
        ];

        XAxes =
        [
            new Axis
            {
                Labels = _labels,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#aaaaaa")),
                SeparatorsPaint = null
            }
        ];

        YAxes =
        [
            new Axis
            {
                Labels = [.. _labels.AsEnumerable().Reverse()],
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#aaaaaa")),
                SeparatorsPaint = null
            }
        ];

        // Pre-populate heatmap grid so we update in place rather than clear+rebuild
        var n = sources.Count;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                _heatData.Add(new WeightedPoint(j, n - 1 - i, i == j ? 1.0 : 0.5));

        DataContext = this;
        InitializeComponent();

        Title = $"Correlation — {sources.Count} stream{(sources.Count > 1 ? "s" : "")}";

        for (var i = 0; i < sources.Count; i++)
        {
            var idx = i;
            var src = sources[i].Contains("://") ? sources[i] : $"tcp://{sources[i]}";
            Task.Run(() => SubscribeLoop(src, idx));
        }
    }

    private readonly object _lock = new();

    private void SubscribeLoop(string source, int streamIdx)
    {
        using var sub = new SubscriberSocket();
        sub.Connect(source);
        sub.SubscribeToAnyTopic();

        while (true)
        {
            if (!sub.TryReceiveFrameString(TimeSpan.FromMilliseconds(500), out var msg) || msg is null)
                continue;

            var tick = JsonConvert.DeserializeObject<TickMessage>(msg);
            if (tick is null) continue;

            double[][] matrix;
            lock (_lock)
            {
                var w = _windows[streamIdx];
                w.Add(tick.Value);
                if (w.Count > WindowSize) w.RemoveAt(0);
                matrix = ComputeMatrix();
            }

            Dispatcher.UIThread.Post(() => UpdateHeatmap(matrix));
        }
    }

    private double[][] ComputeMatrix()
    {
        var n = _labels.Count;
        var mat = new double[n][];
        for (var i = 0; i < n; i++)
        {
            mat[i] = new double[n];
            for (var j = 0; j < n; j++)
                mat[i][j] = i == j ? 1.0 : Correlation(_windows[i], _windows[j]);
        }
        return mat;
    }

    private void UpdateHeatmap(double[][] matrix)
    {
        var n = matrix.Length;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var raw = matrix[j][i];
                var normalized = double.IsNaN(raw) ? 0.5 : (raw + 1.0) / 2.0;
                var point = _heatData[i * n + j];
                point.Weight = normalized;
            }
    }

    private static double Correlation(List<double> x, List<double> y)
    {
        var len = Math.Min(x.Count, y.Count);
        if (len < 2) return double.NaN;
        var xs = x.TakeLast(len).ToArray();
        var ys = y.TakeLast(len).ToArray();
        double mx = xs.Average(), my = ys.Average();
        double num = 0, dx = 0, dy = 0;
        for (var i = 0; i < len; i++)
        {
            double xi = xs[i] - mx, yi = ys[i] - my;
            num += xi * yi; dx += xi * xi; dy += yi * yi;
        }
        var denom = Math.Sqrt(dx * dy);
        return denom == 0 ? 0 : num / denom;
    }
}
