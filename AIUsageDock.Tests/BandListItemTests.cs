using System.Runtime.InteropServices;
using AIUsageDock.Bands;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class BandListItemTests
{
    [Fact]
    public void RestartedHostStillReceivesUpdatesAfterPreviousHostDisconnected()
    {
        var item = new BandListItem(new NoOpCommand());
        INotifyPropChanged observable = item;
        var deadHostCalls = 0;
        var received = new List<string>();

        // The previous Command Palette subscribed first and is now gone.
        observable.PropChanged += (_, _) =>
        {
            deadHostCalls++;
            throw new COMException("host gone", unchecked((int)0x80010108));
        };
        observable.PropChanged += (_, args) => received.Add(args.PropertyName);

        item.Title = "Work 80%";
        item.Subtitle = "wk 40%";

        Assert.Equal(["Title", "Subtitle"], received);
        Assert.Equal(1, deadHostCalls);
        Assert.Equal(1, item.SubscriberCount);
    }

    [Fact]
    public void FailingSubscriberDoesNotBlockOthers()
    {
        var item = new BandListItem(new NoOpCommand());
        INotifyPropChanged observable = item;
        var received = 0;
        observable.PropChanged += (_, _) => throw new InvalidOperationException();
        observable.PropChanged += (_, _) => received++;

        item.Title = "a";
        item.Title = "b";

        Assert.Equal(2, received);
        Assert.Equal(2, item.SubscriberCount);
    }
}
