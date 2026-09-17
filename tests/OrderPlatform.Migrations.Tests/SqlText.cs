using System.Text;
using System.Text.RegularExpressions;

namespace OrderPlatform.Migrations.Tests;

/// <summary>
/// Minimal PostgreSQL lexing for rule checks: removes comments, string literals and dollar-quoted bodies (function code
/// must not trigger rules), then splits into normalised, lower-case statements.
/// </summary>
internal static partial class SqlText
{
    public static IReadOnlyList<string> Statements(string sql) =>
        StripCommentsAndLiterals(sql)
            .Split(';')
            .Select(statement => Whitespace().Replace(statement, " ").Trim().ToLowerInvariant())
            .Where(statement => statement.Length > 0)
            .ToList();

    internal static string StripCommentsAndLiterals(string sql)
    {
        var result = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            if (Starts(sql, i, "--"))
            {
                i = IndexAfter(sql, i, "\n");
                result.Append(' ');
            }
            else if (Starts(sql, i, "/*"))
            {
                i = IndexAfter(sql, i + 2, "*/");
                result.Append(' ');
            }
            else if (sql[i] == '\'')
            {
                // '' inside a literal is an escaped quote and simply starts the next literal segment.
                i = IndexAfter(sql, i + 1, "'");
                result.Append("''");
            }
            else if (sql[i] == '$' && DollarTag().Match(sql, i) is { Success: true, Index: var index } tag && index == i)
            {
                i = IndexAfter(sql, i + tag.Length, tag.Value);
                result.Append("$$ $$");
            }
            else
            {
                result.Append(sql[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool Starts(string text, int index, string value) =>
        string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static int IndexAfter(string text, int start, string value)
    {
        var index = text.IndexOf(value, start, StringComparison.Ordinal);
        return index < 0 ? text.Length : index + value.Length;
    }

    [GeneratedRegex(@"\$[A-Za-z_]*\$")]
    private static partial Regex DollarTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
