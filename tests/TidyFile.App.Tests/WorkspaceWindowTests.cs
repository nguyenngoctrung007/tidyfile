using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TidyFile.App.ViewModels;
using TidyFile.App.Views;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.App.Tests;

/// <summary>Opens the real window off-screen on an STA thread (WPF needs one).</summary>
public class WorkspaceWindowTests
{
    private static WorkspaceViewModel ViewModel(WorkspaceRecord? lastSetup = null) =>
        new(new AppSettings(), DefaultRules.Create(), lastSetup, 2026,
            Path.Combine(Path.GetTempPath(), "tidyfile-ws-window", Guid.NewGuid().ToString("N")));

    private static WorkspaceWindow Open(WorkspaceViewModel viewModel)
    {
        var window = new WorkspaceWindow(viewModel, _ => null, () => null)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        window.Show();
        DoEvents(); // Let bindings resolve.
        return window;
    }

    [Fact]
    public void Cancel_button_closes_the_window() => OnSta(() =>
    {
        var window = Open(ViewModel());
        var closed = false;
        window.Closed += (_, _) => closed = true;

        ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.True(closed);
    });

    [Fact]
    public void Undo_button_shows_which_setup_it_undoes() => OnSta(() =>
    {
        var viewModel = ViewModel(new WorkspaceRecord { BaseFolder = @"D:\Workspace", CreatedAt = DateTimeOffset.Now });
        var window = Open(viewModel);

        Assert.Equal(viewModel.UndoDescription, ((Button)window.FindName("UndoButton")).Content);

        window.Close();
    });

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
