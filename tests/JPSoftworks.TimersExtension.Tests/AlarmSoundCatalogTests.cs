// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Text;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class AlarmSoundCatalogTests
{
    [Fact]
    public void CatalogHasUniqueStableIds()
    {
        Assert.Equal(
            AlarmSoundCatalog.All.Count,
            AlarmSoundCatalog.All.Select(sound => sound.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CatalogStartsWithDefaultSound()
    {
        Assert.Equal(AlarmSoundCatalog.DefaultId, AlarmSoundCatalog.All[0].Id);
    }

    [Fact]
    public void UnknownSoundFallsBackToDefault()
    {
        Assert.Equal(AlarmSoundCatalog.DefaultId, AlarmSoundCatalog.Get("missing-sound").Id);
    }

    [Fact]
    public void EveryCatalogSoundIsAValidPackagedWaveFile()
    {
        foreach (var sound in AlarmSoundCatalog.All)
        {
            if (sound.IsSilent)
            {
                Assert.Null(sound.FileName);
                Assert.Null(sound.Uri);
                continue;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound.FileName!);
            Assert.Equal(new Uri(path).AbsoluteUri, sound.Uri);
            Assert.True(File.Exists(path), $"Missing alarm sound: {sound.FileName}");

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, false);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Assert.True(stream.Length > 44, $"Alarm sound is empty: {sound.FileName}");
            stream.Position = 8;
            Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        }
    }
}