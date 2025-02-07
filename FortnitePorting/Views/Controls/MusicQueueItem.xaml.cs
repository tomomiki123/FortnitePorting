﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using CSCore;
using CSCore.Codecs.OGG;
using CSCore.Codecs.WAV;
using CSCore.CoreAudioAPI;
using CSCore.SoundOut;
using CUE4Parse_Conversion.Sounds;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Sound;
using CUE4Parse.UE4.Assets.Exports.Sound.Node;
using CUE4Parse.Utils;
using FortnitePorting.Exports;
using FortnitePorting.Services;

namespace FortnitePorting.Views.Controls;

public partial class MusicQueueItem : IDisposable
{
    public ImageSource MusicImageSource { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }

    private readonly UObject Asset;
    private ISoundOut? SoundOut;
    private IWaveSource? SoundSource;
    private static readonly MMDeviceEnumerator DeviceEnumerator = new();

    public MusicQueueItem(IExportableAsset musicPackItem)
    {
        InitializeComponent();
        DataContext = this;

        MusicImageSource = musicPackItem.FullSource;
        DisplayName = musicPackItem.DisplayName;
        Description = musicPackItem.Description;
        Asset = musicPackItem.Asset;
    }

    public MusicQueueItem(UObject asset, ImageSource source, string displayName, string description)
    {
        InitializeComponent();
        DataContext = this;

        MusicImageSource = source;
        DisplayName = displayName;
        Description = description;
        Asset = asset;
    }

    public static USoundWave GetProperSoundWave(UObject asset)
    {
        var musicCue = asset.Get<USoundCue>("FrontEndLobbyMusic");
        var sounds = ExportHelpers.HandleAudioTree(musicCue.FirstNode!.Load<USoundNode>()!);
        var properSound = sounds.MaxBy(sound => sound.Time);
        return properSound?.SoundWave;
    }

    public void Initialize()
    {
        var properSoundWave = GetProperSoundWave(Asset);
        properSoundWave.Decode(true, out var format, out var data);
        if (data is null)
        {
            properSoundWave.Decode(false, out format, out data);
        }
        if (data is null) return;

        switch (format.ToLower())
        {
            case "ogg":
                SoundSource = new OggSource(new MemoryStream(data)).ToWaveSource();
                break;
            case "adpcm":
                SoundSource = new WaveFileReader(ConvertedDataVGMStream(data));
                break;
            case "binka":
                SoundSource = new WaveFileReader(ConvertedDataBinkadec(data));
                break;
        }

        SoundOut = GetSoundOut();
        SoundOut.Initialize(SoundSource);
        SoundOut.Play();

        DiscordService.UpdateMusicState(DisplayName);
    }

    public static MemoryStream ConvertedDataVGMStream(byte[] data)
    {
        var vgmPath = Path.Combine(App.VGMStreamFolder.FullName, "vgmstream-cli.exe");

        var adpcmPath = Path.Combine(App.VGMStreamFolder.FullName, "temp.adpcm");
        var wavPath = Path.ChangeExtension(adpcmPath, ".wav");

        File.WriteAllBytes(adpcmPath, data);

        var vgmInst = Process.Start(new ProcessStartInfo
        {
            FileName = vgmPath,
            Arguments = $"-o \"{wavPath}\" \"{adpcmPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        vgmInst?.WaitForExit();

        var memoryStream = new MemoryStream(File.ReadAllBytes(wavPath));

        File.Delete(adpcmPath);
        File.Delete(wavPath);

        return memoryStream;
    }
    
    public static MemoryStream ConvertedDataBinkadec(byte[] data)
    {
        var binkadecPath = Path.Combine(App.VGMStreamFolder.FullName, "binkadec.exe");
        var ffmpegPath = Path.Combine(App.VGMStreamFolder.FullName, "ffmpeg.exe");

        var binkaPath = Path.Combine(App.VGMStreamFolder.FullName, "temp.binka");
        var binPath = Path.ChangeExtension(binkaPath, ".bin");
        var wavPath = Path.ChangeExtension(binkaPath, ".wav");

        File.WriteAllBytes(binkaPath, data);

        var vgmInst = Process.Start(new ProcessStartInfo
        {
            FileName = binkadecPath,
            Arguments = $"-i \"{binkaPath}\" -o \"{binPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        });
        vgmInst?.WaitForExit();
        var output = vgmInst.StandardOutput.ReadToEnd();
        var sampleRate = output.SubstringAfter("-ar ")[..5];
        
        var vgmInst2 = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-f s16le -ar {sampleRate} -ac 2 -i \"{binPath}\" \"{wavPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        });
        vgmInst2?.WaitForExit();

        var memoryStream = new MemoryStream(File.ReadAllBytes(wavPath));

        File.Delete(binkaPath);
        File.Delete(binPath);
        File.Delete(wavPath);

        return memoryStream;
    }

    public MusicPackRuntimeInfo? GetInfo()
    {
        if (SoundSource is null) return null;
        return new MusicPackRuntimeInfo
        {
            Length = SoundSource.GetLength(),
            CurrentPosition = SoundSource.GetPosition()
        };
    }

    public void Pause()
    {
        SoundOut?.Pause();
    }

    public void Resume()
    {
        SoundOut?.Resume();
    }

    public void Restart()
    {
        SoundSource.SetPosition(TimeSpan.Zero);
    }

    public void Scrub(TimeSpan time)
    {
        SoundSource.SetPosition(time);
    }

    public void Dispose()
    {
        SoundOut?.Stop();
        SoundOut?.Dispose();
        SoundSource?.Dispose();
    }

    private static ISoundOut GetSoundOut()
    {
        if (WasapiOut.IsSupportedOnCurrentPlatform)
        {
            return new WasapiOut
            {
                Device = GetDevice()
            };
        }

        return new DirectSoundOut();
    }

    private static MMDevice GetDevice()
    {
        return DeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
    }
}

public class MusicPackRuntimeInfo
{
    public TimeSpan Length;
    public TimeSpan CurrentPosition;
}