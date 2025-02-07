﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AdonisUI.Controls;
using FortnitePorting.AppUtils;
using FortnitePorting.Services;
using FortnitePorting.ViewModels;
using OpenTK.Graphics.OpenGL;
using MessageBox = AdonisUI.Controls.MessageBox;
using MessageBoxImage = AdonisUI.Controls.MessageBoxImage;

namespace FortnitePorting.Views;

public partial class LoadingView
{
    public LoadingView()
    {
        InitializeComponent();
        AppVM.LoadingVM = new LoadingViewModel();
        DataContext = AppVM.LoadingVM;

        AppVM.LoadingVM.TitleText = Globals.TITLE;
        Title = Globals.TITLE;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppSettings.Current.ArchivePath) && AppSettings.Current.InstallType == EInstallType.Local)
        {
            AppHelper.OpenWindow<StartupView>();
            return;
        }

        AppVM.LoadingVM.Update("Checking For Updates");
        var broadcasts = await EndpointService.FortnitePorting.GetBroadcastsAsync();
        var validBroadcasts = broadcasts.Where(broadcast => broadcast.Version.Equals(Globals.VERSION) || broadcast.Version.Equals("All"));
        foreach (var broadcast in validBroadcasts)
        {
            if (broadcast.PushedTime <= AppSettings.Current.LastBroadcastTime || !broadcast.IsActive) continue;
            AppSettings.Current.LastBroadcastTime = broadcast.PushedTime;

            var messageBox = new MessageBoxModel
            {
                Caption = broadcast.Title,
                Text = broadcast.Contents,
                Icon = MessageBoxImage.Exclamation,
                Buttons = new[] { MessageBoxButtons.Ok() }
            };

            MessageBox.Show(messageBox);
        }

        var (updateAvailable, updateVersion) = UpdateService.GetStats();
        if (DateTime.Now >= AppSettings.Current.LastUpdateAskTime.AddDays(1) || updateVersion > AppSettings.Current.LastKnownUpdateVersion)
        {
            AppSettings.Current.LastKnownUpdateVersion = updateVersion;
            UpdateService.Start(automaticCheck: true);
            AppSettings.Current.LastUpdateAskTime = DateTime.Now;
        }

        await AppVM.LoadingVM.Initialize();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        
        DragMove();
    }

    private void OpenSettings(object sender, MouseButtonEventArgs e)
    {
        AppHelper.OpenWindow<SettingsView>(this);
    }
}