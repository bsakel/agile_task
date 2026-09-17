using System.Reflection;
using ArchUnitNET.Loader;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace OrderPlatform.Architecture.Tests;

/// <summary>The platform's assemblies, loaded once for all rules.</summary>
internal static class PlatformArchitecture
{
    public static IReadOnlyList<string> Modules { get; } = ["Ordering", "Customers", "Pricing", "Inventory", "Billing", "Shipping"];

    /// <summary>
    /// Every <c>OrderPlatform.*</c> assembly next to the tests, including module assemblies that contain no types yet
    /// (the compiler drops references to those, so they cannot be found through the Api's references).
    /// </summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } = Directory
        .GetFiles(AppContext.BaseDirectory, "OrderPlatform.*.dll")
        .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.Ordinal))
        .Select(path => Assembly.LoadFrom(path))
        .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
        .ToList();

    public static ArchitectureModel Model { get; } = new ArchLoader().LoadAssemblies([.. Assemblies]).Build();

    public static IEnumerable<System.Type> PlatformTypes => Assemblies.SelectMany(assembly => assembly.GetTypes());

    /// <summary>Walks up from the test output to the repository root (the folder with the solution file).</summary>
    public static DirectoryInfo RepositoryRoot { get; } = FindRepositoryRoot();

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OrderPlatform.slnx")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException("Repository root (OrderPlatform.slnx) not found above " + AppContext.BaseDirectory);
    }
}
