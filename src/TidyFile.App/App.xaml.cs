using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TidyFile.App.Services;
using TidyFile.App.ViewModels;
using TidyFile.App.Views;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.History;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.App;

/// <summary>Start-up and wiring. The app has no main window; it lives in the tray.</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TidyFile.SingleInstance";

    private Mutex? _singleInstance;
    private bool _ownsMutex;

    // Created in OnStartup; non-null for the rest of the app's life.
    private AppPaths _paths = null!;
    private AppSettings _settings = null!;
    private RulesProvider _rules = null!;
    private MoveService _mover = null!;
    private TrayIcon _tray = null!;
    private PopupManager _popups = null!;

    private WatcherService? _watcher;
    private SettingsWindow? _settingsWindow;
    private bool _paused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("TidyFile is already running. Look for its icon in the notification area.",
                "TidyFile", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _paths = new AppPaths();
        Directory.CreateDirectory(_paths.Root);
        Log.FilePath = _paths.LogFile;
        Log.Info("TidyFile starting.");
        HookUnhandledExceptions();

        _settings = JsonFile.LoadOrCreate(_paths.SettingsFile, () => new AppSettings(), out var settingsWarning);

        var history = new HistoryStore(_paths.HistoryFile);
        _mover = new MoveService(history);
        _rules = new RulesProvider(_paths.RulesFile);
        _tray = new TrayIcon(history, _paths);
        _popups = new PopupManager(_rules, _mover, _settings, _tray);

        _rules.ProblemsFound += problems =>
        {
            foreach (var problem in problems)
            {
                Log.Warn(problem);
            }

            _tray.ShowError("Problem in your rules", problems[0]);
        };

        _tray.PauseChanged += OnPauseChanged;
        _tray.UndoRequested += OnUndoRequested;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += OnExitRequested;

        _rules.GetEngine(); // Creates rules.json on first run and reports problems early.
        StartWatcher();

        if (settingsWarning is not null)
        {
            _tray.ShowError("Problem in settings.json", settingsWarning);
        }
        else if (!ReportMissingFolders())
        {
            var watched = string.Join(", ", _watcher!.Folders.Select(PathTokens.Shorten));
            _tray.ShowInfo("TidyFile is running", $"Watching {watched}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("TidyFile exiting.");
        _watcher?.Dispose();
        _tray?.Dispose();

        if (_ownsMutex)
        {
            _singleInstance?.ReleaseMutex();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>(Re)creates the folder watcher from the current settings.</summary>
    private void StartWatcher()
    {
        _watcher?.Dispose();

        var watcher = new WatcherService(_settings.WatchFolders.Select(PathTokens.Expand)) { Paused = _paused };
        watcher.FileArrived += path => Dispatcher.InvokeAsync(() => _popups.Enqueue(path));

        // Files TidyFile writes itself must not trigger a popup.
        _mover.ExpectIncoming = watcher.IgnoreNext;
        _mover.CancelIncoming = watcher.CancelIgnore;

        watcher.Start();
        _watcher = watcher;
    }

    /// <summary>Shows a notification if some watched folders don't exist. Returns true if it did.</summary>
    private bool ReportMissingFolders()
    {
        if (_watcher is null || _watcher.MissingFolders.Count == 0)
        {
            return false;
        }

        _tray.ShowError("Some folders were not found", string.Join(", ", _watcher.MissingFolders));
        return true;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        bool startWithWindows;
        try
        {
            startWithWindows = StartupRegistration.IsEnabled();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the start-with-Windows setting.", ex);
            startWithWindows = false;
        }

        var rulesDocument = _rules.LoadDocument(out var rulesWarning);
        var viewModel = new SettingsViewModel(_settings, rulesDocument, startWithWindows, rulesWarning);
        _settingsWindow = new SettingsWindow(viewModel, SaveSettings, _paths.Root);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Applies and saves the Settings window's edits. Returns an error message, or null.</summary>
    private string? SaveSettings(SettingsViewModel viewModel)
    {
        // 1. Write both files. If either fails, nothing live has changed yet.
        var updated = new AppSettings();
        try
        {
            viewModel.ApplyTo(updated);
            JsonFile.Save(_paths.SettingsFile, updated);
            _rules.Save(viewModel.ToRulesDocument());
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed.", ex);
            return ex.Message;
        }

        // 2. The files are saved: make the running app match them straight away.
        var foldersChanged = !_settings.WatchFolders.SequenceEqual(updated.WatchFolders, StringComparer.OrdinalIgnoreCase);
        viewModel.ApplyTo(_settings);

        if (foldersChanged)
        {
            StartWatcher();
            Log.Info("Watched folders changed: " + string.Join(", ", _settings.WatchFolders));
            ReportMissingFolders();
        }

        Log.Info("Settings saved.");

        // 3. The registry is separate: a failure here only warns, the rest is already saved and live.
        try
        {
            if (StartupRegistration.IsEnabled() != viewModel.StartWithWindows)
            {
                StartupRegistration.SetEnabled(viewModel.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the start-with-Windows setting.", ex);
            _tray.ShowError("Could not change start-up setting", ex.Message);
        }

        return null;
    }

    private void OnExitRequested()
    {
        // Ask about unsaved settings now: once shutdown starts, the window can't cancel its close.
        if (_settingsWindow?.ConfirmClose() == false)
        {
            return;
        }

        Shutdown();
    }

    private void OnPauseChanged(bool paused)
    {
        _paused = paused;
        if (_watcher is not null)
        {
            _watcher.Paused = paused;
        }

        if (paused)
        {
            _popups.Clear();
        }

        Log.Info(paused ? "Paused." : "Resumed.");
    }

    private void OnUndoRequested(HistoryEntry entry)
    {
        try
        {
            var undo = _mover.Undo(entry);
            Log.Info($"Undid move: '{undo.From}' -> '{undo.To}'.");
            _tray.ShowInfo("Undone", $"{Path.GetFileName(undo.To)} is back in {PathTokens.Shorten(Path.GetDirectoryName(undo.To) ?? "")}");
        }
        catch (Exception ex)
        {
            Log.Error("Undo failed.", ex);
            _tray.ShowError("Could not undo", ex.Message);
        }
    }

    private void HookUnhandledExceptions()
    {
        // A tray app should keep running after an unexpected error; log it instead of crashing.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };

        // The tray menu runs on WinForms; without this its errors would show the WinForms crash dialog.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
            Log.Error("Unhandled tray exception.", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }
}
