using System.Text.RegularExpressions;
using FluentAssertions;

namespace Contoso.AppHost.Tests;

/// <summary>
/// Cross-validates the 8-user test list across three independent
/// definitions:
///   1. <c>infra/setup-local.ps1</c>  — <c>$TestUserNicknames</c> (PowerShell hashtable)
///   2. <c>infra/setup-local.sh</c>   — <c>TEST_USER_KEYS</c> + <c>TEST_USER_NICKS</c> (Bash arrays)
///   3. <c>infra/terraform/modules/entra/v1/variables.tf</c> — <c>variable "test_users"</c> default
///
/// These three lists MUST agree on:
///   - the same 8 user keys (emma..tom)
///   - the same mail-nickname stem (e.g. "emma.wilson") for each key
///
/// If they drift, the local-dev cleanup script will leave orphan users
/// behind in Entra (PS1 vs SH disagreement) or fail to delete what
/// Terraform created (PS1/SH vs TF disagreement).
/// </summary>
public class TestUserListConsistencyTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string[] ExpectedKeys =
    {
        "emma", "james", "sarah", "david",
        "lisa", "mike", "anna", "tom"
    };

    // Canonical user -> Cosmos customer-id mapping. A swap here (e.g.
    // emma -> 104) would let one signed-in user view another customer's
    // orders without any test failing on the keys/nicknames alone.
    private static readonly Dictionary<string, string> ExpectedCustomerIds =
        new(StringComparer.Ordinal)
        {
            ["emma"] = "101",
            ["james"] = "102",
            ["sarah"] = "103",
            ["david"] = "104",
            ["lisa"] = "105",
            ["mike"] = "106",
            ["anna"] = "107",
            ["tom"] = "108",
        };

    [Fact]
    public void Powershell_TestUserNicknames_HasEightCanonicalKeys()
    {
        var (keys, _) = ParsePowershellHashtable();

        keys.Should().BeEquivalentTo(ExpectedKeys, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Bash_TestUserKeys_HasEightCanonicalKeys()
    {
        var keys = ParseBashArray("TEST_USER_KEYS");

        keys.Should().BeEquivalentTo(ExpectedKeys, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Terraform_TestUsers_HasEightCanonicalKeys()
    {
        var keys = ParseTerraformTestUserKeys();

        keys.Should().BeEquivalentTo(ExpectedKeys);
    }

    [Fact]
    public void Powershell_And_Bash_Agree_On_Mail_Nicknames()
    {
        var (psKeys, psNicks) = ParsePowershellHashtable();
        var bashKeys = ParseBashArray("TEST_USER_KEYS");
        var bashNicks = ParseBashArray("TEST_USER_NICKS");

        psKeys.Should().Equal(bashKeys, "PS1 and SH MUST agree on the user-key order");
        bashNicks.Should().HaveCount(bashKeys.Count, "TEST_USER_KEYS and TEST_USER_NICKS must align");

        for (int i = 0; i < psKeys.Count; i++)
        {
            psNicks[i].Should().Be(
                bashNicks[i],
                $"key '{psKeys[i]}' must have matching nickname in PS1 and SH");
        }
    }

    [Fact]
    public void Powershell_Nicknames_Match_Terraform_Stem_Plus_LocalSuffix()
    {
        // PS1 stores fully-suffixed nicknames (e.g. "emma.wilson-local"); TF
        // stores the unsuffixed stem ("emma.wilson") and applies the suffix
        // via mail_nickname_suffix at apply-time. They must agree on the
        // stem so terraform apply touches the same UPNs the cleanup deletes.
        var (psKeys, psNicks) = ParsePowershellHashtable();
        var tfNicks = ParseTerraformTestUserMailNicknames();

        foreach (var key in psKeys)
        {
            tfNicks.Should().ContainKey(key, $"Terraform must define test user '{key}'");
            var psStem = psNicks[psKeys.IndexOf(key)].Replace("-local", string.Empty);
            psStem.Should().Be(
                tfNicks[key],
                $"PS1 stem for '{key}' must match Terraform mail_nickname");
        }
    }

    [Fact]
    public void Terraform_TestUsers_CustomerIds_Match_Canonical_Mapping()
    {
        // A swap of customer_id values between users in variables.tf would
        // grant the wrong Entra OID access to the wrong customer's orders
        // — a far worse bug than a nickname typo, and one the other tests
        // would silently miss. Pin the mapping explicitly here.
        var actual = ParseTerraformTestUserCustomerIds();

        actual.Should().BeEquivalentTo(
            ExpectedCustomerIds,
            "Terraform test_users must keep its canonical user -> customer-id mapping; " +
            "a swap here is a privilege-escalation bug across local-dev sessions");
    }

    private static (List<string> Keys, List<string> Nicks) ParsePowershellHashtable()
    {
        var path = Path.Combine(RepoRoot, "infra", "setup-local.ps1");
        // Reviewer LOW: prevent silent passes when test DLL is relocated
        // and RepoRoot points at a missing directory.
        File.Exists(path).Should().BeTrue(
            $"setup-local.ps1 must exist at the resolved path (was '{path}'). " +
            "If the test DLL was relocated, fix RepoRoot resolution.");
        var content = File.ReadAllText(path);

        // Match `$TestUserNicknames = [ordered]@{ ... }`. The `[ordered]`
        // prefix is allowed to be absent (a developer might drop it) so
        // this test isn't accidentally fragile. We still require a SINGLE
        // hashtable opener and walk braces to find the matching close —
        // not regex `[^}]+`, which would silently truncate at any inner
        // `}` if a future value contained one.
        var openMatch = Regex.Match(
            content,
            @"\$TestUserNicknames\s*=\s*(?:\[ordered\]\s*)?@\{");
        openMatch.Success.Should().BeTrue(
            "setup-local.ps1 must define $TestUserNicknames as a hashtable (ordered or not)");

        var bodyStart = openMatch.Index + openMatch.Length;
        var depth = 1;
        var bodyEnd = bodyStart;
        for (; bodyEnd < content.Length && depth > 0; bodyEnd++)
        {
            if (content[bodyEnd] == '{') depth++;
            else if (content[bodyEnd] == '}') depth--;
        }
        depth.Should().Be(0, "unbalanced braces in $TestUserNicknames hashtable");
        var body = content.Substring(bodyStart, bodyEnd - bodyStart - 1);

        var entryRegex = new Regex(
            @"^\s*(?<key>\w+)\s*=\s*""(?<value>[^""]+)""",
            RegexOptions.Multiline);

        var keys = new List<string>();
        var nicks = new List<string>();
        foreach (Match m in entryRegex.Matches(body))
        {
            keys.Add(m.Groups["key"].Value);
            nicks.Add(m.Groups["value"].Value);
        }
        // Reviewer DEFER mitigation: prevent silent passes if the parser
        // matched the opener but extracted no entries.
        keys.Should().NotBeEmpty(
            "PowerShell hashtable parser found the opener but no `key = \"value\"` entries; " +
            "the script format may have changed in a way the parser doesn't understand.");
        return (keys, nicks);
    }

    private static List<string> ParseBashArray(string variableName)
    {
        var path = Path.Combine(RepoRoot, "infra", "setup-local.sh");
        File.Exists(path).Should().BeTrue(
            $"setup-local.sh must exist at the resolved path (was '{path}').");
        var content = File.ReadAllText(path);

        var match = Regex.Match(
            content,
            $@"{Regex.Escape(variableName)}=\((?<body>[^)]+)\)");
        match.Success.Should().BeTrue($"setup-local.sh must define {variableName}");

        var entries = match.Groups["body"].Value
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !token.StartsWith("#", StringComparison.Ordinal))
            .ToList();
        entries.Should().NotBeEmpty(
            $"Bash array '{variableName}' parser found the opener but no entries; " +
            "the script format may have changed.");
        return entries;
    }

    private static List<string> ParseTerraformTestUserKeys()
    {
        var path = Path.Combine(
            RepoRoot, "infra", "terraform", "modules", "entra", "v1", "variables.tf");
        var content = File.ReadAllText(path);

        // Find the test_users default body via brace-walking. Regex alone
        // can't handle nested objects, so we locate the `default = {`
        // opener and walk braces to find its matching closer.
        var body = ExtractTerraformTestUsersDefaultBody(content);

        // Top-level keys are at brace depth 0 within the body; sub-attrs at
        // depth 1+. Walk char by char tracking depth.
        var keys = new List<string>();
        var depth = 0;
        var lineStart = true;
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '{') { depth++; lineStart = true; current.Clear(); continue; }
            if (c == '}') { depth--; lineStart = true; current.Clear(); continue; }
            if (c == '\n') { lineStart = true; current.Clear(); continue; }
            if (depth == 0 && lineStart)
            {
                if (char.IsLetter(c) || c == '_')
                {
                    current.Append(c);
                }
                else if (c == '=' && current.Length > 0)
                {
                    keys.Add(current.ToString().Trim());
                    current.Clear();
                    lineStart = false;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    current.Clear();
                    lineStart = false;
                }
            }
        }
        return keys;
    }

    private static string ExtractTerraformTestUsersDefaultBody(string content)
    {
        // 1. Find the start of `variable "test_users" {`.
        var varStart = content.IndexOf("variable \"test_users\"", StringComparison.Ordinal);
        varStart.Should().BeGreaterThan(-1, "variables.tf must define a `variable \"test_users\"` block");

        // 2. From there, find the FIRST `default = {` (i.e. the keyword
        // `default` followed by `=` and a `{`) — not a comment or
        // description that happens to contain the word "default". Match
        // `default` at a word boundary, then `=` then `{`, allowing
        // whitespace/newlines between them.
        var defaultMatch = Regex.Match(
            content[varStart..],
            @"\bdefault\s*=\s*\{");
        defaultMatch.Success.Should().BeTrue(
            "test_users variable must have a `default = { ... }` block in variables.tf. " +
            "If the default was moved to a `.tfvars` file, this fitness test must be updated.");
        var openBrace = varStart + defaultMatch.Index + defaultMatch.Length - 1;

        // 3. Walk braces from the opener to find the matching closer.
        var depth = 0;
        for (int i = openBrace; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content.Substring(openBrace + 1, i - openBrace - 1);
                }
            }
        }
        throw new InvalidOperationException("Unbalanced braces in test_users default block.");
    }

    private static Dictionary<string, string> ParseTerraformTestUserMailNicknames() =>
        ParseTerraformTestUserAttribute("mail_nickname");

    private static Dictionary<string, string> ParseTerraformTestUserCustomerIds() =>
        ParseTerraformTestUserAttribute("customer_id");

    private static Dictionary<string, string> ParseTerraformTestUserAttribute(string attributeName)
    {
        var path = Path.Combine(
            RepoRoot, "infra", "terraform", "modules", "entra", "v1", "variables.tf");
        File.Exists(path).Should().BeTrue(
            $"variables.tf must exist at the resolved path (was '{path}').");
        var content = File.ReadAllText(path);

        var body = ExtractTerraformTestUsersDefaultBody(content);

        // Walk braces to find each top-level `<key> = {  ...  }` pair, then
        // grep its inner body for `<attribute> = "..."`. Returns ALL parsed
        // users (does not filter to ExpectedKeys); callers can compare key
        // sets with .Should().Equal(...).
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var attrRegex = new Regex(
            $@"\b{Regex.Escape(attributeName)}\s*=\s*""(?<v>[^""]+)""");
        var i = 0;
        while (i < body.Length)
        {
            while (i < body.Length && !char.IsLetter(body[i]) && body[i] != '_')
            {
                i++;
            }
            if (i >= body.Length) break;

            var keyStart = i;
            while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '_'))
            {
                i++;
            }
            var key = body.Substring(keyStart, i - keyStart);

            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length || body[i] != '=') { continue; }
            i++;
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length || body[i] != '{') { continue; }

            var braceOpen = i;
            var depth = 0;
            for (; i < body.Length; i++)
            {
                if (body[i] == '{') depth++;
                else if (body[i] == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }

            var inner = body.Substring(braceOpen, i - braceOpen);
            var attrMatch = attrRegex.Match(inner);
            if (attrMatch.Success)
            {
                result[key] = attrMatch.Groups["v"].Value;
            }
        }
        result.Should().NotBeEmpty(
            $"Terraform parser found 0 user blocks with `{attributeName}` — " +
            "the variables.tf format may have changed.");
        return result;
    }
}
