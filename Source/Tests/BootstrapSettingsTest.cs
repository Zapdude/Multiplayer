using Multiplayer.Common;
using Multiplayer.Common.Util;

namespace Tests;

public class BootstrapSettingsTest
{
    [Test]
    public void SharedTomlSerializerIncludesJoinCriticalFields()
    {
        var settings = new ServerSettings
        {
            gameName = "Bootstrap Test",
            lanAddress = "192.168.1.15",
            direct = true,
            lan = true,
        };

        var toml = TomlSettings.Serialize(settings);

        Assert.That(toml, Does.Contain("gameName = \"Bootstrap Test\""));
        Assert.That(toml, Does.Contain("lanAddress = \"192.168.1.15\""));
    }

    [Test]
    public void RoundTripsServerSettingsThroughToml()
    {
        // Serialization alone does not prove the reader still maps the values back.
        var settings = new ServerSettings
        {
            gameName = "Round Trip",
            lanAddress = "10.0.0.5",
            directAddress = "0.0.0.0:30502",
            maxPlayers = 12,
            steam = true,
            direct = true,
            lan = false,
            multifaction = true,
            autoJoinPoint = AutoJoinPointFlags.Desync,
            pauseOnJoin = false,
        };

        var path = Path.Combine(Path.GetTempPath(), $"mp-toml-{Guid.NewGuid():N}.toml");
        try
        {
            File.WriteAllText(path, TomlSettings.Serialize(settings));
            var loaded = TomlSettings.Load(path);

            Assert.That(loaded.gameName, Is.EqualTo("Round Trip"));
            Assert.That(loaded.lanAddress, Is.EqualTo("10.0.0.5"));
            Assert.That(loaded.maxPlayers, Is.EqualTo(12));
            Assert.That(loaded.steam, Is.True);
            Assert.That(loaded.direct, Is.True);
            Assert.That(loaded.lan, Is.False);
            Assert.That(loaded.multifaction, Is.True);
            Assert.That(loaded.autoJoinPoint, Is.EqualTo(AutoJoinPointFlags.Desync));
            Assert.That(loaded.pauseOnJoin, Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
