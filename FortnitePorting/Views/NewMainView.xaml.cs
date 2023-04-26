﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using FortnitePorting.AppUtils;
using FortnitePorting.Services;
using FortnitePorting.ViewModels;
using FortnitePorting.Views.Controls;
using FortnitePorting.Views.Extensions;
using StyleSelector = FortnitePorting.Views.Controls.StyleSelector;

namespace FortnitePorting.Views;

public partial class NewMainView
{
    public NewMainView()
    {
        InitializeComponent();
        AppVM.NewMainVM = new NewMainViewModel();
        DataContext = AppVM.NewMainVM;
        
        Title = $"Fortnite Porting - v{Globals.VERSION}";
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppSettings.Current.ArchivePath) && AppSettings.Current.InstallType == EInstallType.Local)
        {
            AppHelper.OpenWindow<StartupView>();
            return;
        }

        var (updateAvailable, updateVersion) = UpdateService.GetStats();
        if (DateTime.Now >= AppSettings.Current.LastUpdateAskTime.AddDays(1) || updateVersion > AppSettings.Current.LastKnownUpdateVersion)
        {
            AppSettings.Current.LastKnownUpdateVersion = updateVersion;
            UpdateService.Start(automaticCheck: true);
            AppSettings.Current.LastUpdateAskTime = DateTime.Now;
        }

        if (AppSettings.Current.JustUpdated && !updateAvailable)
        {
            AppHelper.OpenWindow<PluginUpdateView>();
            AppSettings.Current.JustUpdated = false;
        }

        await AppVM.NewMainVM.Initialize();
    }

    private async void OnAssetTypeClick(object sender, RoutedEventArgs e)
    {
        if (AppVM.AssetHandlerVM is null) return;
        
        var clickedButton = (ToggleButton) sender;
        var assetType = (EAssetType) clickedButton.Tag;
        if (AppVM.NewMainVM.CurrentAssetType == assetType) return;
        
        AppVM.NewMainVM.CurrentAssetType = assetType;
        DiscordService.Update(assetType);
        
        // uncheck other buttons
        var buttons = AssetTypePanel.Children.OfType<ToggleButton>();
        foreach (var button in buttons)
        {
            if (button == clickedButton) continue;
            button.IsChecked = false;
        }

        var handlers = AppVM.AssetHandlerVM.Handlers;
        AssetDisplayGrid.ItemsSource = handlers[assetType].TargetCollection;
        foreach (var (handlerType, handlerData) in handlers)
        {
            if (handlerType == assetType && !AppVM.NewMainVM.IsPaused)
            {
                handlerData.PauseState.Unpause();
            }
            else
            {
                handlerData.PauseState.Pause();
            }
        }
        
        if (!handlers[assetType].HasStarted)
        {
            await handlers[assetType].Execute();
        }
    }

    private void OnAssetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is null) return;
        
        var selected = (AssetSelectorItem) listBox.SelectedItem;
        if (selected.IsRandom)
        {
            listBox.SelectedIndex = App.RandomGenerator.Next(0, listBox.Items.Count);
            return;
        }

        AppVM.NewMainVM.Styles.Clear();
        AppVM.NewMainVM.CurrentAsset = selected;
        
        var styles = selected.Asset.GetOrDefault("ItemVariants", Array.Empty<UObject>());
        foreach (var style in styles)
        {
            var channel = style.GetOrDefault("VariantChannelName", new FText("Unknown")).Text.ToLower().TitleCase();
            var optionsName = style.ExportType switch
            {
                "FortCosmeticCharacterPartVariant" => "PartOptions",
                "FortCosmeticMaterialVariant" => "MaterialOptions",
                "FortCosmeticParticleVariant" => "ParticleOptions",
                "FortCosmeticMeshVariant" => "MeshOptions",
                _ => null
            };

            if (optionsName is null) continue;

            var options = style.Get<FStructFallback[]>(optionsName);
            if (options.Length == 0) continue;

            var styleSelector = new StyleSelector(channel, options, selected.IconBitmap);
            if (styleSelector.Options.Items.Count == 0) continue;
            AppVM.NewMainVM.Styles.Add(styleSelector);
        }
    }

    private void OnToggleConsoleChecked(object sender, RoutedEventArgs e)
    {
        var menuItem = (MenuItem) sender;
        var show = menuItem.IsChecked;
        AppSettings.Current.ShowConsole = show;
        App.ToggleConsole(show);
    }

    private void FunkyScrollFix(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        switch (e.Delta)
        {
            case < 0:
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 88);
                break;
            case > 0:
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - 88);
                break;
        }
    }

    private void RefreshFilters()
    {
        AssetDisplayGrid.Items.Filter = o =>
        {
            var asset = (AssetSelectorItem) o;
            return asset.Match(AppVM.NewMainVM.SearchFilter) && AppVM.NewMainVM.Filters.All(x => x.Invoke(asset));
        };
        AssetDisplayGrid.Items.Refresh();
    }

    private void OnFilterItemChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = (CheckBox)sender;
        if (checkBox.Tag is null) return;
        if (!checkBox.IsChecked.HasValue) return;

        AppVM.NewMainVM.ModifyFilters(checkBox.Tag.ToString()!, checkBox.IsChecked.Value);
        RefreshFilters();
    }

    private void OnClearFiltersClicked(object sender, RoutedEventArgs e)
    {
        AppVM.NewMainVM.Filters.Clear();
        foreach (var child in FilterPanel.Children)
        {
            if (child is not CheckBox checkBox) continue;
            checkBox.IsChecked = false;
        }

        RefreshFilters();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFilters();
    }

    private void RefreshSorting()
    {
        AssetDisplayGrid.Items.SortDescriptions.Clear();
        AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("IsRandom", ListSortDirection.Descending));

        switch (AppVM.NewMainVM.SortType)
        {
            case ESortType.Default:
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("ID", GetProperSort(ListSortDirection.Ascending)));
                break;
            case ESortType.AZ:
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("DisplayName", GetProperSort(ListSortDirection.Ascending)));
                break;
            case ESortType.Season:
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("SeasonNumber", GetProperSort(ListSortDirection.Ascending)));
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("Rarity", GetProperSort(ListSortDirection.Ascending)));
                break;
            case ESortType.Rarity:
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("Rarity", GetProperSort(ListSortDirection.Ascending)));
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("ID", GetProperSort(ListSortDirection.Ascending)));
                break;
            case ESortType.Series:
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("Series", GetProperSort(ListSortDirection.Descending)));
                AssetDisplayGrid.Items.SortDescriptions.Add(new SortDescription("Rarity", GetProperSort(ListSortDirection.Descending)));
                break;
        }
    }
    
    private ListSortDirection GetProperSort(ListSortDirection direction)
    {
        return direction switch
        {
            ListSortDirection.Ascending => AppVM.NewMainVM.Ascending ? ListSortDirection.Descending : direction,
            ListSortDirection.Descending => AppVM.NewMainVM.Ascending ? ListSortDirection.Ascending : direction
        };
    }

    private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSorting();
    }
    
    private void OnAscendingDescendingClicked(object sender, RoutedEventArgs e)
    {
        var newValue = !AppVM.NewMainVM.Ascending;
        AppVM.NewMainVM.Ascending = newValue;
        
        var button = (Button) sender;
        var image = (IconBecauseImLazy) button.Content;
        image.RenderTransform = new RotateTransform(newValue ? 180 : 0);
        
        RefreshSorting();
    }

    private void OnPauseLoadingSwitched(object sender, RoutedEventArgs e)
    {
        if (AppVM.AssetHandlerVM is null) return;
        
        var toggleSwitch = (ToggleButton) sender;
        var pauseValue = toggleSwitch.IsChecked.HasValue && toggleSwitch.IsChecked.Value;
        foreach (var (_, handler) in AppVM.AssetHandlerVM.Handlers)
        {
            handler.PauseState.IsPaused = pauseValue; 
        }

        AppVM.NewMainVM.IsPaused = pauseValue;
    }
}