using MeiErp.Platform.Kernel;

namespace MeiErp.Platform.Notifications;

/// <summary>
/// How anything in the suite tells somebody something.
///
/// A caller says who and what; it never says how. Which channels carry a message
/// is the recipient's preference and the channel's own business, so adding
/// WhatsApp later changes no call site.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Queue a notification. Returns once the rows are staged - <b>the caller
    /// must still commit</b>, because the rows are written through the same unit
    /// of work as whatever raised them.
    ///
    /// That is deliberate: an approval that commits but whose notification is
    /// rolled back leaves somebody waiting on a queue they were never told
    /// about, and a notification that commits while the approval rolls back
    /// tells them about something that never happened.
    /// </summary>
    Task<IReadOnlyList<Notification>> NotifyAsync(
        NotificationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Stand down every unread notification raised by one event, for everyone.
    /// Called when a queued approval is decided: the other eligible approvers no
    /// longer need to look at it.
    ///
    /// Stages only, like <see cref="NotifyAsync"/> - <b>the caller commits</b>.
    /// The two calls that take part in somebody else's transaction both behave
    /// this way; the bell's own actions below save for themselves, because
    /// nothing else is in flight when a person clicks one.
    /// </summary>
    Task DismissEventAsync(string eventKey, CancellationToken ct = default);

    /// <summary>What the bell shows. Newest first, unread and undismissed only.</summary>
    Task<IReadOnlyList<Notification>> UnreadAsync(
        string userId, int take = 20, CancellationToken ct = default);

    Task<int> UnreadCountAsync(string userId, CancellationToken ct = default);

    Task MarkReadAsync(int notificationId, CancellationToken ct = default);

    Task MarkAllReadAsync(string userId, CancellationToken ct = default);
}

/// <param name="Recipients">
/// Who to tell. Duplicates are collapsed - a person who is both the line manager
/// and a delegate gets told once, not twice.
/// </param>
/// <param name="Category">
/// Drives per-user preferences, e.g. "approval.assigned". Keep these stable;
/// they are the key a person's opt-outs hang off.
/// </param>
/// <param name="EventKey">
/// Ties together the notifications raised by one event so
/// <see cref="INotifier.DismissEventAsync"/> can stand them all down at once.
/// </param>
public sealed record NotificationRequest(
    IReadOnlyList<NotificationRecipient> Recipients,
    string Category,
    string Subject,
    string Body,
    string? Url = null,
    string? ModuleKey = null,
    NotificationPriority Priority = NotificationPriority.Normal,
    string? EventKey = null);

/// <param name="Email">
/// Snapshotted onto the delivery row. A person's address changing next week must
/// not rewrite where last week's message was sent.
/// </param>
public sealed record NotificationRecipient(string UserId, string Name, string? Email);

/// <summary>
/// The rows behind notifications, kept behind an interface so this project does
/// not depend on EF or on whichever context happens to own the tables. That is
/// what lets a notification be written in the same transaction as an approval
/// while still living in its own project.
/// </summary>
public interface INotificationStore
{
    void Add(Notification notification);

    Task<IReadOnlyList<NotificationPreference>> PreferencesAsync(
        IReadOnlyList<string> userIds, string category, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> UnreadAsync(
        string userId, int take, CancellationToken ct = default);

    Task<int> UnreadCountAsync(string userId, CancellationToken ct = default);

    Task<Notification?> FindAsync(int notificationId, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> ByEventAsync(string eventKey, CancellationToken ct = default);

    Task MarkAllReadAsync(string userId, DateTime nowUtc, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}

/// <summary>
/// One way of getting a message to a person. Email now, WhatsApp later - the
/// abstraction exists on day one precisely so the second one is not a special
/// case bolted onto the first.
/// </summary>
public interface INotificationChannel
{
    /// <summary>Stable key stored on every delivery row. Never rename one in place.</summary>
    string Key { get; }

    /// <summary>Shown on the preferences screen.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this channel is switched on for a category unless the person says
    /// otherwise. In-app is on for everything; email is deliberately not, or the
    /// first busy week teaches everyone to filter it to a folder.
    /// </summary>
    bool EnabledByDefault(string category);

    /// <summary>
    /// Where this channel would send to, or null if it cannot reach this person
    /// at all. Null means <see cref="DeliveryStatus.NotApplicable"/>, not a
    /// failure to retry.
    /// </summary>
    string? AddressFor(NotificationRecipient recipient);

    /// <summary>
    /// Actually send it. A returned failure is retried; a thrown exception is a
    /// bug and is logged as one.
    /// </summary>
    Task<Result> SendAsync(
        Notification notification, string address, CancellationToken ct = default);
}
