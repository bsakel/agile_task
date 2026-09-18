using OrderPlatform.Ordering.Domain.Tests.Builders;

namespace OrderPlatform.Ordering.Domain.Tests;

/// <summary>
/// Submission and the inventory outcomes in every state this PR can reach (ADR-0017 §1, §2 and §6). Invoicing, payment
/// and cancellation follow in PR 2d-2.
/// </summary>
public sealed class OrderTransitionTests
{
    /// <summary>The triggers the aggregate reacts to in this PR.</summary>
    public enum Trigger
    {
        ReservationSucceeded,
        ReservationFailed,
    }

    private static readonly OrderStatus[] ReachableStates =
    [
        OrderStatus.ValidatingInventory,
        OrderStatus.AwaitingCustomer,
        OrderStatus.Invoicing,
        OrderStatus.Cancelled,
    ];

    /// <summary>The only transitions these triggers may make. Everything else must be ignored and logged.</summary>
    private static readonly Dictionary<(OrderStatus, Trigger), OrderStatus> Allowed = new()
    {
        [(OrderStatus.ValidatingInventory, Trigger.ReservationSucceeded)] = OrderStatus.Invoicing,
        [(OrderStatus.ValidatingInventory, Trigger.ReservationFailed)] = OrderStatus.AwaitingCustomer,
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

    private static OrderDecision Act(Order order, Trigger trigger) => trigger switch
    {
        Trigger.ReservationSucceeded => order.ReservationSucceeded(Guid.NewGuid(), OrderBuilder.At),
        Trigger.ReservationFailed => order.ReservationFailed(["SKU-1"], OrderBuilder.At),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}
