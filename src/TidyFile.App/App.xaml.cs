using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TidyFile.App.Services;
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
    private TrayIcon? _tray;
    private WatcherService? _watcher;

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

        var paths = new AppPaths();
        Directory.CreateDirectory(paths.Root);
        Log.FilePath = paths.LogFile;
        Log.Info("TidyFile starting.");
        HookUnhandledExceptions();

        var settings = JsonFile.LoadOrCreate(paths.SettingsFile, () => new AppSettings(), out var settingsWarning);

        var history = new HistoryStore(paths.HistoryFile);
        var mover = new MoveService(history);
        var rules = new RulesProvider(paths.RulesFile);

        var tray = new TrayIcon(history, paths);
        var watcher = new WatcherService(settings.WatchFolders.Select(PathTokens.Expand));
        var popups = new PopupManager(rules, mover, settings, tray);
        _tray = tray;
        _watcher = watcher;

        // Files TidyFile writes itself must not trigger a popup.
        mover.ExpectIncoming = watcher.IgnoreNext;
        mover.CancelIncoming = watcher.CancelIgnore;

        rules.ProblemsFound += problems =>
        {
            foreach (var problem in problems)
            {
                Log.Warn(problem);
            }

            tray.ShowError("Problem in rules.json", problems[0]);
        };

        watcher.FileArrived += path => Dispatcher.InvokeAsync(() => popups.Enqueue(path));

        tray.PauseChanged += paused =>
        {
            watcher.Paused = paused;
            if (paused)
            {
                popups.Clear();
            }

            Log.Info(paused ? "Paused." : "Resumed.");
        };

        tray.UndoRequested += entry =>
        {
            try
            {
                var undo = mover.Undo(entry);
                Log.Info($"Undid move: '{undo.From}' -> '{undo.To}'.");
                tray.ShowInfo("Undone", $"{Path.GetFileName(undo.To)} is back in {PathTokens.Shorten(Path.GetDirectoryName(undo.To) ?? "")}");
            }
            catch (Exception ex)
            {
                Log.Error("Undo failed.", ex);
                tray.ShowError("Could not undo", ex.Message);
            }
        };

        tray.ExitRequested += Shutdown;

        rules.GetEngine(); // Creates rules.json on first run and reports problems early.
        watcher.Start();

        if (settingsWarning is not null)
        {
            tray.ShowError("Problem in settings.json", settingsWarning);
        }
        else if (watcher.MissingFolders.Count > 0)
        {
            tray.ShowError("Some folders were not found", string.Join(", ", watcher.MissingFolders));
        }
        else
        {
            var watched = string.Join(", ", watcher.Folders.Select(PathTokens.Shorten));
            tray.ShowInfo("TidyFile is running", $"Watching {watched}");
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
