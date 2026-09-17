namespace OrderPlatform.BuildingBlocks.Messaging;

/// <summary>
/// A command: asks exactly one handler to do something. The architecture tests fail when a command has no handler or more
/// than one, because Wolverine silently drops messages without a handler (ADR-0004, ADR-0015).
/// </summary>
public interface ICommand;

/// <summary>
/// An integration event in a module's <c>Contracts</c>: something that happened, handled by one or more other modules.
/// The architecture tests fail when nobody handles it (ADR-0015).
/// </summary>
public interface IIntegrationEvent;

/// <summary>
/// An event stored in a Marten event stream. Must be registered with an explicit alias; the stored alias is pinned by the
/// architecture tests so a class rename cannot change it (ADR-0010).
/// </summary>
public interface IDomainEvent;
