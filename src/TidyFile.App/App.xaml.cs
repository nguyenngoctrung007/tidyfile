using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using TidyFile.Core.Workspace;

namespace TidyFile.App;

/// <summary>Start-up and wiring. The app has no main window; it lives in the tray.</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TidyFile.SingleInstance";
    private static readonly TimeSpan MoveFinishTimeout = TimeSpan.FromMinutes(1);

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
    private WorkspaceWindow? _workspaceWindow;
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

        string? settingsWarning;
        try
        {
            _settings = JsonFile.LoadOrCreate(_paths.SettingsFile, () => new AppSettings(), out settingsWarning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // E.g. locked by another program. Run with the defaults rather than not starting at all.
            Log.Error("Could not read settings.json; using defaults.", ex);
            _settings = new AppSettings();
            settingsWarning = $"settings.json could not be read ({ex.Message}); using defaults.";
        }

        var history = new HistoryStore(_paths.HistoryFile);
        _mover = new MoveService(history);
        _rules = new RulesProvider(_paths.RulesFile);
        _tray = new TrayIcon(history, _paths);
        _popups = new PopupManager(_rules, _mover, history, _settings, _tray);

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
        _tray.SettingsRequested += () => OpenSettings();
        _tray.WorkspaceRequested += OpenWorkspace;
        _popups.EditRuleRequested += OpenSettings;
        _popups.UndoRequested += OnUndoRequested;
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

        // Let a running move finish: cutting off a cross-drive copy leaves a partial file at the destination.
        if (_mover is not null && !_mover.WaitUntilIdle(MoveFinishTimeout))
        {
            Log.Warn("A file move was still running at exit.");
        }

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

    /// <param name="ruleName">Rule to select on the Rules tab (e.g. one just added by "Always do this").</param>
    private void OpenSettings(string? ruleName = null)
    {
        if (_settingsWindow is not null && ruleName is not null && !_settingsWindow.ShowRule(ruleName))
        {
            // The open window was loaded before the rule was added. Reload it if that loses nothing.
            if (!_settingsWindow.HasUnsavedChanges)
            {
                _settingsWindow.Close();
            }
            else
            {
                _settingsWindow.Activate();
                MessageBox.Show(_settingsWindow,
                    $"The rule \"{ruleName}\" was added, but this window was opened before that and has unsaved changes. " +
                    "Saving them will remove the new rule. Cancel, then open Settings again to see it.",
                    "TidyFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

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

        if (ruleName is not null)
        {
            _settingsWindow.ShowRule(ruleName);
        }
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

    private const string CloseSettingsFirst =
        "Close the Settings window first, so its unsaved copy doesn't overwrite the workspace changes.";

    private void OpenWorkspace()
    {
        // Settings edits copies of the rules and settings; saving it later would undo what the workspace changed.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            MessageBox.Show(_settingsWindow, CloseSettingsFirst, "TidyFile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_workspaceWindow is not null)
        {
            _workspaceWindow.Activate();
            return;
        }

        var viewModel = new WorkspaceViewModel(
            _settings, LoadRulesForEdit(), WorkspaceRecord.Load(_paths.WorkspaceSetupFile), DateTime.Now.Year);
        _workspaceWindow = new WorkspaceWindow(viewModel, CreateWorkspace, UndoWorkspace);
        _workspaceWindow.Closed += (_, _) => _workspaceWindow = null;
        _workspaceWindow.Show();
        _workspaceWindow.Activate();
    }

    /// <summary>Creates the workspace and saves what changed. Returns an error message, or null.</summary>
    private string? CreateWorkspace(WorkspaceViewModel viewModel)
    {
        if (_settingsWindow is not null)
        {
            return CloseSettingsFirst;
        }

        // Work on copies; the live settings change only after settings.json is saved.
        var settings = CopyOfSettings();
        var rules = LoadRulesForEdit();
        WorkspaceApplyResult result;
        try
        {
            var plan = WorkspacePlanner.Plan(WorkspaceTemplate.Developer, viewModel.BaseFolder, settings, rules, DateTime.Now.Year);
            result = WorkspaceSetup.Apply(plan, viewModel.Options, settings, rules, DateTimeOffset.Now,
                beforeWriteFile: path => _watcher?.IgnoreNext(path)); // The base may be a watched folder.
        }
        catch (Exception ex)
        {
            Log.Error("Creating the workspace failed.", ex);
            return ex.Message;
        }

        var record = result.Record;
        var notes = new List<string>(result.Notes);

        if (result.SettingsChanged && !TrySaveSettings(settings, notes))
        {
            record.AddedWatchFolder = null; // Not applied, so nothing to undo.
        }

        if (result.RulesChanged && rules is not null && !TrySaveRules(rules, notes))
        {
            record.RedirectedRuleName = record.RedirectedFrom = record.RedirectedTo = null;
            record.AddedRuleName = record.AddedRuleDestination = null;
        }

        // An empty record (nothing was missing) must not replace the previous setup's undo.
        if (!record.IsEmpty)
        {
            try
            {
                record.Save(_paths.WorkspaceSetupFile);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save the workspace undo record.", ex);
                notes.Add($"Undo won't be available ({ex.Message}).");
            }
        }

        Log.Info($"Workspace in '{record.BaseFolder}': {record.CreatedFolders.Count} folders created" +
                 (result.Error is null ? "." : $", stopped: {result.Error}"));

        if (result.Error is null)
        {
            var text = record.BaseFolder + Environment.NewLine + string.Join(" ", notes.Prepend("Click to open."));
            _tray.ShowInfo(result.Title, text, () => OpenFolder(record.BaseFolder));
        }
        else
        {
            _tray.ShowError(result.Title, result.Error);
        }

        return result.Error is null
            ? null
            : $"Stopped part-way: {result.Error}{Environment.NewLine}{Environment.NewLine}" +
              "What was created so far can be undone from this window.";
    }

    /// <summary>Undoes the last workspace setup and saves what changed. Returns an error message, or null.</summary>
    private string? UndoWorkspace()
    {
        if (_settingsWindow is not null)
        {
            return CloseSettingsFirst;
        }

        var record = WorkspaceRecord.Load(_paths.WorkspaceSetupFile);
        if (record is null)
        {
            return "Nothing to undo: the record of the last setup is missing.";
        }

        var settings = CopyOfSettings();
        var rules = LoadRulesForEdit();
        WorkspaceUndoResult result;
        try
        {
            result = WorkspaceSetup.Undo(record, settings, rules);
        }
        catch (Exception ex)
        {
            Log.Error("Undoing the workspace setup failed.", ex);
            return ex.Message;
        }

        var problems = new List<string>();
        if (result.SettingsChanged)
        {
            TrySaveSettings(settings, problems);
        }

        if (result.RulesChanged && rules is not null)
        {
            TrySaveRules(rules, problems);
        }

        if (problems.Count > 0)
        {
            return string.Join(Environment.NewLine, problems); // Keep the record so Undo can be tried again.
        }

        WorkspaceRecord.Delete(_paths.WorkspaceSetupFile);
        Log.Info($"Workspace setup in '{record.BaseFolder}' undone; kept: {string.Join(", ", result.Kept)}");
        _tray.ShowInfo("Workspace setup undone",
            result.Kept.Count == 0 ? "Everything it created was removed." : "Kept: " + string.Join(", ", result.Kept));
        return null;
    }

    /// <summary>Saves settings.json and makes the running app match it. Adds a note and returns false on failure.</summary>
    private bool TrySaveSettings(AppSettings settings, List<string> notes)
    {
        try
        {
            JsonFile.Save(_paths.SettingsFile, settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.json.", ex);
            notes.Add($"Could not save settings.json ({ex.Message}).");
            return false;
        }

        _settings.WatchFolders = settings.WatchFolders;
        StartWatcher();
        Log.Info("Watched folders changed: " + string.Join(", ", _settings.WatchFolders));
        ReportMissingFolders();
        return true;
    }

    private bool TrySaveRules(RulesDocument rules, List<string> notes)
    {
        try
        {
            _rules.Save(rules);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not save rules.json.", ex);
            notes.Add($"Could not save rules.json ({ex.Message}).");
            return false;
        }
    }

    /// <summary>A deep copy, so edits don't reach the live settings until they are saved.</summary>
    private AppSettings CopyOfSettings() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings, JsonFile.Options), JsonFile.Options)!;

    /// <summary>The rules for editing, or null if rules.json can't be read (so it is never overwritten with defaults).</summary>
    private RulesDocument? LoadRulesForEdit()
    {
        try
        {
            var document = _rules.LoadDocument(out var warning);
            return warning is null ? document : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read rules.json: {ex.Message}");
            return null;
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{path}'.", ex);
            _tray.ShowError("Could not open folder", ex.Message);
        }
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

    // async void: an event handler whose errors are all caught below.
    private async void OnUndoRequested(HistoryEntry entry)
    {
        try
        {
            var undo = await _mover.UndoAsync(entry); // Resumes on the UI thread.
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
