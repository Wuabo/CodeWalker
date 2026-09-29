using CodeWalker.GameFiles;
using Xunit;

namespace CodeWalker.Core.Tests;

public class DoorTuningDocumentTests
{
    [Fact]
    public void RoundTripsNamedTuningAndMapping()
    {
        var doc = new DoorTuningDocument();
        doc.NamedTunings.Add(new DoorNamedTuning
        {
            Name = "test_tune",
            Tuning = new DoorTuningParams
            {
                AutoOpenRate = 1.5f,
                MassMultiplier = 2f,
                BreakableByVehicle = true,
                StdDoorRotDir = "StdDoorOpenBothDir",
                Flags = new System.Collections.Generic.List<string> { "AutoOpensForAllVehicles" }
            }
        });
        doc.ModelMappings.Add(new DoorModelMapping
        {
            ModelName = "prop_test_door",
            TuningName = "test_tune"
        });

        var xml = doc.ToXml();
        Assert.Contains("CDoorTuningFile", xml);
        Assert.Contains("test_tune", xml);
        Assert.Contains("prop_test_door", xml);

        var bytes = doc.Save();
        Assert.True(bytes.Length > 16);

        var ymt = doc.ToYmt();
        Assert.Equal(YmtFileContentType.DoorTuning, ymt.ContentType);
        Assert.NotNull(ymt.DoorTuning);
        Assert.Single(ymt.DoorTuning!.NamedTunings);
        Assert.Equal("test_tune", ymt.DoorTuning.NamedTunings[0].Name);
    }
}
