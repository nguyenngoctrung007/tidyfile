using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using TidyFile.App.ViewModels;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.Paths;

namespace TidyFile.App.Views;

/// <summary>Settings window: General, Watched folders, Rules.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Func<SettingsViewModel, string?> _save;
    private readonly string _dataFolder;
    private bool _closeConfirmed;

    /// <param name="viewModel">Copies of the current settings and rules.</param>
    /// <param name="save">Applies and saves the edits. Returns an error message, or null on success.</param>
    /// <param name="dataFolder">%LOCALAPPDATA%\TidyFile, for the "Open data folder" link.</param>
    public SettingsWindow(SettingsViewModel viewModel, Func<SettingsViewModel, string?> save, string dataFolder)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _save = save;
        _dataFolder = dataFolder;
        DataContext = viewModel;
        TrySetWindowIcon();

        if (viewModel.RulesWarning is not null)
        {
            Tabs.SelectedItem = RulesTab; // Make sure the "rules.json couldn't be read" banner is seen.
        }
    }

    /// <summary>
    /// Asks "Discard changes?" if there are unsaved edits. Returns true if the window may close.
    /// Used by the tray's Exit too, because closing during app shutdown can't be cancelled.
    /// </summary>
    public bool ConfirmClose()
    {
        if (_closeConfirmed || !_viewModel.IsDirty)
        {
            _closeConfirmed = true;
            return true;
        }

        var answer = MessageBox.Show(this, "You have unsaved changes. Discard them?", "TidyFile",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        _closeConfirmed = answer == MessageBoxResult.Yes;
        return _closeConfirmed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    // ----- Folders -----

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to watch" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!_viewModel.AddFolder(dialog.FolderName))
        {
            MessageBox.Show(this, "That folder is already in the list.", "TidyFile",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedFolder();

    // ----- Rules -----

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddRule();
        RulesList.ScrollIntoView(_viewModel.SelectedRule);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        var rule = _viewModel.SelectedRule;
        if (rule is null)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Delete the rule \"{rule.DisplayName}\"?", "TidyFile",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _viewModel.DeleteSelectedRule();
        }
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedRule(-1);

    private void MoveRuleDown_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedRule(+1);

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var rule = _viewModel.SelectedRule;
        if (rule is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = $"Where should \"{rule.DisplayName}\" files go?" };
        var current = PathTokens.Expand(rule.Destination);
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            rule.Destination = PathTokens.Tokenize(dialog.FolderName);
        }
    }

    // ----- Footer -----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var problems = _viewModel.Validate();
        if (problems.Count > 0)
        {
            Tabs.SelectedItem = RulesTab;
            _viewModel.SelectedRule = _viewModel.Rules.FirstOrDefault(r => r.HasProblem);
            MessageBox.Show(this,
                "Please fix these rules first (or untick \"Enabled\"):" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "• " + p)),
                "TidyFile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var duplicates = _viewModel.DuplicateRuleNames();
        if (duplicates.Count > 0)
        {
            var answer = MessageBox.Show(this,
                "Several rules share a name, so the popup and the tester can't tell them apart:" +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, duplicates.Select(d => "• " + d)) +
                Environment.NewLine + Environment.NewLine + "Save anyway?",
                "TidyFile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                Tabs.SelectedItem = RulesTab;
                return;
            }
        }

        if (_viewModel.Folders.Count == 0)
        {
            var answer = MessageBox.Show(this,
                "No folders are watched, so TidyFile will not suggest anything. Save anyway?",
                "TidyFile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var error = _save(_viewModel);
        if (error is not null)
        {
            MessageBox.Show(this, "Could not save the settings:" + Environment.NewLine + error, "TidyFile",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _closeConfirmed = true; // Saved, nothing to discard.
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_dataFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{_dataFolder}'.", ex);
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tidyfile.ico");
            if (File.Exists(icoPath))
            {
                Icon = BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not set the settings window icon: {ex.Message}");
        }
    }
}
