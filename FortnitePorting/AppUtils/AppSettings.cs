﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Versions;
using FortnitePorting.Exports.Blender;
using FortnitePorting.Exports.Unreal;
using FortnitePorting.Services.Endpoints.Models;
using Newtonsoft.Json;

namespace FortnitePorting.AppUtils;

public partial class AppSettings : ObservableObject
{
    public static AppSettings Current;

    public static readonly DirectoryInfo DirectoryPath = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortnitePorting"));
    public static readonly DirectoryInfo FilePath = new(Path.Combine(DirectoryPath.FullName, "AppSettings.json"));

    public static void Load()
    {
        if (File.Exists(FilePath.FullName))
        {
            Current = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath.FullName));
        }

        Current ??= new AppSettings();
    }

    public static void Save()
    {
        File.WriteAllText(FilePath.FullName, JsonConvert.SerializeObject(Current, Formatting.Indented));
    }

    [JsonIgnore] public string ArchivePath => InstallType is EInstallType.Custom ? CustomArchivePath : LocalArchivePath;

    [ObservableProperty] private string localArchivePath = string.Empty;
    [ObservableProperty] private string customArchivePath = string.Empty;

    [ObservableProperty] private ELanguage language;

    [ObservableProperty] private EInstallType installType;

    [ObservableProperty] private bool discordRichPresence = true;

    [ObservableProperty] private AesResponse? aesResponse;

    [ObservableProperty] private BlenderExportSettings blenderExportSettings = new();

    [ObservableProperty] private UnrealExportSettings unrealExportSettings = new();

    [ObservableProperty] private List<string> favoriteIDs = new();

    [ObservableProperty] private EpicAuthResponse? epicAuth;

    [ObservableProperty] private string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

    [ObservableProperty] private bool justUpdated = true;

    [ObservableProperty] private DateTime lastUpdateAskTime = DateTime.Now.Subtract(TimeSpan.FromDays(1));

    [ObservableProperty] private Version lastKnownUpdateVersion;

    [ObservableProperty] private bool showConsole = true;

    [ObservableProperty] private EImageType imageType = EImageType.PNG;

    [ObservableProperty] private float assetSize = 1.0f;

    [ObservableProperty] private Dictionary<string, List<string>> itemMappings = new();

    [ObservableProperty] private DateTime lastBroadcastTime;

    [ObservableProperty] private EGame gameVersion = EGame.GAME_UE5_4;

    [ObservableProperty] private string customMappingsPath;

    [ObservableProperty] private List<CustomAESKey> customAesKeys = new();

    [ObservableProperty] private bool filterProps = true;

    [ObservableProperty] private bool filterItems = true;

    [ObservableProperty] private List<string> unrealProjects = new();
}

public partial class CustomAESKey : ObservableObject
{
    public static CustomAESKey ZERO => new(Globals.ZERO_CHAR);

    [ObservableProperty] private string hex;
    
    public CustomAESKey(string hex)
    {
        Hex = hex;
    }
}