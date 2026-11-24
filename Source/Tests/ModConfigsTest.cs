using Multiplayer.Client;

namespace Tests;

// A mod can register several settings classes, so the wire carries a list per mod and both sides
// compare sets. The regression this documents: a client whose settings were identical to the host's
// still reported a mismatch, because the host transmitted one file of several and the client
// compared against all of them.
[TestFixture]
public class ModConfigsTest
{
    private static ModConfig Config(string mod, string file, string contents) => new(mod, file, contents);

    [Test]
    public void SameSingleConfigMatches()
    {
        Assert.That(ModConfigs.Match(
            [Config("a.mod", "ModA_Settings", "<x/>")],
            [Config("a.mod", "ModA_Settings", "<x/>")]), Is.True);
    }

    [Test]
    public void SeveralConfigsPerModMatch()
    {
        // 3 and 9 files for one mod: the shape Vanilla Expanded Framework produces.
        var three = Enumerable.Range(0, 3).Select(i => Config("a.mod", $"ModA_Settings{i}", $"<x>{i}</x>")).ToList();
        var nine = Enumerable.Range(0, 9).Select(i => Config("a.mod", $"ModA_Settings{i}", $"<x>{i}</x>")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigs.Match(three, three.ToList()), Is.True, "3 vs 3");
            Assert.That(ModConfigs.Match(nine, nine.ToList()), Is.True, "9 vs 9");
        });
    }

    [Test]
    public void MissingOneConfigDoesNotMatch()
    {
        var all = Enumerable.Range(0, 4).Select(i => Config("a.mod", $"Settings{i}", "<x/>")).ToList();
        var fewer = all.Take(3).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigs.Match(fewer, all), Is.False, "fewer remote");
            Assert.That(ModConfigs.Match(all, fewer), Is.False, "fewer local");
        });
    }

    [Test]
    public void LineEndingsDoNotMatter()
    {
        Assert.That(ModConfigs.Match(
            [Config("a.mod", "Settings", "<x/>\r\n<y/>")],
            [Config("a.mod", "Settings", "<x/>\n<y/>")]), Is.True);
    }

    [Test]
    public void OrderDoesNotMatter()
    {
        var first = Config("a.mod", "A", "<a/>");
        var second = Config("a.mod", "B", "<b/>");

        Assert.That(ModConfigs.Match([first, second], [second, first]), Is.True);
    }

    [Test]
    public void DifferentFileNameDoesNotMatch()
    {
        Assert.That(ModConfigs.Match(
            [Config("a.mod", "SettingsA", "<x/>")],
            [Config("a.mod", "SettingsB", "<x/>")]), Is.False);
    }

    [Test]
    public void DifferentModDoesNotMatch()
    {
        Assert.That(ModConfigs.Match(
            [Config("a.mod", "Settings", "<x/>")],
            [Config("b.mod", "Settings", "<x/>")]), Is.False);
    }

    [Test]
    public void DifferentContentsDoNotMatch()
    {
        Assert.That(ModConfigs.Match(
            [Config("a.mod", "Settings", "<x/>")],
            [Config("a.mod", "Settings", "<x><y/></x>")]), Is.False);
    }

    [Test]
    public void EmptyMatchesEmpty()
    {
        Assert.That(ModConfigs.Match([], []), Is.True);
    }

    [Test]
    public void EmptyDoesNotMatchOne()
    {
        Assert.That(ModConfigs.Match([], [Config("a.mod", "Settings", "<x/>")]), Is.False);
    }
}
