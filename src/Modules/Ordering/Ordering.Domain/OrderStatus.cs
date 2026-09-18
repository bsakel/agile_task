namespace OrderPlatform.Ordering.Domain;

/// <summary>
/// The states of the order lifecycle (ADR-0017 §2, README §8). Every state exists from the first release, so a status a
/// client does not know is a new value it must tolerate, not a new endpoint (ADR-0020).
/// </summary>
public enum OrderStatus
{
    /// <summary>Availability check and all-or-nothing reservation in the external inventory system.</summary>
    ValidatingInventory = 1,

    /// <summary>Inventory problem; the customer must reduce the order or cancel.</summary>
    AwaitingCustomer = 2,

    /// <summary>Invoice being issued by the external billing system.</summary>
    Invoicing = 3,

    /// <summary>Invoice issued, due in 3 calendar days (UTC).</summary>
    AwaitingPayment = 4,

    /// <summary>Packaging and shipment preparation by the fulfilment system.</summary>
    Processing = 5,

    /// <summary>Fulfilment error; the customer must update information or cancel.</summary>
    FulfilmentOnHold = 6,

    /// <summary>Refund requested, inventory being released, shipment request cancelled.</summary>
    Refunding = 7,

    /// <summary>Dispatched by the carrier.</summary>
    Shipped = 8,

    /// <summary>Terminal.</summary>
    Delivered = 9,

    /// <summary>Terminal: inventory released, invoice voided or refunded.</summary>
    Cancelled = 10,

    /// <summary>Automation stopped; a support agent must resolve.</summary>
    RequiresAttention = 11,
}
