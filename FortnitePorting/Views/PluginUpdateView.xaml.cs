﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using FortnitePorting.ViewModels;
using FortnitePorting.Views.Extensions;
using Ionic.Zip;
using Microsoft.Win32;
using MessageBox = AdonisUI.Controls.MessageBox;
using MessageBoxButton = AdonisUI.Controls.MessageBoxButton;
using MessageBoxImage = AdonisUI.Controls.MessageBoxImage;

namespace FortnitePorting.Views;

public partial class PluginUpdateView
{
    public PluginUpdateView()
    {
        InitializeComponent();
        AppVM.PluginUpdateVM = new PluginUpdateViewModel();
        DataContext = AppVM.PluginUpdateVM;

        BlenderInstallationsItemsControl.Items.SortDescriptions.Add(new SortDescription { PropertyName = "Content", Direction = ListSortDirection.Ascending });
        AppVM.PluginUpdateVM.Initialize();
    }

    private void OnClickFinished(object sender, RoutedEventArgs e)
    {
        var selectedVersions = AppVM.PluginUpdateVM.BlenderInstallations.Where(x => x.IsChecked.HasValue && x.IsChecked.Value).Select(x => x.Tag as DirectoryInfo).ToArray();

        if (selectedVersions.Length == 0)
        {
            Close();
            return;
        }

        App.BlenderPluginStream.Position = 0;
        var addonZip = ZipFile.Read(App.BlenderPluginStream);
        foreach (var selectedVersion in selectedVersions)
        {
            var addonPath = Path.Combine(selectedVersion.FullName, "scripts", "addons");
            addonZip.ExtractAll(addonPath, ExtractExistingFileAction.OverwriteSilently);
        }

        Close();
        MessageBox.Show($"Successfully updated plugin for Blender {selectedVersions.Select(x => x.Name).CommaJoin()}. Please remember to enable the plugin (if this is your first time installing) and restart Blender.", "Updated Plugin Successfully", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

public static class SteamDetection
{
    public static List<AppInfo> GetSteamApps(string[] steamLibs)
    {
        var apps = new List<AppInfo>();
        foreach (var lib in steamLibs)
        {
            var appMetaDataPath = Path.Combine(lib, "SteamApps");
            var files = Directory.GetFiles(appMetaDataPath, "*.acf");
            apps.AddRange(files.Select(GetAppInfo).Where(appInfo => appInfo is not null));
        }

        return apps;
    }

    public static AppInfo? GetAppInfo(string appMetaFile)
    {
        var fileDataLines = File.ReadAllLines(appMetaFile);
        var dic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in fileDataLines)
        {
            var match = Regex.Match(line, @"\s*""(?<key>\w+)""\s+""(?<val>.*)""");
            if (!match.Success) continue;
            var key = match.Groups["key"].Value;
            var val = match.Groups["val"].Value;
            dic[key] = val;
        }

        AppInfo? appInfo;

        if (dic.Keys.Count <= 0) return null;

        appInfo = new AppInfo();
        var appId = dic["appid"];
        var name = dic["name"];
        var installDir = dic["installDir"];

        var path = Path.GetDirectoryName(appMetaFile);
        var libGameRoot = Path.Combine(path, "common", installDir);

        if (!Directory.Exists(libGameRoot)) return null;

        appInfo.Id = appId;
        appInfo.Name = name;
        appInfo.GameRoot = libGameRoot;

        return appInfo;
    }

    public static string[] GetSteamLibs()
    {
        var steamPath = GetSteamPath();
        if (steamPath is null) return Array.Empty<string>();
        var libraries = new List<string> { steamPath };

        var listFile = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
        if (!File.Exists(listFile)) return Array.Empty<string>();
        var lines = File.ReadAllLines(listFile);
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"""(?<path>\w:\\\\.*)""");
            if (!match.Success) continue;
            var path = match.Groups["path"].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
            {
                libraries.Add(path);
            }
        }

        return libraries.ToArray();
    }

    private static string? GetSteamPath()
    {
        var bit64 = (string?) Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "");
        var bit32 = (string?) Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", "");

        return bit64 ?? bit32 ?? null;
    }

    public class AppInfo
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string GameRoot { get; internal set; }
    }
}