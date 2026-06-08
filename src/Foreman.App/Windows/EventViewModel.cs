using Foreman.Core.Models;
using System.Windows.Media;

namespace Foreman.App.Windows;

public sealed class EventViewModel
{
    public string Timestamp     { get; init; } = string.Empty;
    public string SeverityLabel { get; init; } = string.Empty;
    public Brush  SeverityBrush { get; init; } = Brushes.Gray;
    /// <summary>Row text colour — slightly dimmed for Info, full colour for alerts.</summary>
    public Brush  RowForeground { get; init; } = Brushes.LightGray;
    public string Source        { get; init; } = string.Empty;
    public string Message       { get; init; } = string.Empty;
    public string EventId       { get; init; } = string.Empty;

    /// <summary>Original domain event — used by AlertDetailWindow.</summary>
    public ForemanEvent OriginalEvent { get; init; } = null!;

    public static EventViewModel FromEvent(ForemanEvent evt)
    {
        return new EventViewModel
        {
            Timestamp     = evt.Timestamp.LocalDateTime.ToString("HH:mm:ss.fff"),
            SeverityLabel = evt.Severity.ToString().ToUpperInvariant(),
            SeverityBrush = SeverityToBrush(evt.Severity),
            RowForeground = RowBrush(evt.Severity),
            Source        = evt.Source,
            Message       = evt.Message,
            EventId       = evt.Id,
            OriginalEvent = evt,
        };
    }

    private static Brush SeverityToBrush(ForemanSeverity s) => s switch
    {
        ForemanSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xDD, 0x33, 0x33)),
        ForemanSeverity.High     => new SolidColorBrush(Color.FromRgb(0xEE, 0x77, 0x33)),
        ForemanSeverity.Medium   => new SolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x3C)),
        ForemanSeverity.Low      => new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0x78)),
        _                        => new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)),
    };

    private static Brush RowBrush(ForemanSeverity s) => s switch
    {
        ForemanSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xCC)),
        ForemanSeverity.High     => new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0xAA)),
        ForemanSeverity.Medium   => new SolidColorBrush(Color.FromRgb(0xF0, 0xE8, 0xC8)),
        ForemanSeverity.Low      => new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
        ForemanSeverity.Info     => new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90)),
        _                        => new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
    };
}
