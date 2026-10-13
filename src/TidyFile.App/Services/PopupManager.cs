using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TidyFile.App.Views;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.Files;
using TidyFile.Core.History;
using TidyFile.Core.Learning;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.App.Services;

/// <summary>
/// Shows one popup at a time and queues the rest, so a burst of downloads doesn't cover the screen.
/// Must be used on the UI thread.
/// </summary>
public sealed class PopupManager
{
    private readonly Queue<string> _queue = new();
    private readonly RulesProvider _rules;
    private readonly MoveService _mover;
    private readonly HistoryStore _history;
    private readonly AppSettings _settings;
    private readonly TrayIcon _tray;
    private SuggestionPopup? _current;

    public PopupManager(RulesProvider rules, MoveService mover, HistoryStore history, AppSettings settings, TrayIcon tray)
    {
        _rules = rules;
        _mover = mover;
        _history = history;
        _settings = settings;
        _tray = tray;
    }

    public void Enqueue(string path)
    {
        if (_current?.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase) == true ||
            _queue.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _queue.Enqueue(path);
        _current?.UpdateWaitingCount(_queue.Count);
        ShowNext();
    }

    /// <summary>Closes the current popup and forgets anything waiting (used when pausing).</summary>
    public void Clear()
    {
        _queue.Clear();
        _current?.Close();
    }

    private void ShowNext()
    {
        if (_current is not null)
        {
            return;
        }

        while (_queue.Count > 0)
        {
            var path = _queue.Dequeue();
            if (!File.Exists(path))
            {
                continue; // Moved or deleted while waiting.
            }

            // A tiny local read (the browser's "Zone.Identifier" note); cheap enough for the UI thread.
            var source = DownloadSource.Read(path);

            IReadOnlyList<Suggestion> suggestions;
            try
            {
                var max = _settings.MaxSuggestions;
                var fromRules = _rules.GetEngine().Suggest(path, max, source);
                var learned = ChoiceLearner.Suggest(_history.ReadAll(), path, source, max);
                suggestions = ChoiceLearner.Merge(fromRules, learned, max);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not build suggestions for '{path}'.", ex);
                suggestions = Array.Empty<Suggestion>(); // Still show the popup so Browse… works.
            }

            var popup = new SuggestionPopup(path, suggestions, TimeSpan.FromSeconds(_settings.PopupSeconds), _queue.Count, source);
            popup.MoveRequested += (destination, ruleName) => MoveFile(path, destination, ruleName, source?.DisplayDomain);
            popup.Closed += (_, _) =>
            {
                _current = null;
                ShowNext();
            };

            _current = popup;
            popup.Show();
            return;
        }
    }

    // async void: an event handler whose errors are all caught below. The popup has already closed,
    // so the next one can show while a large cross-drive move is still copying.
    private async void MoveFile(string path, string destinationFolder, string? ruleName, string? sourceDomain)
    {
        try
        {
            var entry = await _mover.MoveAsync(path, destinationFolder, ruleName, sourceDomain); // Resumes on the UI thread.
            Log.Info($"Moved '{entry.From}' -> '{entry.To}' (rule: {ruleName ?? "manual"}).");
            _tray.ShowInfo("Moved", $"{Path.GetFileName(entry.To)} → {PathTokens.Shorten(destinationFolder)}");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move '{path}' to '{destinationFolder}'.", ex);
            _tray.ShowError("Could not move file", ex.Message);
        }
    }
}
