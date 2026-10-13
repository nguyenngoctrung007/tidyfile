using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.Files;

namespace TidyFile.App.Services;

/// <summary>
/// Watches folders for new files and raises <see cref="FileArrived"/> once a file is complete.
/// Uses OS change notifications (FileSystemWatcher), so it costs ~0% CPU while idle.
/// </summary>
public sealed class WatcherService : IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IgnoreWindow = TimeSpan.FromSeconds(30);

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly CancellationTokenSource _cts = new();

    // Paths we've already seen (existing at start-up or already handled), so we never ask twice.
    private readonly ConcurrentDictionary<string, byte> _known = new(StringComparer.OrdinalIgnoreCase);

    // Paths TidyFile is about to write itself (e.g. an undo back into Downloads).
    private readonly ConcurrentDictionary<string, DateTime> _ignoreUntil = new(StringComparer.OrdinalIgnoreCase);

    public WatcherService(IEnumerable<string> folders)
    {
        var all = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(SafeFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Folders = all.Where(Directory.Exists).ToList();
        MissingFolders = all.Where(f => !Directory.Exists(f)).ToList();
    }

    /// <summary>Raised on a background thread with the full path of a new, complete file.</summary>
    public event Action<string>? FileArrived;

    public IReadOnlyList<string> Folders { get; }

    public IReadOnlyList<string> MissingFolders { get; }

    public bool Paused { get; set; }

    public void Start()
    {
        foreach (var folder in Folders)
        {
            // Everything already in the folder is "old" - we only care about new arrivals.
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    _known.TryAdd(file, 0);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not list '{folder}': {ex.Message}");
            }

            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };

            // Browsers download to "x.crdownload" / "x.part" and RENAME at the end, so Renamed matters as much as Created.
            watcher.Created += (_, e) => OnCandidate(e.FullPath);
            // Note: name the sender parameter, otherwise "out _" below would bind to a lambda parameter called "_".
            watcher.Renamed += (sender, e) =>
            {
                _known.TryRemove(e.OldFullPath, out _);
                OnCandidate(e.FullPath);
            };
            watcher.Deleted += (sender, e) => _known.TryRemove(e.FullPath, out _);
            watcher.Error += (_, e) => OnWatcherError(watcher, e.GetException());
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
            Log.Info($"Watching '{folder}'.");
        }
    }

    /// <summary>Tell the watcher that TidyFile itself is about to create <paramref name="path"/>.</summary>
    public void IgnoreNext(string path) => _ignoreUntil[path] = DateTime.UtcNow + IgnoreWindow;

    /// <summary>Undo <see cref="IgnoreNext"/> when the move did not happen after all.</summary>
    public void CancelIgnore(string path) => _ignoreUntil.TryRemove(path, out _);

    private void OnCandidate(string path)
    {
        if (Paused || FileFilter.ShouldIgnore(path))
        {
            return;
        }

        if (_ignoreUntil.TryRemove(path, out var until) && until > DateTime.UtcNow)
        {
            _known.TryAdd(path, 0);
            return;
        }

        if (!_known.TryAdd(path, 0))
        {
            return; // Already handled (e.g. Office re-saving an existing document).
        }

        _ = ProcessAsync(path);
    }

    private async Task ProcessAsync(string path)
    {
        try
        {
            var ready = await FileReadiness.WaitUntilReadyAsync(path, ReadyTimeout, cancellationToken: _cts.Token)
                .ConfigureAwait(false);

            if (!ready)
            {
                // Forget it, so a later rename to the same name (e.g. Firefox finishing) is noticed again.
                _known.TryRemove(path, out _);
                return;
            }

            if (!Paused)
            {
                FileArrived?.Invoke(path);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Log.Error($"Failed while waiting for '{path}'.", ex);
        }
    }

    private static string SafeFullPath(string folder)
    {
        try
        {
            return Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return folder; // Reported later as a missing folder.
        }
    }

    private static void OnWatcherError(FileSystemWatcher watcher, Exception ex)
    {
        // Usually a buffer overflow after a huge burst of changes. Restart so we keep watching.
        Log.Error($"Watcher error on '{watcher.Path}'. Restarting it.", ex);
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception restartEx)
        {
            Log.Error($"Could not restart watcher on '{watcher.Path}'.", restartEx);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _cts.Dispose();
    }
}
