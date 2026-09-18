using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain;

/// <summary>Failures the Ordering module returns for commands it cannot accept, with stable error codes (ADR-0020).</summary>
public static class OrderErrors
{
    /// <summary>
    /// The order is past the point where the customer may cancel it. The endpoint turns this into a
    /// <c>409 Conflict</c> that names the current state and points at customer support (ADR-0017 §3).
    /// </summary>
    public static Error NotCancellable(OrderStatus status) =>
        Error.Conflict("order-not-cancellable", $"An order in state {status} can no longer be cancelled by the customer.");

    /// <summary>
    /// Support may cancel one state further than the customer, but a dispatched, delivered or already closed order is
    /// past every cancellation (ADR-0017 §3, back-office table). Its own code keeps the back-office answer distinct
    /// from the customer one, which points at support and would be misleading here.
    /// </summary>
    public static Error NotCancellableBySupport(OrderStatus status) =>
        Error.Conflict("order-not-cancellable-by-support", $"An order in state {status} can no longer be cancelled.");

    /// <summary>Reducing the order is only offered while the customer is asked to react to an inventory problem.</summary>
    public static Error ItemsNotReducible(OrderStatus status) =>
        Error.Conflict("order-items-not-reducible", $"An order in state {status} is not waiting for reduced items.");

    /// <summary>Fulfilment information is only editable while fulfilment is on hold for it (ADR-0017 §3).</summary>
    public static Error FulfilmentInformationNotUpdatable(OrderStatus status) =>
        Error.Conflict(
            "fulfilment-information-not-updatable",
            $"An order in state {status} is not waiting for updated fulfilment information.");

    /// <summary>Refreshing the payment status only means something while an issued invoice is unpaid (ADR-0017 §4).</summary>
    public static Error InvoiceStatusNotCheckable(OrderStatus status) =>
        Error.Conflict("invoice-status-not-checkable", $"An order in state {status} has no invoice awaiting payment.");

    /// <summary>Only an order that automation stopped on can be resolved by a support agent (ADR-0017 §3).</summary>
    public static Error NotRequiringAttention(OrderStatus status) =>
        Error.Conflict("order-not-requiring-attention", $"An order in state {status} is not waiting for a support agent.");

    /// <summary>
    /// Every back-office action records why a human overrode the process, so the stream explains itself later
    /// (ADR-0017 §3, "mandatory reason recorded on the event").
    /// </summary>
    public static Error SupportReasonRequired() =>
        Error.Validation("support-reason-required", "A support action must state its reason.");

    /// <summary>
    /// Only an active customer account may submit orders (README §2). A suspended or closed account is a business rule
    /// failure, not a missing resource, so submission answers <c>422</c> (ADR-0020).
    /// </summary>
    public static Error AccountNotActive(string status) =>
        Error.BusinessRule("account-not-active", $"The customer account is {status} and cannot submit orders.");
}
