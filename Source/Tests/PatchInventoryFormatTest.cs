using System.Text.Json;
using Multiplayer.Client.Desyncs;

namespace Tests;

// The inventory is a dump, so the only things worth testing are its shape, its escaping and its
// bounds: a report must stay readable and must say what it dropped instead of dropping it silently.
[TestFixture]
public class PatchInventoryFormatTest
{
    private static PatchRow Row(string target, string kind = "Prefix", string owner = "Multiplayer", int priority = 400) =>
        new(target, "Assembly-CSharp", 123, kind, owner, priority,
            "Multiplayer.Client.Patches.SomePatch.Prefix", "Multiplayer", 456, "rwmt.multiplayer");

    private static string Json(IReadOnlyList<PatchRow> rows, int omitted = 0) =>
        PatchInventoryFormat.Json(rows, omitted, "2.4.2.0", 10, rows.Count, ["Multiplayer"]);

    [Test]
    public void WritesOneObjectPerPatch()
    {
        using var doc = JsonDocument.Parse(Json([Row("Verse.Pawn.Kill"), Row("Verse.Thing.Spawn", "Postfix")]));
        var rows = doc.RootElement.GetProperty("rows");

        Assert.Multiple(() =>
        {
            Assert.That(rows.GetArrayLength(), Is.EqualTo(2));
            Assert.That(rows[0].GetProperty("target").GetString(), Is.EqualTo("Verse.Pawn.Kill"));
            Assert.That(rows[0].GetProperty("kind").GetString(), Is.EqualTo("Prefix"));
            Assert.That(rows[0].GetProperty("package").GetString(), Is.EqualTo("rwmt.multiplayer"));
            Assert.That(rows[1].GetProperty("kind").GetString(), Is.EqualTo("Postfix"));
        });
    }

    [Test]
    public void HeaderCarriesTheCountsAndOwners()
    {
        using var doc = JsonDocument.Parse(Json([Row("a"), Row("b")]));
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("harmonyVersion").GetString(), Is.EqualTo("2.4.2.0"));
            Assert.That(root.GetProperty("patchedMethods").GetInt32(), Is.EqualTo(10));
            Assert.That(root.GetProperty("patches").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("written").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("omitted").GetInt32(), Is.EqualTo(0));
            Assert.That(root.GetProperty("owners")[0].GetString(), Is.EqualTo("Multiplayer"));
        });
    }

    [Test]
    public void EscapesCharactersThatWouldBreakTheJson()
    {
        const string nasty = "Quote\" backslash\\ newline\n tab\t control\u0001";
        using var doc = JsonDocument.Parse(Json([Row(nasty)]));

        Assert.That(doc.RootElement.GetProperty("rows")[0].GetProperty("target").GetString(), Is.EqualTo(nasty));
    }

    [Test]
    public void CapsTheRowCountAndSaysHowManyWereDropped()
    {
        var rows = Enumerable.Range(0, PatchInventoryFormat.MaxRows + 7)
            .Select(i => Row($"Target{i}"))
            .ToList();

        using var doc = JsonDocument.Parse(Json(rows));
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("written").GetInt32(), Is.EqualTo(PatchInventoryFormat.MaxRows));
            Assert.That(root.GetProperty("omitted").GetInt32(), Is.EqualTo(7));
            Assert.That(root.GetProperty("rows").GetArrayLength(), Is.EqualTo(PatchInventoryFormat.MaxRows));
        });
    }

    [Test]
    public void CarriesOverAnOmittedCountFromTheCaller()
    {
        using var doc = JsonDocument.Parse(Json([Row("a")], omitted: 3));

        Assert.That(doc.RootElement.GetProperty("omitted").GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public void CapsTheByteSize()
    {
        var rows = Enumerable.Range(0, 40)
            .Select(i => Row(new string('x', 100_000)))
            .ToList();

        var json = Json(rows);
        using var doc = JsonDocument.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(json.Length, Is.LessThanOrEqualTo(PatchInventoryFormat.MaxBytes + 2048));
            Assert.That(doc.RootElement.GetProperty("omitted").GetInt32(), Is.GreaterThan(0));
            Assert.That(doc.RootElement.GetProperty("written").GetInt32(), Is.LessThan(40));
        });
    }

    [Test]
    public void EmptyInventoryIsStillValidJson()
    {
        using var doc = JsonDocument.Parse(Json([]));

        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.GetProperty("rows").GetArrayLength(), Is.EqualTo(0));
            Assert.That(doc.RootElement.GetProperty("owners").GetArrayLength(), Is.EqualTo(1));
        });
    }
}
