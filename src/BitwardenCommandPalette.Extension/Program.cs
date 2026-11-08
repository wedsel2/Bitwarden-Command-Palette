using System.Diagnostics.CodeAnalysis;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Entry point. Command Palette starts the packaged executable as an
/// out-of-process COM server; any other start exits straight away.
/// </summary>
internal static class Program
{
    [MTAThread]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "ComServer is only IAsyncDisposable; UnsafeDispose in finally releases it even if the host still holds references at exit.")]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            return;
        }

        using var extensionDisposed = new ManualResetEvent(false);
        using var extension = new BitwardenExtension(extensionDisposed);

        var server = new ComServer();
        try
        {
            server.RegisterClass<BitwardenExtension, IExtension>(() => extension);
            server.Start();

            // One extension instance for the life of the process; exit once the host disposes it.
            extensionDisposed.WaitOne();
            server.Stop();
        }
        finally
        {
            server.UnsafeDispose();
        }
    }
}
