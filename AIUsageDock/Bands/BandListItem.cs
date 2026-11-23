using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AIUsageDock.Bands;

/// <summary>
/// Dock band item whose property notifications survive a Command Palette restart.
/// </summary>
/// <remarks>
/// The extension process outlives the host: a restarted Command Palette reconnects to
/// the same band objects and subscribes again, while the previous host's handler stays
/// in the invocation list. SDK 0.9 raises <c>PropChanged</c> as one multicast call, so
/// the dead handler throws RPC_E_DISCONNECTED first and the live Dock never hears about
/// the update (microsoft/PowerToys#50483, fixed only in later SDKs). This item
/// re-implements <see cref="INotifyPropChanged"/>, delivers to each subscriber on its
/// own and drops subscribers whose host is gone.
/// </remarks>
internal sealed partial class BandListItem : ListItem, INotifyPropChanged
{
    private const int RpcDisconnected = unchecked((int)0x80010108);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);

    private readonly object _gate = new();
    private TypedEventHandler<object, IPropChangedEventArgs>[] _handlers = [];

    public BandListItem(ICommand command) : base(command)
    {
    }

    public new event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged
    {
        add
        {
            if (value is null) return;
            lock (_gate) _handlers = [.. _handlers, value];
        }
        remove
        {
            if (value is null) return;
            lock (_gate)
            {
                var index = Array.IndexOf(_handlers, value);
                if (index >= 0) _handlers = [.. _handlers[..index], .. _handlers[(index + 1)..]];
            }
        }
    }

    internal int SubscriberCount => Volatile.Read(ref _handlers).Length;

    public override string Title
    {
        get => base.Title;
        set { base.Title = value; Raise(nameof(Title)); }
    }

    public override string Subtitle
    {
        get => base.Subtitle;
        set { base.Subtitle = value; Raise(nameof(Subtitle)); }
    }

    public override IIconInfo? Icon
    {
        get => base.Icon;
        set { base.Icon = value; Raise(nameof(Icon)); }
    }

    private void Raise(string propertyName)
    {
        var args = new PropChangedEventArgs(propertyName);
        foreach (var handler in Volatile.Read(ref _handlers))
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception) when (exception.HResult is RpcDisconnected or RpcServerUnavailable)
            {
                PropChanged -= handler;
            }
            catch
            {
                // One failing subscriber must not block the others.
            }
        }
    }
}
