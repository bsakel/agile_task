using OrderPlatform.BuildingBlocks;
using OrderPlatform.Ordering.Domain.Tests.Builders;

namespace OrderPlatform.Ordering.Domain.Tests;

/// <summary>
/// Submission, the inventory outcomes, invoicing and payment in every state these PRs can reach (ADR-0017 §1, §2, §4
/// and §6). The fulfilment states follow in PR 2e; so does the late payment that turns a cancelled order into one
/// requiring attention, which is why payment triggers are only ignored in `Cancelled` here.
/// </summary>
public sealed class OrderTransitionTests
{
    /// <summary>The triggers the aggregate reacts to in these PRs.</summary>
    public enum Trigger
    {
        ReservationSucceeded,
        ReservationFailed,
        InvoicingSucceeded,
        PaymentReceived,
        PartialPaymentReceived,
        PaymentOverdue,
    }

    private static readonly OrderStatus[] ReachableStates =
    [
        OrderStatus.ValidatingInventory,
        OrderStatus.AwaitingCustomer,
        OrderStatus.Invoicing,
        OrderStatus.AwaitingPayment,
        OrderStatus.Processing,
        OrderStatus.RequiresAttention,
        OrderStatus.Cancelled,
    ];

    /// <summary>
    /// The only transitions these triggers may make, and the state each one leaves behind. Everything else must be
    /// ignored and logged. A part payment is allowed but deliberately maps to its own state: it records the shortfall
    /// and waits for the due date (ADR-0017 §4).
    /// </summary>
    private static readonly Dictionary<(OrderStatus, Trigger), OrderStatus> Allowed = new()
    {
        [(OrderStatus.ValidatingInventory, Trigger.ReservationSucceeded)] = OrderStatus.Invoicing,
        [(OrderStatus.ValidatingInventory, Trigger.ReservationFailed)] = OrderStatus.AwaitingCustomer,
        [(OrderStatus.Invoicing, Trigger.InvoicingSucceeded)] = OrderStatus.AwaitingPayment,
        [(OrderStatus.AwaitingPayment, Trigger.PaymentReceived)] = OrderStatus.Processing,
        [(OrderStatus.AwaitingPayment, Trigger.PartialPaymentReceived)] = OrderStatus.AwaitingPayment,
        [(OrderStatus.AwaitingPayment, Trigger.PaymentOverdue)] = OrderStatus.Cancelled,
    };

    /// <summary>Cancelling from these states is the customer's right; PR 2e adds `FulfilmentOnHold` (ADR-0017 §3).</summary>
    private static readonly Dictionary<OrderStatus, OrderFollowUp[]> CancellableWithCompensation = new()
    {
        [OrderStatus.ValidatingInventory] = [],
        [OrderStatus.AwaitingCustomer] = [],
        [OrderStatus.Invoicing] = [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
        [OrderStatus.AwaitingPayment] = [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
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

    public static TheoryData<OrderStatus> EveryState() => [.. ReachableStates];

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
    [MemberData(nameof(EveryState))]
    public void Customer_cancellation_is_allowed_only_before_fulfilment_and_undoes_what_that_state_did(OrderStatus status)
    {
        var order = new OrderBuilder().InState(status).Build();

        var result = order.CancelByCustomer(OrderBuilder.At);

        if (!CancellableWithCompensation.TryGetValue(status, out var compensation))
        {
            result.IsSuccess.ShouldBeFalse();
            result.Error.Code.ShouldBe("order-not-cancellable");
            result.Error.Kind.ShouldBe(ErrorKind.Conflict);
            result.Error.Message.ShouldContain(status.ToString());
            return;
        }

        result.IsSuccess.ShouldBeTrue();
        var decision = result.Value;
        OrderBuilder.Apply(order, decision.Events);

        decision.Events.ShouldHaveSingleItem().ShouldBeOfType<OrderCancelled>()
            .Reason.ShouldBe(OrderCancellationReason.CustomerRequest);
        decision.FollowUps.ShouldBe(compensation);
        order.Status.ShouldBe(OrderStatus.Cancelled);
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
        var reservationKey = OrderBuilder.ReservationKey;

        var decision = order.ReservationSucceeded(reservationKey, OrderBuilder.At);
        OrderBuilder.Apply(order, decision.Events);

        decision.FollowUps.ShouldBe([OrderFollowUp.IssueInvoice]);
        order.ReservationKey.ShouldBe(reservationKey);
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

        var decision = order.ReservationSucceeded(OrderBuilder.ReservationKey, OrderBuilder.At);

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

    private static OrderDecision Act(Order order, Trigger trigger) => trigger switch
    {
        Trigger.ReservationSucceeded => order.ReservationSucceeded(OrderBuilder.ReservationKey, OrderBuilder.At),
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
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}
