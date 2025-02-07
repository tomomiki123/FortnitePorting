﻿using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CUE4Parse.UE4.Versions;
using FortnitePorting.AppUtils;
using FortnitePorting.Views.Extensions;

namespace FortnitePorting.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public bool IsRestartRequired = false;
    public bool ChangedUpdateChannel = false;
    
    public bool IsLiveInstall => InstallType == EInstallType.Live;
    public bool IsCustomInstall => InstallType == EInstallType.Custom;
    public bool CanChangePath => InstallType != EInstallType.Live;
    
    [ObservableProperty] private ObservableCollection<CustomAESKey> aesKeys = new();

    public EInstallType InstallType
    {
        get => AppSettings.Current.InstallType;
        set
        {
            AppSettings.Current.InstallType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLiveInstall));
            OnPropertyChanged(nameof(IsCustomInstall));
            OnPropertyChanged(nameof(CanChangePath));
            OnPropertyChanged(nameof(ArchivePath));
            IsRestartRequired = true;
        }
    }

    public string ArchivePath
    {
        get => AppSettings.Current.ArchivePath;
        set
        {
            if (IsCustomInstall)
                AppSettings.Current.CustomArchivePath = value;
            else
                AppSettings.Current.LocalArchivePath = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }

    public EGame GameVersion
    {
        get => AppSettings.Current.GameVersion;
        set
        {
            AppSettings.Current.GameVersion = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }

    public string MappingsPath
    {
        get => AppSettings.Current.CustomMappingsPath;
        set
        {
            AppSettings.Current.CustomMappingsPath = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }

    public ELanguage Language
    {
        get => AppSettings.Current.Language;
        set
        {
            AppSettings.Current.Language = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }

    public string AssetsPath
    {
        get => AppSettings.Current.AssetsPath;
        set
        {
            AppSettings.Current.AssetsPath = value;
            OnPropertyChanged();
        }
    }

    public bool DiscordRPC
    {
        get => AppSettings.Current.DiscordRichPresence;
        set
        {
            AppSettings.Current.DiscordRichPresence = value;
            OnPropertyChanged();
        }
    }

    public float AssetSize
    {
        get => AppSettings.Current.AssetSize;
        set
        {
            AppSettings.Current.AssetSize = value;
            OnPropertyChanged();
        }
    }

    public bool FilterProps
    {
        get => AppSettings.Current.FilterProps;
        set
        {
            AppSettings.Current.FilterProps = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }

    public bool FilterItems
    {
        get => AppSettings.Current.FilterItems;
        set
        {
            AppSettings.Current.FilterItems = value;
            OnPropertyChanged();
            IsRestartRequired = true;
        }
    }
}