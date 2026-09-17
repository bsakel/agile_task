namespace OrderPlatform.Migrations.Tests;

/// <summary>Finds every DbUp script in the repository: <c>src/**/Migrations/{expand,contract}/*.sql</c>, one schema per project.</summary>
internal static class RepositoryScripts
{
    public static DirectoryInfo Root { get; } = FindRoot();

    public static IReadOnlyList<MigrationScript> All { get; } = Load();

    private static List<MigrationScript> Load() =>
        Directory.GetFiles(Path.Combine(Root.FullName, "src"), "*.sql", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Directory is { Name: "expand" or "contract", Parent.Name: "Migrations" })
            .Where(file => !file.FullName.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(file => new MigrationScript(
                Schema: file.Directory!.Parent!.Parent!.Name,
                Kind: file.Directory.Name == "expand" ? ScriptKind.Expand : ScriptKind.Contract,
                FileName: file.Name,
                Sql: File.ReadAllText(file.FullName)))
            .OrderBy(script => script.Schema, StringComparer.Ordinal)
            .ThenBy(script => script.FileName, StringComparer.Ordinal)
            .ToList();

    public static string RelativePath(MigrationScript script) =>
        Directory.GetFiles(Path.Combine(Root.FullName, "src"), script.FileName, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root.FullName, path).Replace('\\', '/'))
            .First(path => path.Contains($"/{script.Schema}/Migrations/{script.Name}", StringComparison.Ordinal));

    private static DirectoryInfo FindRoot()
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
