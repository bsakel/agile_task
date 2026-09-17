using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using OrderPlatform.BuildingBlocks.Messaging;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// Marten creates no schema at runtime (<c>AutoCreate.None</c>), and the Migrator only knows explicitly registered types,
/// so an unregistered document fails in production only (spike S4). These checks catch it at build time (ADR-0008, ADR-0015).
/// </summary>
public sealed class PersistenceRegistrationTests(UnstartedApiHost host) : IClassFixture<UnstartedApiHost>
{
    [Fact]
    public void Every_type_used_as_a_Marten_document_is_registered_explicitly()
    {
        var registered = host.DocumentStore.Options.AllKnownDocumentTypes()
            .Select(mapping => mapping.DocumentType.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var unregistered = DocumentTypesUsedWithMarten()
            .Where(usage => !registered.Contains(usage.DocumentType))
            .Select(usage => $"{usage.DocumentType} (used by {usage.Caller})")
            .Distinct()
            .ToList();

        unregistered.ShouldBeEmpty("register every document type in the module's ConfigureMarten (options.Schema.For<T>())");
    }

    [Fact]
    public void Every_domain_event_is_registered_as_a_Marten_event_type()
    {
        var registered = host.DocumentStore.Options.Events.AllKnownEventTypes()
            .Select(eventType => eventType.EventType)
            .ToHashSet();

        var unregistered = PlatformArchitecture.PlatformTypes
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .Where(type => !registered.Contains(type))
            .Select(type => type.FullName)
            .ToList();

        unregistered.ShouldBeEmpty("register every event with options.Events.MapEventType<T>(\"alias\") (ADR-0010 rule 4)");
    }

    /// <summary>
    /// Generic arguments of calls to Marten session and store members (<c>Insert&lt;T&gt;</c>, <c>LoadAsync&lt;T&gt;</c>,
    /// <c>Query&lt;T&gt;</c>, ...) that are platform types. Event store operations are excluded: their generic arguments
    /// are aggregates, not documents.
    /// </summary>
    private static IEnumerable<(string DocumentType, string Caller)> DocumentTypesUsedWithMarten() =>
        PlatformArchitecture.Model.Types
            .SelectMany(type => type.Dependencies.OfType<MethodCallDependency>())
            .Where(call => call.TargetMember.DeclaringType.FullName.StartsWith("Marten.", StringComparison.Ordinal)
                && !call.TargetMember.DeclaringType.Name.Contains("Event", StringComparison.Ordinal))
            .SelectMany(call => call.TargetMemberGenericArguments.Select(argument => (argument.Type, call.Origin)))
            .Where(usage => usage.Type.FullName.StartsWith("OrderPlatform.", StringComparison.Ordinal))
            .Select(usage => (usage.Type.FullName, ((IType)usage.Origin).FullName));
}
