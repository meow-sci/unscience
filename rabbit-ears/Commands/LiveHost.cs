using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using RabbitEars.Live;
using RabbitEars.Web;

namespace RabbitEars.Commands;

/// <summary>Shared tail of `live` and `play`: web server, start, wait for Ctrl-C / SIGTERM, orderly shutdown.</summary>
public static class LiveHost
{
    public static int Run(LiveSession session, int port, bool openBrowser)
    {
        using var stopRequested = new ManualResetEventSlim();
        using ShutdownSignals signals = ShutdownSignals.Hook(stopRequested.Set);
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), io);                // the mux fans out per channel while web requests come in

        using var web = new WebServer(session, port);
        session.Start();
        Console.WriteLine($"rabbit-ears: open {web.Url}   (Ctrl-C to stop)");
        if (openBrowser) OpenBrowser(web.Url);

        while (!stopRequested.Wait(500))
            if (session.Pipeline.Failure is not null) break;
        Console.WriteLine("rabbit-ears: shutting down");
        session.Dispose();
        return session.Pipeline.Failure is null ? 0 : 1;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", url)?.Dispose();
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            else Process.Start("xdg-open", url)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine("rabbit-ears: could not open a browser; open the URL yourself");
        }
    }
}

/// <summary>Ctrl-C, SIGTERM and SIGHUP all ask for the same orderly stop (so child processes are never orphaned).</summary>
public sealed class ShutdownSignals : IDisposable
{
    private readonly PosixSignalRegistration[] _registrations;
    private readonly ConsoleCancelEventHandler _onCancel;

    private ShutdownSignals(Action stop)
    {
        _onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop();
        };
        Console.CancelKeyPress += _onCancel;
        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            stop();
        }
        _registrations = OperatingSystem.IsWindows()
            ? new[] { PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal) }
            : new[] { PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal), PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal) };
    }

    public static ShutdownSignals Hook(Action stop) => new(stop);

    public void Dispose()
    {
        Console.CancelKeyPress -= _onCancel;
        foreach (PosixSignalRegistration registration in _registrations) registration.Dispose();
    }
}
