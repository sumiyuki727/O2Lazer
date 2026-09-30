using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.O2Lazer.Import;

// Notifications cannot roll back a committed write. Keep only the latest value for each
// recipient and entity, and retry failures independently of recipients that already succeeded.
internal sealed class O2JamLibraryNotificationQueue<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<(TKey Key, Action<TValue> Recipient), PendingNotification> pending = new();

    public int Count => pending.Count;

    public void Publish(TKey key, TValue value, IEnumerable<Action<TValue>> recipients)
    {
        foreach (var recipient in recipients.Distinct())
        {
            var notification = new PendingNotification(key, value, recipient);
            pending[(key, recipient)] = notification;
            deliver(notification);
        }
    }

    public void Retry(IEnumerable<Action<TValue>> recipients)
    {
        var currentRecipients = recipients.ToHashSet();
        foreach (var notification in pending.Values.ToArray())
        {
            if (!currentRecipients.Contains(notification.Recipient))
                pending.Remove((notification.Key, notification.Recipient));
            else
                deliver(notification);
        }
    }

    private void deliver(PendingNotification notification)
    {
        try
        {
            notification.Recipient(notification.Value);
            var key = (notification.Key, notification.Recipient);
            if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, notification))
                pending.Remove(key);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "O2Jam library notification failed; the committed write is retained and the notification will be retried.");
        }
    }

    private sealed record PendingNotification(TKey Key, TValue Value, Action<TValue> Recipient);
}
