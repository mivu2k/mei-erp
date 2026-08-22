namespace MeiErp.Platform.Notifications;

/// <summary>
/// One thing one person needs to be told about.
///
/// Written in the same transaction as whatever raised it, and sent afterwards by
/// the dispatcher. Nothing is sent inline: an SMTP server that is slow, down or
/// simply behind a firewall would otherwise hold open the transaction that
/// approves a payment, and a mail failure would roll back the approval.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Who it is for. An Identity user id.</summary>
    public string UserId { get; set; } = "";

    /// <summary>
    /// What kind of thing this is - "approval.assigned", "approval.settled".
    /// Preferences are expressed per category, so a person can stop the daily
    /// noise without also losing the message that says their money is ready.
    /// </summary>
    public string Category { get; set; } = "";

    public string Subject { get; set; } = "";

    /// <summary>Plain text. Channels that want markup build it from this and the link.</summary>
    public string Body { get; set; } = "";

    /// <summary>Where to go to act on it. Relative, e.g. "/finance/requests/42".</summary>
    public string? Url { get; set; }

    /// <summary>Owning module, for filtering the bell and for reporting.</summary>
    public string? ModuleKey { get; set; }

    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    public DateTime CreatedUtc { get; set; }

    /// <summary>Set when the person has seen it in the app. Null while unread.</summary>
    public DateTime? ReadUtc { get; set; }

    /// <summary>
    /// Set when the thing it was about no longer needs attention - the approval
    /// was decided by somebody else, the document was withdrawn. A bell full of
    /// items that are already handled trains people to ignore the bell.
    /// </summary>
    public DateTime? DismissedUtc { get; set; }

    /// <summary>
    /// Groups the notifications raised by one event, so they can all be
    /// dismissed together when it is settled.
    /// </summary>
    public string? EventKey { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = [];
}

public enum NotificationPriority
{
    Low = 0,
    Normal = 1,

    /// <summary>Bypasses quiet-hours batching. Reserve it for things that block someone.</summary>
    High = 2
}

/// <summary>
/// One attempt to get one notification out through one channel.
///
/// Separate from the notification because a message that reached the bell but
/// not the mail server is half-delivered, and "did they actually get told?" is
/// the only question worth asking when an approval is disputed.
/// </summary>
public class NotificationDelivery
{
    public int Id { get; set; }

    public int NotificationId { get; set; }
    public Notification? Notification { get; set; }

    /// <summary>Channel key - "inapp", "email", later "whatsapp".</summary>
    public string Channel { get; set; } = "";

    /// <summary>Where it was sent: an email address, a phone number. Snapshotted, because it changes.</summary>
    public string? Address { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>When the dispatcher should next pick this up. Null once it is finished with.</summary>
    public DateTime? NextAttemptUtc { get; set; }

    public DateTime? SentUtc { get; set; }

    /// <summary>Why the last attempt failed. Kept even after a later attempt succeeds.</summary>
    public string? LastError { get; set; }
}

public enum DeliveryStatus
{
    /// <summary>Waiting for the dispatcher.</summary>
    Pending = 0,

    Sent = 1,

    /// <summary>
    /// Failed and will be retried. Distinct from <see cref="Dead"/> so a
    /// transient mail outage does not look like a lost message.
    /// </summary>
    Failed = 2,

    /// <summary>Out of attempts. Needs a human to look at it.</summary>
    Dead = 3,

    /// <summary>
    /// This channel cannot carry this message and never will - no email address
    /// on the account, no phone number. Retrying would burn attempts forever
    /// against a thing that cannot change on its own.
    /// </summary>
    NotApplicable = 4,

    /// <summary>The recipient turned this channel off for this category.</summary>
    Suppressed = 5
}

/// <summary>
/// One person's answer to "tell me about X through Y".
///
/// Absent means the channel's own default applies, so a new category starts out
/// switched on for everybody rather than silently reaching nobody until each
/// person opts in.
/// </summary>
public class NotificationPreference
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Channel { get; set; } = "";

    public bool Enabled { get; set; } = true;
}
