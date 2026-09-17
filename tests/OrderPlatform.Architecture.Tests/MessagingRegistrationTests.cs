using System.Text.RegularExpressions;
using OrderPlatform.BuildingBlocks.Messaging;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// Wolverine drops a message without a handler silently (spike S1), so handler coverage is checked against the compiled
/// handler graph of the real Api host (ADR-0004, ADR-0015).
/// </summary>
public sealed partial class MessagingRegistrationTests(UnstartedApiHost host) : IClassFixture<UnstartedApiHost>
{
    /// <summary>Platform code outside the modules that may declare handlers.</summary>
    private static readonly string[] PlatformHandlerNamespaces =
    [
        "OrderPlatform.Api.Diagnostics",
        "OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency",
    ];

    [Fact]
    public void Every_command_has_exactly_one_handler()
    {
        var problems = MessageTypes<ICommand>()
            .Select(type => (type, count: HandlerCount(type)))
            .Where(entry => entry.count != 1)
            .Select(entry => $"{entry.type.FullName}: {entry.count} handler(s)")
            .ToList();

        problems.ShouldBeEmpty("every ICommand needs exactly one <Message>Handler in its module's Application project");
    }

    [Fact]
    public void Every_integration_event_has_at_least_one_handler()
    {
        var unhandled = MessageTypes<IIntegrationEvent>()
            .Where(type => HandlerCount(type) == 0)
            .Select(type => type.FullName)
            .ToList();

        unhandled.ShouldBeEmpty("an integration event nobody handles would be dropped silently by Wolverine");
    }

    [Fact]
    public void Every_handled_platform_message_is_marked_as_command_or_integration_event()
    {
        var unmarked = PlatformChains()
            .Select(chain => chain.MessageType)
            .Where(type => !typeof(ICommand).IsAssignableFrom(type) && !typeof(IIntegrationEvent).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        unmarked.ShouldBeEmpty("mark messages with ICommand or IIntegrationEvent so handler coverage can be checked");
    }

    [Fact]
    public void Handlers_are_named_after_their_message_and_live_in_an_Application_project()
    {
        var problems = PlatformChains()
            .SelectMany(chain => chain.Handlers.Select(call => (chain.MessageType, call.HandlerType)))
            .Where(entry => entry.HandlerType.Name != entry.MessageType.Name + "Handler"
                || !(ApplicationNamespace().IsMatch(entry.HandlerType.Namespace ?? "") || PlatformHandlerNamespaces.Contains(entry.HandlerType.Namespace)))
            .Select(entry => $"{entry.HandlerType.FullName} handles {entry.MessageType.FullName}")
            .ToList();

        problems.ShouldBeEmpty("handlers are named <Message>Handler and live in OrderPlatform.<Module>.Application (ADR-0004)");
    }

    private static IEnumerable<Type> MessageTypes<TMarker>() =>
        PlatformArchitecture.PlatformTypes.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(TMarker).IsAssignableFrom(type));

    private int HandlerCount(Type messageType) =>
        host.Handlers.Chains.Where(chain => chain.MessageType == messageType).Sum(chain => chain.Handlers.Count);

    private IEnumerable<Wolverine.Runtime.Handlers.HandlerChain> PlatformChains() =>
        host.Handlers.Chains.Where(chain => chain.MessageType.Namespace?.StartsWith("OrderPlatform.", StringComparison.Ordinal) == true);

    [GeneratedRegex(@"^OrderPlatform\.\w+\.Application(\.|$)")]
    private static partial Regex ApplicationNamespace();
}
