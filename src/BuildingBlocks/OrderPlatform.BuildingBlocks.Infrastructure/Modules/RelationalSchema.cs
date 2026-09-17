using System.Reflection;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// A database schema owned by one module and the assembly embedding its DbUp scripts as
/// <c>migrations/expand/*.sql</c> and <c>migrations/contract/*.sql</c> (ADR-0007, ADR-0009).
/// </summary>
public sealed record RelationalSchema(string SchemaName, Assembly ScriptsAssembly);
