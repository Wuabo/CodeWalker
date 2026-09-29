using CodeWalker.GameFiles;
using Xunit;

namespace CodeWalker.Core.Tests;

public class DoorAudioDocumentTests
{
    [Fact]
    public void BuildsDoorAudioSettingsAndLinkRel()
    {
        var doc = new DoorAudioDocument();
        doc.Doors.Add(new DoorAudioEntry
        {
            Name = "wuabo_storage_g_door",
            Sounds = "null",
            TuningParams = "null",
            MaxOcclusion = 0.7f
        });

        var xml = doc.ToRelXml();
        Assert.Contains("type=\"DoorAudioSettings\"", xml);
        Assert.Contains("type=\"DoorAudioSettingsLink\"", xml);
        Assert.Contains("<Name>d_wuabo_storage_g_door</Name>", xml);
        Assert.DoesNotContain("type=\"DoorModel\"", xml);
        Assert.DoesNotContain("type=\"Door\"", xml);

        var rel = doc.ToRel();
        Assert.Equal(RelDatFileType.Dat151, rel.RelType);
        Assert.Contains(rel.RelDatas, d => d is Dat151DoorAudioSettings);
        Assert.Contains(rel.RelDatas, d => d is Dat151DoorAudioSettingsLink);

        var bytes = doc.SaveRelBytes();
        Assert.True(bytes.Length > 32);

        var nt = doc.BuildNameTableText();
        Assert.Contains("d_wuabo_storage_g_door", nt);

        var tmp = Path.Combine(Path.GetTempPath(), "cw-door-audio-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);
        try
        {
            var relPath = Path.Combine(tmp, "doors.dat151.rel");
            doc.Export(relPath);
            Assert.True(File.Exists(relPath));
            Assert.True(File.Exists(Path.Combine(tmp, "doors.dat151.nametable")));
            Assert.True(new FileInfo(relPath).Length > 32);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ImportsLegacyDoorAndDoorModelXml()
    {
        const string legacy = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Dat151>
              <Version value="9458585" />
              <Items>
                <Item type="Door" ntOffset="0">
                  <Name>d_my_custom_door</Name>
                  <SoundSet>hash_f1e8d9fe</SoundSet>
                  <Params>hash_0246d335</Params>
                  <Unk1 value="0.7" />
                </Item>
                <Item type="DoorModel" ntOffset="0">
                  <Name>dasl_aabbccdd</Name>
                  <Door>d_my_custom_door</Door>
                </Item>
              </Items>
            </Dat151>
            """;

        Assert.True(DoorAudioDocument.LooksLikeLegacyDoorXml(legacy));
        var doc = DoorAudioDocument.FromXml(legacy);
        Assert.Equal(9458585u, doc.Version);
        Assert.Single(doc.Doors);
        Assert.Equal("my_custom_door", doc.Doors[0].Name);
        Assert.Equal("hash_f1e8d9fe", doc.Doors[0].Sounds);
        Assert.Equal("hash_0246d335", doc.Doors[0].TuningParams);
        Assert.Equal(0.7f, doc.Doors[0].MaxOcclusion);
        Assert.Equal("dasl_aabbccdd", doc.Doors[0].LinkName);

        var cwXml = doc.ToRelXml();
        Assert.Contains("type=\"DoorAudioSettings\"", cwXml);
        Assert.Contains("type=\"DoorAudioSettingsLink\"", cwXml);
        Assert.DoesNotContain("type=\"Door\"", cwXml);
        Assert.DoesNotContain("type=\"DoorModel\"", cwXml);
    }

    [Fact]
    public void ModelHashMatchesJenkinsLowercase()
    {
        var name = "Prop_Test_Door";
        var expected = JenkHash.GenHash(name.ToLowerInvariant());
        Assert.Equal(expected, DoorAudioDocument.GetModelHash(name));
        Assert.Equal($"dasl_{expected:x8}", DoorAudioDocument.GetAutoLinkName(name));
    }
}
