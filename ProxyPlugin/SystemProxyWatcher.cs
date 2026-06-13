using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using ProxyPlugin.Core;

namespace ProxyPlugin;

/// <summary>
/// Background watcher that blocks on <c>RegNotifyChangeKeyValue</c> for the WinINET
/// Internet Settings key and invokes a callback whenever the system proxy changes,
/// so the resolver can refresh without waiting for its TTL.
/// </summary>
public sealed class SystemProxyWatcher : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    [Flags]
    private enum Filter : uint
    {
        Name = 0x1,
        Attributes = 0x2,
        LastSet = 0x4,
        Security = 0x8,
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey, bool watchSubtree, Filter notifyFilter, SafeWaitHandle hEvent, bool asynchronous);

    private readonly IInterceptorLog _log;
    private readonly Action _onChange;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEvent _changeEvent = new(false);
    private Thread? _thread;

    public SystemProxyWatcher(Action onChange, IInterceptorLog log)
    {
        _onChange = onChange;
        _log = log;
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "ProxyPlugin.ProxyWatcher" };
        _thread.Start();
    }

    private void Loop()
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (regKey == null)
            {
                _log.Info("ProxyWatcher: Internet Settings key missing; relying on TTL refresh.");
                return;
            }

            var handle = regKey.Handle;
            var waits = new WaitHandle[] { _cts.Token.WaitHandle, _changeEvent };
            while (!_cts.IsCancellationRequested)
            {
                _changeEvent.Reset();
                int rc = RegNotifyChangeKeyValue(handle, false, Filter.LastSet | Filter.Name, _changeEvent.SafeWaitHandle, true);
                if (rc != 0)
                {
                    _log.Info($"ProxyWatcher: RegNotifyChangeKeyValue failed ({rc}); falling back to TTL refresh.");
                    break;
                }

                if (WaitHandle.WaitAny(waits) == 0)
                    break; // cancellation requested

                _onChange();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ProxyWatcher loop crashed.");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _changeEvent.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _changeEvent.Dispose();
        _cts.Dispose();
    }
}
