using OrderPlatform.BuildingBlocks.Messaging;
using Wolverine.Util;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// Stored names are contracts: Marten event aliases live forever in streams, and Wolverine message type names sit in durable
/// queues across deployments and rollbacks. A class rename must never change them silently (ADR-0010 rules 4 and 8).
/// The names are pinned in <c>contract-names.approved.txt</c>; adding a name is a reviewed change to that file.
/// </summary>
public sealed class ContractNamesTests(UnstartedApiHost host) : IClassFixture<UnstartedApiHost>
{
    private const string ApprovedFile = "contract-names.approved.txt";

    [Fact]
    public void Stored_message_and_event_names_match_the_approved_list()
    {
        var messages = PlatformArchitecture.PlatformTypes
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && (typeof(ICommand).IsAssignableFrom(type) || typeof(IIntegrationEvent).IsAssignableFrom(type)))
            .Select(type => $"message {type.ToMessageTypeName()} <- {type.FullName}");

        var events = host.DocumentStore.Options.Events.AllKnownEventTypes()
            .Where(eventType => eventType.EventType.Namespace?.StartsWith("OrderPlatform.", StringComparison.Ordinal) == true)
            .Select(eventType => $"event {eventType.EventTypeName} <- {eventType.EventType.FullName}");

        var actual = messages.Concat(events).Order(StringComparer.Ordinal).ToList();

        var approvedPath = Path.Combine(PlatformArchitecture.RepositoryRoot.FullName, "tests", "OrderPlatform.Architecture.Tests", ApprovedFile);
        var approved = File.ReadAllLines(approvedPath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        if (!actual.SequenceEqual(approved))
        {
            File.WriteAllLines(Path.ChangeExtension(approvedPath, ".received.txt"), actual);
        }

        actual.ShouldBe(
            approved,
            "Stored names changed. A renamed type needs an explicit alias that keeps the old name; a new type is added to " +
            $"{ApprovedFile} (see contract-names.received.txt) as a reviewed change.");
    }
}
