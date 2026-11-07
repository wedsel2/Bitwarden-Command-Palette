using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// The COM class Command Palette creates. Its GUID must match the class ID in
/// Package.appxmanifest.
/// </summary>
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "The COM class Command Palette activates; kept public as in Microsoft's template until the spike confirms internal works.")]
[ComVisible(true)]
[Guid("5916515F-0C56-417F-B2C7-9DA5DF4C6758")]
[ComDefaultInterface(typeof(IExtension))]
public sealed partial class BitwardenExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly BitwardenCommandsProvider _provider = new();

    /// <summary>Creates the extension.</summary>
    /// <param name="disposed">Set when the host disposes the extension, which ends the process.</param>
    public BitwardenExtension(ManualResetEvent disposed)
    {
        _disposed = disposed;
    }

    /// <inheritdoc />
    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
        _disposed.Set();
    }
}
