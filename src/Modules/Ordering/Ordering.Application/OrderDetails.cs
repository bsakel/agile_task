using OrderPlatform.Ordering.Domain;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// The current state of an order, kept up to date by an inline projection so a read straight after a write sees it
/// (ADR-0006). It is a read model: it carries ids and amounts, never personal data (ADR-0016), and it is rebuildable
/// from the stream, which stays the source of truth.
/// </summary>
/// <remarks>
/// Every stored event needs an <c>Apply</c> here or the projection silently stops following the order, so
/// <c>ProjectionCoverageTests</c> fails the build when a stored event has none.
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

    /// <summary>Set once the carrier took the shipment, so a customer can follow it (ADR-0017 §2).</summary>
    public string? TrackingReference { get; set; }

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

    /// <summary>The customer removed what could not be supplied, so the order is priced again (ADR-0018).</summary>
    public void Apply(OrderItemsReduced @event)
    {
        Lines = @event.Lines;
        Pricing = @event.Pricing;
        Status = OrderStatus.ValidatingInventory;
        UpdatedAt = @event.ReducedAt;
    }

    public void Apply(FulfilmentFailed @event)
    {
        Status = OrderStatus.FulfilmentOnHold;
        UpdatedAt = @event.ReportedAt;
    }

    public void Apply(FulfilmentInformationUpdated @event)
    {
        Status = OrderStatus.Processing;
        UpdatedAt = @event.UpdatedAt;
    }

    public void Apply(ShipmentDispatched @event)
    {
        TrackingReference = @event.TrackingReference;
        Status = OrderStatus.Shipped;
        UpdatedAt = @event.DispatchedAt;
    }

    public void Apply(ShipmentDelivered @event)
    {
        Status = OrderStatus.Delivered;
        UpdatedAt = @event.DeliveredAt;
    }

    /// <summary>Being unwound, but not cancelled until billing confirms the refund (ADR-0017 §5).</summary>
    public void Apply(RefundRequested @event)
    {
        Status = OrderStatus.Refunding;
        UpdatedAt = @event.RequestedAt;
    }

    public void Apply(RefundCompleted @event)
    {
        Status = OrderStatus.Cancelled;
        UpdatedAt = @event.CompletedAt;
    }

    public void Apply(AttentionResolved @event)
    {
        Status = OrderStatus.Cancelled;
        UpdatedAt = @event.ResolvedAt;
    }
}
