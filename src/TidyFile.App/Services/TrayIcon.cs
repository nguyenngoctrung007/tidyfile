using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.History;
using TidyFile.Core.Paths;
using TidyFile.Core.Storage;

namespace TidyFile.App.Services;

/// <summary>The notification-area icon and its right-click menu (WinForms NotifyIcon, no extra packages).</summary>
public sealed class TrayIcon : IDisposable
{
    private const int RecentMovesShown = 10;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _recentItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly HistoryStore _history;
    private readonly AppPaths _paths;

    public TrayIcon(HistoryStore history, AppPaths paths)
    {
        _history = history;
        _paths = paths;

        _pauseItem = new ToolStripMenuItem("Pause watching") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _notifyIcon!.Text = _pauseItem.Checked ? "TidyFile (paused)" : "TidyFile";
            PauseChanged?.Invoke(_pauseItem.Checked);
        };

        _recentItem = new ToolStripMenuItem("Recent moves (click to undo)");

        _startupItem = new ToolStripMenuItem("Start with Windows") { Checked = SafeIsStartupEnabled() };
        _startupItem.Click += (_, _) => ToggleStartup();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_recentItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Edit rules…", null, (_, _) => OpenWithShell(_paths.RulesFile));
        _menu.Items.Add("Edit settings…", null, (_, _) => OpenWithShell(_paths.SettingsFile));
        _menu.Items.Add("Open TidyFile data folder", null, (_, _) => OpenWithShell(_paths.Root));
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        _menu.Opening += (_, _) => RebuildRecentMoves();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "TidyFile",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    public event Action<bool>? PauseChanged;

    public event Action<HistoryEntry>? UndoRequested;

    public event Action? ExitRequested;

    public void ShowInfo(string title, string text) => ShowBalloon(title, text, ToolTipIcon.Info);

    public void ShowError(string title, string text) => ShowBalloon(title, text, ToolTipIcon.Error);

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        // Windows shows these as normal toast notifications on Windows 10/11.
        _notifyIcon.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text, icon);
    }

    private void RebuildRecentMoves()
    {
        _recentItem.DropDownItems.Clear();

        var moves = _history.RecentUndoableMoves(RecentMovesShown);
        if (moves.Count == 0)
        {
            _recentItem.DropDownItems.Add(new ToolStripMenuItem("(nothing yet)") { Enabled = false });
            return;
        }

        foreach (var move in moves)
        {
            var folder = PathTokens.Shorten(Path.GetDirectoryName(move.To) ?? "");
            var label = $"{move.Timestamp.LocalDateTime:HH:mm}  {Path.GetFileName(move.To)}  →  {folder}";
            var item = new ToolStripMenuItem(EscapeMenuText(label)) { ToolTipText = $"Undo: move back to {move.From}" };
            item.Click += (_, _) => UndoRequested?.Invoke(move);
            _recentItem.DropDownItems.Add(item);
        }
    }

    private void ToggleStartup()
    {
        try
        {
            var enable = !_startupItem.Checked;
            StartupRegistration.SetEnabled(enable);
            _startupItem.Checked = enable;
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the start-with-Windows setting.", ex);
            ShowError("Could not change start-up setting", ex.Message);
        }
    }

    private static bool SafeIsStartupEnabled()
    {
        try
        {
            return StartupRegistration.IsEnabled();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the start-with-Windows setting.", ex);
            return false;
        }
    }

    private void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (File.Exists(path))
        {
            // No app is associated with .json files: fall back to Notepad.
            Log.Warn($"Shell open failed for '{path}' ({ex.Message}); using Notepad.");
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{path}'.", ex);
            ShowError("Could not open", ex.Message);
        }
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            // Prefer the .ico file at the tray size (sharper than the exe's 32px icon).
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tidyfile.ico");
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath, SystemInformation.SmallIconSize);
            }

            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not load app icon: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    // In menus "&" marks a keyboard shortcut; double it to show a real ampersand.
    private static string EscapeMenuText(string text) => text.Replace("&", "&&");

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
