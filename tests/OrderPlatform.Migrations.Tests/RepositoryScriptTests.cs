using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OrderPlatform.Migrations.Tests;

/// <summary>Applies the ADR-0009 rules to every DbUp script in the repository and to the generated Marten patch.</summary>
public sealed partial class RepositoryScriptTests
{
    /// <summary>Set by CI to the latest release tag; contract scripts must complete an expand script released there.</summary>
    private const string ReleaseTagVariable = "MIGRATIONS_RELEASE_TAG";

    /// <summary>Set by CI to the Marten patch generated against the previous release's schema (ADR-0008).</summary>
    private const string MartenPatchVariable = "MARTEN_PATCH_FILE";

    public static TheoryData<string> Scripts => [.. RepositoryScripts.All.Select(script => $"{script.Schema}/{script.Name}")];

    [Fact]
    public void The_repository_contains_scripts_to_check()
    {
        RepositoryScripts.All.ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Script_follows_the_expand_contract_and_online_safety_rules(string scriptId)
    {
        var script = RepositoryScripts.All.Single(candidate => $"{candidate.Schema}/{candidate.Name}" == scriptId);
        var schemaScripts = RepositoryScripts.All.Where(other => other.Schema == script.Schema).ToList();

        MigrationRules.CheckScript(script, schemaScripts)
            .ShouldBeEmpty($"{RepositoryScripts.RelativePath(script)} violates architecture/adr/0009-expand-contract-database-changes.md");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Contract_script_completes_an_expand_script_from_an_earlier_release(string scriptId)
    {
        var script = RepositoryScripts.All.Single(candidate => $"{candidate.Schema}/{candidate.Name}" == scriptId);
        if (script.Kind != ScriptKind.Contract)
        {
            return;
        }

        var releaseTag = Environment.GetEnvironmentVariable(ReleaseTagVariable);
        if (string.IsNullOrWhiteSpace(releaseTag))
        {
            Assert.Skip($"{ReleaseTagVariable} is not set (no release yet, or not running in CI).");
        }

        var expandName = CompletesReference().Match(script.Sql).Groups["script"].Value;
        var expandPath = Path.GetDirectoryName(Path.GetDirectoryName(RepositoryScripts.RelativePath(script)))!.Replace('\\', '/') + "/" + expandName;

        GitObjectExists($"{releaseTag}:{expandPath}")
            .ShouldBeTrue($"{expandPath} is not part of release {releaseTag}; code removal and contract must ship in a later release (ADR-0009 rule 2)");
    }

    [Fact]
    public void Generated_Marten_patch_is_expand_only()
    {
        var patchFile = Environment.GetEnvironmentVariable(MartenPatchVariable);
        if (string.IsNullOrWhiteSpace(patchFile))
        {
            Assert.Skip($"{MartenPatchVariable} is not set; CI generates the patch against the previous release.");
        }

        if (!File.Exists(patchFile))
        {
            // db-patch writes no file when the schema is unchanged.
            return;
        }

        MigrationRules.CheckGeneratedPatch(File.ReadAllText(patchFile))
            .ShouldBeEmpty($"{patchFile} contains changes that are not expand-only; write a hand-reviewed contract instead (ADR-0008, ADR-0009)");
    }

    private static bool GitObjectExists(string revisionPath)
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["cat-file", "-e", revisionPath])
        {
            WorkingDirectory = RepositoryScripts.Root.FullName,
            RedirectStandardError = true,
        })!;
        git.WaitForExit();
        return git.ExitCode == 0;
    }

    [GeneratedRegex(@"--\s*completes:\s*(?<script>expand/[\w.\-]+\.sql)", RegexOptions.IgnoreCase)]
    private static partial Regex CompletesReference();
}
