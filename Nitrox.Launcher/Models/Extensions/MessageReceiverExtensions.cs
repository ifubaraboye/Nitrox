using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Nitrox.Launcher.Models.Utils;
using Nitrox.Model.Logger;

namespace Nitrox.Launcher.Models.Extensions;

public static class MessageReceiverExtensions
{
    public static void RegisterMessageListener<T, TReceiver>(this TReceiver receiver, Func<T, TReceiver, Task> asyncFunc) where T : class where TReceiver : IMessageReceiver
    {
        if (WeakReferenceMessenger.Default.IsRegistered<T>(receiver))
        {
            WeakReferenceMessenger.Default.Unregister<T>(receiver);
        }
        // WeakReferenceMessenger handlers return void. Passing an async lambda directly makes it 'async void', which
        // crashes the whole process when the handler throws (unhandled exception on a thread pool thread, no crash
        // report, no log line). Catch and log instead.
        WeakReferenceMessenger.Default.Register<T>(receiver, (_, message) => RunSafely(asyncFunc, message, receiver));
    }

    public static void RegisterMessageListener<T, TReceiver>(this TReceiver receiver, Action<T, TReceiver> action) where T : class where TReceiver : IMessageReceiver
    {
        if (WeakReferenceMessenger.Default.IsRegistered<T>(receiver))
        {
            WeakReferenceMessenger.Default.Unregister<T>(receiver);
        }
        WeakReferenceMessenger.Default.Register<T>(receiver, (_, message) =>
        {
            try
            {
                action(message, receiver);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Message handler '{typeof(T).Name}' failed");
                LaunchDiagnostics.Write($"Message handler '{typeof(T).Name}' failed", ex);
            }
        });
    }

    private static async void RunSafely<T, TReceiver>(Func<T, TReceiver, Task> asyncFunc, T message, TReceiver receiver) where T : class where TReceiver : IMessageReceiver
    {
        try
        {
            await asyncFunc(message, receiver);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Async message handler '{typeof(T).Name}' failed");
            LaunchDiagnostics.Write($"Async message handler '{typeof(T).Name}' failed", ex);
        }
    }
}
