namespace OrderPlatform.Testing;

/// <summary>Locations in the repository, found by walking up from the test output to the solution file.</summary>
public static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string KeycloakRealm => Path.Combine(Root, "deploy", "keycloak", "orderplatform-realm.json");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OrderPlatform.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (OrderPlatform.slnx) not found above " + AppContext.BaseDirectory);
    }
}
