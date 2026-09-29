using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Xunit;

namespace CodeWalker.Core.Tests;

public class DoorTuningVanillaParseTests
{
    [Fact]
    public void ParsesBarrierArmCustomBoxTriggerAndOffsetFromVanillaXml()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "GTA5-Door-Editor", "src", "assets", "vanilla", "doortuning.ymt"));
        if (!File.Exists(path))
            return;

        var doc = DoorTuningDocument.FromXml(File.ReadAllText(path));
        var t = doc.NamedTunings.First(x => x.Name == "BarrierArmCustomBox").Tuning;

        Assert.Equal(0f, t.AutoOpenVolumeOffsetX);
        Assert.Equal(-3f, t.AutoOpenVolumeOffsetY);
        Assert.Equal(-3.5f, t.AutoOpenVolumeOffsetZ);
        Assert.Equal(-3f, t.TriggerBoxMinX);
        Assert.Equal(-3f, t.TriggerBoxMinY);
        Assert.Equal(-1.5f, t.TriggerBoxMinZ);
        Assert.Equal(3f, t.TriggerBoxMaxX);
        Assert.Equal(3f, t.TriggerBoxMaxY);
        Assert.Equal(4.25f, t.TriggerBoxMaxZ);
        Assert.True(t.CustomTriggerBox);
        Assert.True(t.UseAutoOpenTriggerBox);
    }
}
