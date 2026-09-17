namespace OrderPlatform.Ordering.Domain;

/// <summary>
/// A step another module has to perform after the decision. The aggregate names the step in its own language; the
/// handler translates it into that module's command (ADR-0003, ADR-0017 §5).
/// </summary>
public enum OrderFollowUp
{
    ReserveInventory = 1,
    ReleaseInventory = 2,
    IssueInvoice = 3,
}

/// <summary>
/// What the aggregate decided: the events to append and the steps to send through the outbox. An event or timer that
/// does not apply to the current state is <see cref="Ignored"/> — logged, never an error, so stale timers, duplicate
/// deliveries and late external events are harmless (ADR-0017 §1).
/// </summary>
public sealed record OrderDecision(
    IReadOnlyList<object> Events,
    IReadOnlyList<OrderFollowUp> FollowUps,
    string? IgnoredReason = null)
{
    public bool IsIgnored => IgnoredReason is not null;

    public static OrderDecision Ignore(string reason) => new([], [], reason);

    public static OrderDecision From(object @event, params OrderFollowUp[] followUps) => new([@event], followUps);

    /// <summary>A decision with no event of its own, e.g. releasing a reservation that arrived after cancellation.</summary>
    public static OrderDecision FollowUpOnly(params OrderFollowUp[] followUps) => new([], followUps);
}
