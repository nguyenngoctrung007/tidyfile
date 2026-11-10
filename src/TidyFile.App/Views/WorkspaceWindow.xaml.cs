using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using TidyFile.App.ViewModels;
using TidyFile.Core.Diagnostics;

namespace TidyFile.App.Views;

/// <summary>"Create workspace": base folder, preview, two options, and undo of the last setup.</summary>
public partial class WorkspaceWindow : Window
{
    private readonly WorkspaceViewModel _viewModel;
    private readonly Func<WorkspaceViewModel, string?> _create;
    private readonly Func<string?> _undo;

    /// <param name="create">Creates the workspace. Returns an error message, or null on success.</param>
    /// <param name="undo">Undoes the last setup. Returns an error message, or null on success.</param>
    public WorkspaceWindow(WorkspaceViewModel viewModel, Func<WorkspaceViewModel, string?> create, Func<string?> undo)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _create = create;
        _undo = undo;
        DataContext = viewModel;
        TrySetWindowIcon();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the workspace folder (create a new one if needed)" };
        if (Directory.Exists(_viewModel.BaseFolder))
        {
            dialog.InitialDirectory = _viewModel.BaseFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.BaseFolder = dialog.FolderName;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.HasUndo)
        {
            var answer = MessageBox.Show(this, "The previous setup can no longer be undone. Continue?", "TidyFile",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var error = _create(_viewModel);
        if (error is not null)
        {
            MessageBox.Show(this, error, "TidyFile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            $"{_viewModel.UndoDescription}?{Environment.NewLine}{Environment.NewLine}" +
            "Only folders that are still empty are removed. Your files are never deleted.",
            "TidyFile", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var error = _undo();
        if (error is not null)
        {
            MessageBox.Show(this, error, "TidyFile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
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
            Log.Warn($"Could not set the workspace window icon: {ex.Message}");
        }
    }
}
