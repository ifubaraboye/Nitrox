using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Nitrox.Launcher.Models;

namespace Nitrox.Launcher.ViewModels.Abstract;

internal abstract class RoutableViewModelBase : ViewModelBase
{
    /// <summary>
    ///     Updates the current view container to show a different view, as is known by the TViewModel type.
    /// </summary>
    protected void ChangeView<TViewModel>(TViewModel viewModel) where TViewModel : RoutableViewModelBase
    {
        SendOnUiThread(new ShowViewMessage
        {
            ViewModel = viewModel
        });
    }

    protected void ChangeViewToPrevious()
    {
        SendOnUiThread(new ShowPreviousViewMessage());
    }

    protected void ChangeViewToPrevious<T>() where T : RoutableViewModelBase
    {
        SendOnUiThread(new ShowPreviousViewMessage(typeof(T)));
    }

    private static void SendOnUiThread<TMessage>(TMessage message) where TMessage : class
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            WeakReferenceMessenger.Default.Send(message);
        }
        else
        {
            // View navigation touches Avalonia controls and must always happen on the UI thread.
            Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(message));
        }
    }

    /// <summary>
    ///     Loads content that the view should show. While the returned task is running a loading indicator will be visible.
    /// </summary>
    internal virtual Task ViewContentLoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal virtual Task ViewContentUnloadAsync() => Task.CompletedTask;
}
