using Marten;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Ordering.Application;
using OrderPlatform.Ordering.Domain;
using OrderPlatform.Ordering.Infrastructure.Endpoints;
using Wolverine;

namespace OrderPlatform.Ordering.Infrastructure;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";

    // Event-sourced module: its schema is owned by Marten (ADR-0006), so it has no DbUp scripts.
    public RelationalSchema? RelationalSchema => null;

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureMarten(StoreOptions options)
    {
        // Every document type, projection and event alias is registered here explicitly (ADR-0008, ADR-0010).
        // The alias is the stored contract: renaming the C# class never changes it (ADR-0010 rule 4).
        options.Events.MapEventType<OrderSubmitted>("order_submitted");
        options.Events.MapEventType<InventoryReserved>("inventory_reserved");
        options.Events.MapEventType<InventoryUnavailable>("inventory_unavailable");
        options.Events.MapEventType<InvoiceIssued>("invoice_issued");
        options.Events.MapEventType<InvoicePaid>("invoice_paid");
        options.Events.MapEventType<InvoicePartiallyPaid>("invoice_partially_paid");
        options.Events.MapEventType<AttentionRequired>("attention_required");
        options.Events.MapEventType<OrderCancelled>("order_cancelled");
        options.Events.MapEventType<OrderItemsReduced>("order_items_reduced");
        options.Events.MapEventType<FulfilmentFailed>("fulfilment_failed");
        options.Events.MapEventType<FulfilmentInformationUpdated>("fulfilment_information_updated");
        options.Events.MapEventType<ShipmentDispatched>("shipment_dispatched");
        options.Events.MapEventType<ShipmentDelivered>("shipment_delivered");
        options.Events.MapEventType<RefundRequested>("refund_requested");
        options.Events.MapEventType<RefundCompleted>("refund_completed");
        options.Events.MapEventType<AttentionResolved>("attention_resolved");
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(OrderingApplication.Assembly);

    public void MapEndpoints(IEndpointRouteBuilder version) => version.MapOrderEndpoints();
}
