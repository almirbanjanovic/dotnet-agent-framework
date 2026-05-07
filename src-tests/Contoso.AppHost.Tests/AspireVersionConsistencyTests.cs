using System.Text.RegularExpressions;
using FluentAssertions;

namespace Contoso.AppHost.Tests;

/// <summary>
/// Pins the Aspire AppHost SDK version (declared in
/// <c>src/AppHost/Contoso.AppHost.csproj</c> as
/// <c>&lt;Sdk Name="Aspire.AppHost.Sdk" Version="X.Y.Z" /&gt;</c>) to the
/// same MAJOR version as the centrally-managed
/// <c>Aspire.Hosting.AppHost</c> NuGet package in
/// <c>Directory.Packages.props</c>.
///
/// Why this matters: this drift already shipped once (SDK pinned at 9.0.0
/// while the package was 13.2.4). The two versions MUST stay aligned;
/// otherwise the AppHost build can resolve incompatible MSBuild targets
/// and Aspire-specific tasks against a different version of the runtime
/// types they orchestrate.
/// </summary>
public class AspireVersionConsistencyTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void AppHostSdk_And_HostingPackage_Major_Versions_Match()
    {
        var sdkVersion = ReadAspireAppHostSdkVersion();
        var pkgVersion = ReadAspireHostingPackageVersion();

        var sdkMajor = ParseMajor(sdkVersion);
        var pkgMajor = ParseMajor(pkgVersion);

        sdkMajor.Should().Be(
            pkgMajor,
            $"Aspire.AppHost.Sdk (version {sdkVersion}) MUST be on the same major as " +
            $"Aspire.Hosting.AppHost (version {pkgVersion}). Cross-major drift causes " +
            "AppHost build/restore failures.");
    }

    private static string ReadAspireAppHostSdkVersion()
    {
        var path = Path.Combine(RepoRoot, "src", "AppHost", "Contoso.AppHost.csproj");
        File.Exists(path).Should().BeTrue(
            $"src/AppHost/Contoso.AppHost.csproj must exist at '{path}'.");
        var content = File.ReadAllText(path);

        var match = Regex.Match(
            content,
            @"<Sdk\s+Name=""Aspire\.AppHost\.Sdk""\s+Version=""(?<v>[^""]+)""");
        match.Success.Should().BeTrue(
            "Contoso.AppHost.csproj must declare an Aspire.AppHost.Sdk import with a Version.");
        return match.Groups["v"].Value;
    }

    private static string ReadAspireHostingPackageVersion()
    {
        var path = Path.Combine(RepoRoot, "Directory.Packages.props");
        File.Exists(path).Should().BeTrue(
            $"Directory.Packages.props must exist at '{path}'.");
        var content = File.ReadAllText(path);

        var match = Regex.Match(
            content,
            @"<PackageVersion\s+Include=""Aspire\.Hosting\.AppHost""\s+Version=""(?<v>[^""]+)""");
        match.Success.Should().BeTrue(
            "Directory.Packages.props must define a PackageVersion for Aspire.Hosting.AppHost.");
        return match.Groups["v"].Value;
    }

    private static int ParseMajor(string version)
    {
        // Strip any pre-release suffix (e.g. "13.2.4-preview.1") before parsing.
        var dashIdx = version.IndexOf('-');
        var clean = dashIdx > 0 ? version[..dashIdx] : version;
        var firstDot = clean.IndexOf('.');
        firstDot.Should().BeGreaterThan(0, $"version '{version}' must include a major component");
        return int.Parse(clean[..firstDot], System.Globalization.CultureInfo.InvariantCulture);
    }
}
