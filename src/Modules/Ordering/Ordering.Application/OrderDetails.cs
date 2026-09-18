using OrderPlatform.Ordering.Domain;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// The current state of an order, kept up to date by an inline projection so a read straight after a write sees it
/// (ADR-0006). It is a read model: it carries ids and amounts, never personal data (ADR-0016), and it is rebuildable
/// from the stream, which stays the source of truth.
/// </summary>
/// <remarks>
/// Every stored event needs an <c>Apply</c> here or the projection silently stops following the order, so
/// <c>OrderDetailsProjectionTests</c> fails the build when a registered event has none.
/// </remarks>
public sealed class OrderDetails
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public OrderStatus Status { get; set; }

    public IReadOnlyList<OrderLine> Lines { get; set; } = [];

    public OrderPricing Pricing { get; set; } = null!;

    /// <summary>Set once inventory confirmed the reservation; the handle it is released with (ADR-0017 §7).</summary>
    public string? ReservationKey { get; set; }

    public string? InvoiceId { get; set; }

    public DateTimeOffset? PaymentDueAt { get; set; }

    public bool IsPartiallyPaid { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public void Apply(OrderSubmitted @event)
    {
        Id = @event.OrderId;
        AccountId = @event.AccountId;
        Lines = @event.Lines;
        Pricing = @event.Pricing;
        Status = OrderStatus.ValidatingInventory;
        SubmittedAt = @event.SubmittedAt;
        UpdatedAt = @event.SubmittedAt;
    }

    public void Apply(InventoryReserved @event)
    {
        ReservationKey = @event.ReservationKey;
        Status = OrderStatus.Invoicing;
        UpdatedAt = @event.ReservedAt;
    }

    public void Apply(InventoryUnavailable @event)
    {
        Status = OrderStatus.AwaitingCustomer;
        UpdatedAt = @event.ReportedAt;
    }

    public void Apply(InvoiceIssued @event)
    {
        InvoiceId = @event.InvoiceId;
        PaymentDueAt = @event.DueAt;
        Status = OrderStatus.AwaitingPayment;
        UpdatedAt = @event.IssuedAt;
    }

    public void Apply(InvoicePaid @event)
    {
        Status = OrderStatus.Processing;
        UpdatedAt = @event.PaidAt;
    }

    public void Apply(InvoicePartiallyPaid @event)
    {
        IsPartiallyPaid = true;
        UpdatedAt = @event.ReportedAt;
    }

    public void Apply(AttentionRequired @event)
    {
        Status = OrderStatus.RequiresAttention;
        UpdatedAt = @event.RaisedAt;
    }

    public void Apply(OrderCancelled @event)
    {
        Status = OrderStatus.Cancelled;
        UpdatedAt = @event.CancelledAt;
    }
}
