using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tests;

// The version lives in four files because four consumers read it: version.json drives the build,
// Version.cs is compiled into the assemblies, About.xml is what the mod manager displays, and
// Manifest.xml is what an installed copy fetches to learn that a new release exists. They stay in
// sync only because build/apply-version.sh rewrites three of them. A hand edit can drift one, and
// the symptom is a release that advertises the wrong version or points at the previous tag.
[TestFixture]
public class VersionConsistencyTest
{
    [Test]
    public void VersionJsonMatchesCompiledVersion()
    {
        var root = RepositoryRoot();
        var meta = ReadVersionJson(root);
        var source = File.ReadAllText(Path.Combine(root, "Source", "Common", "Version.cs"));

        Assert.That(ReadStringConst(source, "SimpleVersion"), Is.EqualTo(meta.Version),
            "Version.cs SimpleVersion drifted from version.json");
        Assert.That(ReadStringConst(source, "AssemblyVersion"), Is.EqualTo(meta.AssemblyVersion),
            "Version.cs AssemblyVersion drifted from version.json");
        Assert.That(ReadIntConst(source, "Protocol"), Is.EqualTo(meta.Protocol),
            "Version.cs Protocol drifted from version.json");
    }

    [Test]
    public void ShippedMetadataMatchesVersionJson()
    {
        var root = RepositoryRoot();
        var meta = ReadVersionJson(root);

        var about = XDocument.Load(Path.Combine(root, "About", "About.xml"));
        var modVersion = about.Root!.Element("modVersion")!.Value.Trim();
        Assert.That(StripCommitHash(modVersion), Is.EqualTo(meta.Version),
            "About.xml modVersion drifted from version.json");

        var supported = about.Root!.Element("supportedVersions")!.Elements("li")
            .Select(element => element.Value.Trim()).ToList();
        Assert.That(supported, Does.Contain(meta.Rimworld),
            "About.xml supportedVersions does not list the version.json rimworld value");

        var manifest = XDocument.Load(Path.Combine(root, "About", "Manifest.xml"));
        var expectedTag = $"v{meta.Version}{meta.Suffix}";
        var download = manifest.Root!.Element("downloadUri")!.Value.Trim();
        Assert.That(download, Does.EndWith($"/releases/tag/{expectedTag}"),
            $"Manifest.xml downloadUri does not name the derived tag {expectedTag}");

        var update = manifest.Root!.Element("manifestUri")!.Value.Trim();
        Assert.That(update, Does.Not.Contain("/releases/"),
            "Manifest.xml manifestUri must stay on a branch, or installed copies stop seeing updates");
    }

    private sealed record VersionMetadata(
        string Version,
        string AssemblyVersion,
        int Protocol,
        string Rimworld,
        string Suffix);

    private static VersionMetadata ReadVersionJson(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "version.json")));
        var json = document.RootElement;
        return new VersionMetadata(
            json.GetProperty("version").GetString()!,
            json.GetProperty("assemblyVersion").GetString()!,
            json.GetProperty("protocol").GetInt32(),
            json.GetProperty("rimworld").GetString()!,
            json.TryGetProperty("releaseTagSuffix", out var suffix) ? suffix.GetString() ?? "" : "");
    }

    private static string ReadStringConst(string source, string name)
    {
        var match = Regex.Match(source, "public const string " + name + " = \"([^\"]*)\";");
        Assert.That(match.Success, Is.True, $"{name} not found in Version.cs");
        return match.Groups[1].Value;
    }

    private static int ReadIntConst(string source, string name)
    {
        var match = Regex.Match(source, "public const int " + name + @" = (\d+);");
        Assert.That(match.Success, Is.True, $"{name} not found in Version.cs");
        return int.Parse(match.Groups[1].Value);
    }

    private static string StripCommitHash(string modVersion) =>
        Regex.Replace(modVersion, @" \([0-9a-fA-F]{7,40}\)$", "");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "version.json")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find version.json above {TestContext.CurrentContext.TestDirectory}");
    }
}
