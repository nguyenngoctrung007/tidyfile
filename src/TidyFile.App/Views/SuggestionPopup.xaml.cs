using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TidyFile.Core;
using TidyFile.Core.Files;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;

namespace TidyFile.App.Views;

/// <summary>
/// The small bottom-right card shown when a new file arrives.
/// It never steals focus and closes by itself (the timer pauses while the mouse is over it).
/// </summary>
public partial class SuggestionPopup : Window
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _lifetime;
    private readonly string _filePath;
    private TimeSpan _remaining;
    private bool _dialogOpen;

    /// <param name="source">Where the file was downloaded from, shown as "from github.com" when known.</param>
    public SuggestionPopup(string filePath, IReadOnlyList<Suggestion> suggestions, TimeSpan lifetime, int waitingCount,
        DownloadSource? source = null)
    {
        InitializeComponent();

        _filePath = filePath;
        _lifetime = lifetime < TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : lifetime;
        _remaining = _lifetime;

        var folder = Path.GetDirectoryName(filePath) ?? "";
        HeaderText.Text = $"New file in {PathTokens.Shorten(folder)}";
        FileNameText.Text = Path.GetFileName(filePath);
        FileNameText.ToolTip = filePath;
        FileInfoText.Text = DescribeFile(filePath);
        if (source?.DisplayDomain is { } domain)
        {
            FileInfoText.Text += $" · from {domain}";
            if (source.HostDomain is { } host && host != domain)
            {
                FileInfoText.ToolTip = $"Page: {domain}{Environment.NewLine}File: {host}";
            }
        }
        UpdateWaitingCount(waitingCount);
        BuildSuggestionButtons(suggestions);

        // Start near the corner so there is no visible jump before the exact position is known.
        WindowStartupLocation = WindowStartupLocation.Manual;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width;
        Top = workArea.Bottom - 260;

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += OnTimerTick;

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            _timer.Start();
        };
        SizeChanged += (_, _) => PositionBottomRight();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Raised with (destination folder, rule name or null) when the user picks a destination.</summary>
    public event Action<string, string?>? MoveRequested;

    public string FilePath => _filePath;

    public void UpdateWaitingCount(int waitingCount)
    {
        QueueText.Text = waitingCount > 0 ? $"+{waitingCount} more waiting" : "";
    }

    private void BuildSuggestionButtons(IReadOnlyList<Suggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            MoveToLabel.Visibility = Visibility.Collapsed;
            NoSuggestionText.Visibility = Visibility.Visible;
            return;
        }

        for (var i = 0; i < suggestions.Count; i++)
        {
            var suggestion = suggestions[i];

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = PathTokens.Shorten(suggestion.Destination),
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            content.Children.Add(new TextBlock
            {
                Text = suggestion.Reason,
                FontSize = 11,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var button = new Button
            {
                Content = content,
                ToolTip = suggestion.Destination,
                Style = (Style)FindResource(i == 0 ? "PrimarySuggestionButton" : "SuggestionButton"),
            };
            button.Click += (_, _) => Choose(suggestion.Destination, suggestion.RuleName);

            SuggestionsPanel.Children.Add(button);
        }
    }

    private void Choose(string destinationFolder, string? ruleName)
    {
        MoveRequested?.Invoke(destinationFolder, ruleName);
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Move \"{Path.GetFileName(_filePath)}\" to…",
        };

        _dialogOpen = true;
        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                Choose(dialog.FolderName, null);
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsMouseOver || _dialogOpen)
        {
            return; // Give the user all the time they need while they're interacting.
        }

        _remaining -= TickInterval;
        TimeoutBar.Value = Math.Max(0, _remaining.TotalMilliseconds / _lifetime.TotalMilliseconds);

        if (_remaining <= TimeSpan.Zero)
        {
            Close();
        }
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth;
        Top = workArea.Bottom - ActualHeight;
    }

    private static string DescribeFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var extension = info.Extension.TrimStart('.').ToUpperInvariant();
            var size = Format.Bytes(info.Length);
            return string.IsNullOrEmpty(extension) ? size : $"{extension} · {size}";
        }
        catch (IOException)
        {
            return "";
        }
    }
}
