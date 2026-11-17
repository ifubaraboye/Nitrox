using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Nitrox.Launcher.Models.Design;

namespace Nitrox.Launcher.Models.Utils;

public static class LauncherNotifier
{
    public static void Error(string message)
    {
        Send(new NotificationItem(message, NotificationType.Error));
    }

    public static void Info(string message)
    {
        Send(new NotificationItem(message));
    }

    public static void Warning(string message)
    {
        Send(new NotificationItem(message, NotificationType.Warning));
    }

    public static void Success(string message)
    {
        Send(new NotificationItem(message, NotificationType.Success));
    }

    [Conditional("DEBUG")]
    public static void Debug(string message, [CallerMemberName] string memberName = "")
    {
        Send(new NotificationItem($"Error in '{memberName}':{Environment.NewLine}{message}", NotificationType.Success));
    }

    /// <summary>
    ///     Notifications update Avalonia controls, so they must be raised on the UI thread. Notifications are often sent
    ///     from background threads (game setup runs in a Task), which can corrupt or crash the UI if not marshalled.
    /// </summary>
    private static void Send(NotificationItem item)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                WeakReferenceMessenger.Default.Send(new NotificationAddMessage(item));
            }
            else
            {
                Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(new NotificationAddMessage(item)));
            }
        }
        catch (Exception ex)
        {
            LaunchDiagnostics.Write($"Failed to send notification '{item.Message}'", ex);
        }
    }
}
