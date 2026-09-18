using OrderPlatform.BuildingBlocks;
using OrderPlatform.Ordering.Domain.Tests.Builders;

namespace OrderPlatform.Ordering.Domain.Tests;

/// <summary>
/// The whole lifecycle of ADR-0017: every state of §2 against every event, timer and command of §3 and §4, plus the
/// races of §6. The tables below are the specification — anything not in them must be ignored and logged (events and
/// timers, §1) or refused with its stable error code (commands, §3).
/// </summary>
public sealed class OrderTransitionTests
{
    /// <summary>What happens to an order without anyone asking: external events and timers.</summary>
    public enum Trigger
    {
        ReservationSucceeded,
        ReservationFailed,
        InvoicingSucceeded,
        PaymentReceived,
        PartialPaymentReceived,
        PaymentOverdue,
        FulfilmentAttemptFailed,
        DispatchReported,
        DeliveryReported,
        RefundConfirmed,
        CompensationFailed,
        CustomerResponseTimedOut,
    }

    /// <summary>What a person asks for, through the customer or the back-office API (ADR-0017 §3).</summary>
    public enum Command
    {
        CancelByCustomer,
        CancelBySupport,
        ReduceItems,
        UpdateFulfilmentInformation,
        CheckInvoiceStatus,
        ResolveAttention,
    }

    /// <summary>Every state of ADR-0017 §2; <see cref="OrderBuilder"/> replays real events to reach each one.</summary>
    private static readonly OrderStatus[] ReachableStates = [.. Enum.GetValues<OrderStatus>()];

    /// <summary>
    /// The only transitions these triggers may make, and the state each one leaves behind. Everything else must be
    /// ignored and logged. A part payment is allowed but deliberately maps to its own state: it records the shortfall
    /// and waits for the due date (ADR-0017 §4).
    /// </summary>
    private static readonly Dictionary<(OrderStatus, Trigger), OrderStatus> Allowed = new()
    {
        [(OrderStatus.ValidatingInventory, Trigger.ReservationSucceeded)] = OrderStatus.Invoicing,
        [(OrderStatus.ValidatingInventory, Trigger.ReservationFailed)] = OrderStatus.AwaitingCustomer,
        [(OrderStatus.AwaitingCustomer, Trigger.CustomerResponseTimedOut)] = OrderStatus.Cancelled,
        [(OrderStatus.Invoicing, Trigger.InvoicingSucceeded)] = OrderStatus.AwaitingPayment,
        [(OrderStatus.AwaitingPayment, Trigger.PaymentReceived)] = OrderStatus.Processing,
        [(OrderStatus.AwaitingPayment, Trigger.PartialPaymentReceived)] = OrderStatus.AwaitingPayment,
        [(OrderStatus.AwaitingPayment, Trigger.PaymentOverdue)] = OrderStatus.Cancelled,
        [(OrderStatus.Processing, Trigger.DispatchReported)] = OrderStatus.Shipped,
        [(OrderStatus.Processing, Trigger.FulfilmentAttemptFailed)] = OrderStatus.FulfilmentOnHold,
        [(OrderStatus.FulfilmentOnHold, Trigger.CustomerResponseTimedOut)] = OrderStatus.Refunding,
        [(OrderStatus.Shipped, Trigger.DeliveryReported)] = OrderStatus.Delivered,
        [(OrderStatus.Refunding, Trigger.RefundConfirmed)] = OrderStatus.Cancelled,
        [(OrderStatus.Refunding, Trigger.CompensationFailed)] = OrderStatus.RequiresAttention,
        [(OrderStatus.Cancelled, Trigger.CompensationFailed)] = OrderStatus.RequiresAttention,
        [(OrderStatus.Cancelled, Trigger.PaymentReceived)] = OrderStatus.RequiresAttention,
    };

    /// <summary>
    /// Where each command is allowed and the state it leaves behind (ADR-0017 §3, both tables). Cancelling from
    /// FulfilmentOnHold or Processing cannot finish in one step: the money has to come back first, so it ends in
    /// Refunding (§5).
    /// </summary>
    private static readonly Dictionary<(OrderStatus, Command), OrderStatus> AllowedCommands = new()
    {
        [(OrderStatus.ValidatingInventory, Command.CancelByCustomer)] = OrderStatus.Cancelled,
        [(OrderStatus.AwaitingCustomer, Command.CancelByCustomer)] = OrderStatus.Cancelled,
        [(OrderStatus.Invoicing, Command.CancelByCustomer)] = OrderStatus.Cancelled,
        [(OrderStatus.AwaitingPayment, Command.CancelByCustomer)] = OrderStatus.Cancelled,
        [(OrderStatus.FulfilmentOnHold, Command.CancelByCustomer)] = OrderStatus.Refunding,
        [(OrderStatus.ValidatingInventory, Command.CancelBySupport)] = OrderStatus.Cancelled,
        [(OrderStatus.AwaitingCustomer, Command.CancelBySupport)] = OrderStatus.Cancelled,
        [(OrderStatus.Invoicing, Command.CancelBySupport)] = OrderStatus.Cancelled,
        [(OrderStatus.AwaitingPayment, Command.CancelBySupport)] = OrderStatus.Cancelled,
        [(OrderStatus.FulfilmentOnHold, Command.CancelBySupport)] = OrderStatus.Refunding,
        [(OrderStatus.Processing, Command.CancelBySupport)] = OrderStatus.Refunding,
        [(OrderStatus.AwaitingCustomer, Command.ReduceItems)] = OrderStatus.ValidatingInventory,
        [(OrderStatus.FulfilmentOnHold, Command.UpdateFulfilmentInformation)] = OrderStatus.Processing,
        [(OrderStatus.AwaitingPayment, Command.CheckInvoiceStatus)] = OrderStatus.AwaitingPayment,
        [(OrderStatus.RequiresAttention, Command.ResolveAttention)] = OrderStatus.Cancelled,
    };

    /// <summary>The stable code each command answers with where ADR-0017 does not allow it (ADR-0020).</summary>
    private static readonly Dictionary<Command, string> RefusalCodes = new()
    {
        [Command.CancelByCustomer] = "order-not-cancellable",
        [Command.CancelBySupport] = "order-not-cancellable-by-support",
        [Command.ReduceItems] = "order-items-not-reducible",
        [Command.UpdateFulfilmentInformation] = "fulfilment-information-not-updatable",
        [Command.CheckInvoiceStatus] = "invoice-status-not-checkable",
        [Command.ResolveAttention] = "order-not-requiring-attention",
    };

    /// <summary>What cancelling from each state has to undo, whoever asked for it (ADR-0017 §5).</summary>
    private static readonly Dictionary<OrderStatus, OrderFollowUp[]> Compensation = new()
    {
        [OrderStatus.ValidatingInventory] = [],
        [OrderStatus.AwaitingCustomer] = [],
        [OrderStatus.Invoicing] = [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
        [OrderStatus.AwaitingPayment] = [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
        [OrderStatus.FulfilmentOnHold] =
            [OrderFollowUp.CancelShipmentRequest, OrderFollowUp.RefundInvoice, OrderFollowUp.ReleaseInventory],
        [OrderStatus.Processing] =
            [OrderFollowUp.CancelShipmentRequest, OrderFollowUp.RefundInvoice, OrderFollowUp.ReleaseInventory],
    };

    public static TheoryData<OrderStatus, Trigger> EveryStateAndTrigger()
    {
        var data = new TheoryData<OrderStatus, Trigger>();
        foreach (var status in ReachableStates)
        {
            foreach (var trigger in Enum.GetValues<Trigger>())
            {
                data.Add(status, trigger);
            }
        }

        return data;
    }

    public static TheoryData<OrderStatus, Command> EveryStateAndCommand()
    {
        var data = new TheoryData<OrderStatus, Command>();
        foreach (var status in ReachableStates)
        {
            foreach (var command in Enum.GetValues<Command>())
            {
                data.Add(status, command);
            }
        }

        return data;
    }

    public static TheoryData<OrderStatus> EveryCancellableState() => [.. Compensation.Keys];

    [Theory]
    [MemberData(nameof(EveryStateAndTrigger))]
    public void Every_trigger_either_makes_its_allowed_transition_or_is_ignored(OrderStatus status, Trigger trigger)
    {
        var order = new OrderBuilder().InState(status).Build();

        var decision = Act(order, trigger);
        OrderBuilder.Apply(order, decision.Events);

        if (Allowed.TryGetValue((status, trigger), out var expected))
        {
            decision.IsIgnored.ShouldBeFalse();
            decision.Events.ShouldNotBeEmpty();
            order.Status.ShouldBe(expected);
            return;
        }

        decision.Events.ShouldBeEmpty();
        order.Status.ShouldBe(status);

        // The one non-transition that still acts: a reservation completing after a cancellation is released (§6).
        if ((status, trigger) == (OrderStatus.Cancelled, Trigger.ReservationSucceeded))
        {
            decision.FollowUps.ShouldBe([OrderFollowUp.ReleaseInventory]);
        }
        else
        {
            decision.IgnoredReason.ShouldNotBeNull();
            decision.FollowUps.ShouldBeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(EveryStateAndCommand))]
    public void Every_command_is_accepted_only_where_it_is_allowed_and_refused_with_its_code_everywhere_else(
        OrderStatus status,
        Command command)
    {
        var order = new OrderBuilder().InState(status).Build();

        var result = Act(order, command);

        if (AllowedCommands.TryGetValue((status, command), out var expected))
        {
            result.IsSuccess.ShouldBeTrue();
            result.Value.IsIgnored.ShouldBeFalse();
            OrderBuilder.Apply(order, result.Value.Events);
            order.Status.ShouldBe(expected);
            return;
        }

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(RefusalCodes[command]);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Message.ShouldContain(status.ToString());
        order.Status.ShouldBe(status);
    }

    [Theory]
    [MemberData(nameof(EveryCancellableState))]
    public void Cancelling_undoes_what_the_state_it_leaves_had_already_done(OrderStatus status)
    {
        var order = new OrderBuilder().InState(status).Build();

        // Support reaches one state further than the customer; where both may cancel, they compensate identically.
        var result = status is OrderStatus.Processing
            ? order.CancelBySupport("Customer called the account manager.", OrderBuilder.At)
            : order.CancelByCustomer(OrderBuilder.At);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FollowUps.ShouldBe(Compensation[status]);
    }

    [Fact]
    public void Submitting_starts_the_stream_and_asks_inventory_to_reserve()
    {
        var submitted = OrderBuilder.Submitted();

        var decision = Order.Submit(
            submitted.OrderId,
            submitted.AccountId,
            submitted.Lines,
            submitted.Pricing,
            submitted.BillingAddressId,
            submitted.ShippingAddressId,
            submitted.ContactId,
            OrderBuilder.At);

        var @event = decision.Events.ShouldHaveSingleItem().ShouldBeOfType<OrderSubmitted>();
        @event.ProcessVersion.ShouldBe(Order.CurrentProcessVersion);
        @event.AccountId.ShouldBe(OrderBuilder.AccountId);
        decision.FollowUps.ShouldBe([OrderFollowUp.ReserveInventory]);

        var order = new Order();
        OrderBuilder.Apply(order, decision.Events);
        order.Status.ShouldBe(OrderStatus.ValidatingInventory);
    }

    [Fact]
    public void A_reserved_order_keeps_the_reservation_and_asks_billing_to_issue_the_invoice()
    {
        var order = new OrderBuilder().InState(OrderStatus.ValidatingInventory).Build();
        var reservationId = Guid.NewGuid();

        var decision = order.ReservationSucceeded(reservationId, OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.FollowUps.ShouldBe([OrderFollowUp.IssueInvoice]);
        order.ReservationId.ShouldBe(reservationId);
    }

    [Fact]
    public void An_unavailable_line_sends_the_order_back_to_the_customer()
    {
        var order = new OrderBuilder().InState(OrderStatus.ValidatingInventory).Build();

        var decision = order.ReservationFailed(["SKU-1"], OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<InventoryUnavailable>().UnavailableSkus.ShouldBe(["SKU-1"]);
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.AwaitingCustomer);
    }

    [Fact]
    public void A_reservation_arrives_after_a_cancellation_and_is_released()
    {
        var order = new OrderBuilder().InState(OrderStatus.Cancelled).Build();

        var decision = order.ReservationSucceeded(Guid.NewGuid(), OrderBuilder.At);

        decision.Events.ShouldBeEmpty();
        decision.FollowUps.ShouldBe([OrderFollowUp.ReleaseInventory]);
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void An_issued_invoice_records_its_id_and_due_date_and_waits_for_payment()
    {
        var order = new OrderBuilder().InState(OrderStatus.Invoicing).Build();

        var decision = order.InvoicingSucceeded(
            OrderBuilder.InvoiceId,
            OrderBuilder.InvoiceAmount,
            OrderBuilder.DueAt,
            OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        var @event = decision.Events.ShouldHaveSingleItem().ShouldBeOfType<InvoiceIssued>();
        @event.InvoiceId.ShouldBe(OrderBuilder.InvoiceId);
        @event.DueAt.ShouldBe(OrderBuilder.DueAt);
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        order.InvoiceId.ShouldBe(OrderBuilder.InvoiceId);
        order.PaymentDueAt.ShouldBe(OrderBuilder.DueAt);
    }

    [Fact]
    public void A_part_payment_is_recorded_and_the_order_keeps_waiting_for_the_rest()
    {
        var order = new OrderBuilder().InState(OrderStatus.AwaitingPayment).Build();

        var decision = order.PartialPaymentReceived(Money.InEur(10.00m), Money.InEur(14.20m), OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<InvoicePartiallyPaid>()
            .AmountOutstanding.ShouldBe(Money.InEur(14.20m));
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        order.IsPartiallyPaid.ShouldBeTrue();
    }

    [Fact]
    public void An_unpaid_invoice_at_its_due_date_cancels_the_order_and_voids_and_releases()
    {
        var order = new OrderBuilder().InState(OrderStatus.AwaitingPayment).Build();

        var decision = order.PaymentOverdue(OrderBuilder.DueAt);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<OrderCancelled>()
            .Reason.ShouldBe(OrderCancellationReason.InvoiceOverdue);
        decision.FollowUps.ShouldBe([OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory]);
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_part_paid_invoice_at_its_due_date_stops_automation_for_support()
    {
        var order = new OrderBuilder().InState(OrderStatus.AwaitingPayment).WithPartialPayment().Build();

        var decision = order.PaymentOverdue(OrderBuilder.DueAt);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<AttentionRequired>()
            .Reason.ShouldBe(OrderAttentionReason.PartialPaymentAtDueDate);
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.RequiresAttention);
    }

    [Fact]
    public void An_overdue_timer_that_fires_after_payment_is_ignored()
    {
        var order = new OrderBuilder().InState(OrderStatus.Processing).Build();

        var decision = order.PaymentOverdue(OrderBuilder.DueAt);

        decision.IsIgnored.ShouldBeTrue();
        decision.Events.ShouldBeEmpty();
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.Processing);
    }

    [Fact]
    public void A_paid_invoice_asks_the_fulfilment_system_for_a_shipment()
    {
        var order = new OrderBuilder().InState(OrderStatus.AwaitingPayment).Build();

        var decision = order.PaymentReceived(OrderBuilder.At);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<InvoicePaid>();
        decision.FollowUps.ShouldBe([OrderFollowUp.RequestShipment]);
    }

    [Fact]
    public void Refreshing_the_payment_status_asks_billing_without_changing_the_stream()
    {
        var result = new OrderBuilder().InState(OrderStatus.AwaitingPayment).Build().CheckInvoiceStatus();

        result.Value!.Events.ShouldBeEmpty();
        result.Value.FollowUps.ShouldBe([OrderFollowUp.CheckInvoiceStatus]);
    }

    [Fact]
    public void Corrected_fulfilment_information_is_recorded_by_id_and_fulfilment_is_asked_again()
    {
        var order = new OrderBuilder().InState(OrderStatus.FulfilmentOnHold).Build();
        var shippingAddressId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var result = order.UpdateFulfilmentInformation(shippingAddressId, contactId, OrderBuilder.At);
        OrderBuilder.Apply(order, result.Value!.Events);

        var @event = result.Value.Events.ShouldHaveSingleItem().ShouldBeOfType<FulfilmentInformationUpdated>();
        @event.ShippingAddressId.ShouldBe(shippingAddressId);
        @event.ContactId.ShouldBe(contactId);
        result.Value.FollowUps.ShouldBe([OrderFollowUp.RequestShipment]);
        order.Status.ShouldBe(OrderStatus.Processing);
    }

    [Fact]
    public void A_dispatched_shipment_records_the_tracking_reference_and_only_delivery_follows()
    {
        var order = new OrderBuilder().InState(OrderStatus.Processing).Build();

        var decision = order.DispatchReported(OrderBuilder.TrackingReference, OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        order.TrackingReference.ShouldBe(OrderBuilder.TrackingReference);
        order.Status.ShouldBe(OrderStatus.Shipped);
        order.DeliveryReported(OrderBuilder.At).Events.ShouldHaveSingleItem().ShouldBeOfType<ShipmentDelivered>();
    }

    [Fact]
    public void A_fulfilment_failure_reported_after_dispatch_is_ignored_because_carrier_issues_are_out_of_scope()
    {
        var order = new OrderBuilder().InState(OrderStatus.Shipped).Build();

        var decision = order.FulfilmentAttemptFailed("Parcel damaged in the depot.", OrderBuilder.At);

        decision.IsIgnored.ShouldBeTrue();
        decision.Events.ShouldBeEmpty();
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.Shipped);
    }

    [Fact]
    public void Support_cancellation_records_the_mandatory_reason_on_the_event()
    {
        var order = new OrderBuilder().InState(OrderStatus.Processing).Build();

        var result = order.CancelBySupport("Customer is disputing the delivery window.", OrderBuilder.At);
        OrderBuilder.Apply(order, result.Value!.Events);

        var @event = result.Value.Events.ShouldHaveSingleItem().ShouldBeOfType<RefundRequested>();
        @event.Reason.ShouldBe(OrderCancellationReason.SupportRequest);
        @event.Details.ShouldBe("Customer is disputing the delivery window.");
        order.Status.ShouldBe(OrderStatus.Refunding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_support_action_without_a_reason_is_refused(string reason)
    {
        var cancel = new OrderBuilder().InState(OrderStatus.Processing).Build().CancelBySupport(reason, OrderBuilder.At);
        var resolve = new OrderBuilder().InState(OrderStatus.RequiresAttention).Build().ResolveAttention(reason, OrderBuilder.At);

        cancel.Error!.Code.ShouldBe("support-reason-required");
        cancel.Error.Kind.ShouldBe(ErrorKind.Validation);
        resolve.Error!.Code.ShouldBe("support-reason-required");
    }

    [Fact]
    public void A_confirmed_refund_completes_the_cancellation_a_late_cancel_started()
    {
        var order = new OrderBuilder().InState(OrderStatus.Refunding).Build();

        var decision = order.RefundConfirmed("REF-1", OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<RefundCompleted>().RefundReference.ShouldBe("REF-1");
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_compensation_step_that_gave_up_stops_automation_instead_of_retrying_forever()
    {
        var order = new OrderBuilder().InState(OrderStatus.Refunding).Build();

        var decision = order.CompensationFailed(OrderFollowUp.RefundInvoice, OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        var @event = decision.Events.ShouldHaveSingleItem().ShouldBeOfType<AttentionRequired>();
        @event.Reason.ShouldBe(OrderAttentionReason.CompensationFailed);
        @event.Details.ShouldNotBeNull().ShouldContain(nameof(OrderFollowUp.RefundInvoice));
        order.Status.ShouldBe(OrderStatus.RequiresAttention);
    }

    [Fact]
    public void A_payment_that_arrives_after_a_cancellation_needs_a_support_agent()
    {
        var order = new OrderBuilder().InState(OrderStatus.Cancelled).Build();

        var decision = order.PaymentReceived(OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<AttentionRequired>()
            .Reason.ShouldBe(OrderAttentionReason.LatePaymentAfterCancellation);
        decision.FollowUps.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.RequiresAttention);
    }

    [Fact]
    public void A_customer_who_never_responds_is_cancelled_the_same_way_as_one_who_asks()
    {
        var awaitingCustomer = new OrderBuilder().InState(OrderStatus.AwaitingCustomer).Build();
        var onHold = new OrderBuilder().InState(OrderStatus.FulfilmentOnHold).Build();

        var abandoned = awaitingCustomer.CustomerResponseTimedOut(OrderBuilder.At);
        var abandonedOnHold = onHold.CustomerResponseTimedOut(OrderBuilder.At);

        abandoned.Events.ShouldHaveSingleItem().ShouldBeOfType<OrderCancelled>()
            .Reason.ShouldBe(OrderCancellationReason.CustomerResponseTimeout);
        abandonedOnHold.Events.ShouldHaveSingleItem().ShouldBeOfType<RefundRequested>()
            .Reason.ShouldBe(OrderCancellationReason.CustomerResponseTimeout);
        abandonedOnHold.FollowUps.ShouldBe(Compensation[OrderStatus.FulfilmentOnHold]);
    }

    [Fact]
    public void A_reduced_order_carries_its_new_price_and_is_reserved_again()
    {
        var order = new OrderBuilder().InState(OrderStatus.AwaitingCustomer).Build();
        var pricing = new OrderPricing(Money.InEur(10.00m), Money.InEur(2.10m), Money.InEur(12.10m), "base-2026-09", ReverseCharge: false);

        var result = order.ReduceItems([new OrderLine("SKU-1", 1, Money.InEur(10.00m))], pricing, OrderBuilder.At);
        OrderBuilder.Apply(order, result.Value!.Events);

        var @event = result.Value.Events.ShouldHaveSingleItem().ShouldBeOfType<OrderItemsReduced>();
        @event.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(1);
        @event.Pricing.ShouldBe(pricing);
        result.Value.FollowUps.ShouldBe([OrderFollowUp.ReserveInventory]);
        order.Status.ShouldBe(OrderStatus.ValidatingInventory);
    }

    [Fact]
    public void Support_resolving_an_order_closes_it_with_the_reason_on_the_stream()
    {
        var order = new OrderBuilder().InState(OrderStatus.RequiresAttention).Build();

        var result = order.ResolveAttention("Refunded the part payment by bank transfer.", OrderBuilder.At);
        OrderBuilder.Apply(order, result.Value!.Events);

        result.Value.Events.ShouldHaveSingleItem().ShouldBeOfType<AttentionResolved>()
            .Reason.ShouldBe("Refunded the part payment by bank transfer.");
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    private static Result<OrderDecision> Act(Order order, Command command) => command switch
    {
        Command.CancelByCustomer => order.CancelByCustomer(OrderBuilder.At),
        Command.CancelBySupport => order.CancelBySupport("Agreed with the account manager.", OrderBuilder.At),
        Command.ReduceItems => order.ReduceItems(
            [new OrderLine("SKU-1", 1, Money.InEur(10.00m))],
            new OrderPricing(Money.InEur(10.00m), Money.InEur(2.10m), Money.InEur(12.10m), "base-2026-09", ReverseCharge: false),
            OrderBuilder.At),
        Command.UpdateFulfilmentInformation => order.UpdateFulfilmentInformation(Guid.NewGuid(), Guid.NewGuid(), OrderBuilder.At),
        Command.CheckInvoiceStatus => order.CheckInvoiceStatus(),
        Command.ResolveAttention => order.ResolveAttention("Handled by the support desk.", OrderBuilder.At),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private static OrderDecision Act(Order order, Trigger trigger) => trigger switch
    {
        Trigger.ReservationSucceeded => order.ReservationSucceeded(Guid.NewGuid(), OrderBuilder.At),
        Trigger.ReservationFailed => order.ReservationFailed(["SKU-1"], OrderBuilder.At),
        Trigger.InvoicingSucceeded => order.InvoicingSucceeded(
            OrderBuilder.InvoiceId,
            OrderBuilder.InvoiceAmount,
            OrderBuilder.DueAt,
            OrderBuilder.At),
        Trigger.PaymentReceived => order.PaymentReceived(OrderBuilder.At),
        Trigger.PartialPaymentReceived => order.PartialPaymentReceived(
            Money.InEur(10.00m),
            Money.InEur(14.20m),
            OrderBuilder.At),
        Trigger.PaymentOverdue => order.PaymentOverdue(OrderBuilder.DueAt),
        Trigger.FulfilmentAttemptFailed => order.FulfilmentAttemptFailed("The delivery address is incomplete.", OrderBuilder.At),
        Trigger.DispatchReported => order.DispatchReported(OrderBuilder.TrackingReference, OrderBuilder.At),
        Trigger.DeliveryReported => order.DeliveryReported(OrderBuilder.At),
        Trigger.RefundConfirmed => order.RefundConfirmed("REF-2026-0001", OrderBuilder.At),
        Trigger.CompensationFailed => order.CompensationFailed(OrderFollowUp.RefundInvoice, OrderBuilder.At),
        Trigger.CustomerResponseTimedOut => order.CustomerResponseTimedOut(OrderBuilder.At),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}
