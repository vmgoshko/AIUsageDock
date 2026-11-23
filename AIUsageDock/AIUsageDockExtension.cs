using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace AIUsageDock;

[Guid("f521ca1e-54ec-4dca-87f6-e1ddba95bf58")]
public sealed partial class AIUsageDockExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly AIUsageDockCommandsProvider _provider = new();

    public AIUsageDockExtension(ManualResetEvent disposed)
    {
        _disposed = disposed;
    }

    public object? GetProvider(ProviderType providerType) =>
        providerType == ProviderType.Commands ? _provider : null;

    public void Dispose()
    {
        _provider.Dispose();
        _disposed.Set();
    }
}
