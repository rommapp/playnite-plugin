using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using RomM.Models.RomM;
using RomM.Models.RomM.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace RomM.Settings
{
    public partial class SettingsView : UserControl
    {
        private bool InManualCellCommit = false;

        public SettingsView()
        {
            InitializeComponent();
        }

        private void Click_TestConnection(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.TestConnection();
            e.Handled = true;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri.Scheme == Uri.UriSchemeHttp || e.Uri.Scheme == Uri.UriSchemeHttps)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
            e.Handled = true;
        }

        private async void Click_PullPlatforms(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.Notify = false;

            try
            {
                HttpResponseMessage response = await HttpClientSingleton.Instance.GetAsync($"{SettingsViewModel.Instance.RomMHost}/api/platforms");
                response.EnsureSuccessStatusCode();

                string body = await response.Content.ReadAsStringAsync();
                SettingsViewModel.Instance.RomMPlatforms = JsonConvert.DeserializeObject<List<RomMPlatform>>(body);
                SettingsViewModel.Instance.UpdateNotifcationBar("Platforms successfully retrieved!");
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error($"RomM - failed to get platforms: {ex}");
                SettingsViewModel.Instance.UpdateNotifcationBar($"Failed to get platforms: {ex.Message}!", true);
            }
        }

        private void Click_AddMapping(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.Mappings.Add(new EmulatorMapping(SettingsViewModel.Instance.RomMPlatforms));
        }

        private void Click_Delete(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is EmulatorMapping mapping)
            {
                var res = SettingsViewModel.Instance.PlayniteAPI.Dialogs.ShowMessage(string.Format("Delete this mapping?\r\n\r\n{0}", mapping.GetDescriptionLines().Aggregate((a, b) => $"{a}{Environment.NewLine}{b}")), "Confirm delete", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    SettingsViewModel.Instance.Mappings.Remove(mapping);
                }
            }
        }

        private void Click_BrowseDestination(object sender, RoutedEventArgs e)
        {
            var mapping = ((FrameworkElement)sender).DataContext as EmulatorMapping;
            string path;
            if ((path = GetSelectedFolderPath()) == null) return;
            var playnite = SettingsViewModel.Instance.PlayniteAPI;
            if (playnite.Paths.IsPortable)
            {
                path = path.Replace(playnite.Paths.ApplicationPath, Playnite.SDK.ExpandableVariables.PlayniteDirectory);
            }

            mapping.DestinationPath = path;
        }

        private static string GetSelectedFolderPath()
        {
            return SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFolder();
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (!InManualCellCommit && sender is DataGrid grid)
            {
                InManualCellCommit = true;

                // HACK!!!!
                // Alternate approach 1: try to find new value here and store that somewhere as the currently selected emu
                // Alternate approach 2: the "right" way(?) https://stackoverflow.com/a/34332709
                if (e.Column.Header?.ToString() == "Emulator" || e.Column.Header?.ToString() == "Profile")
                {
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                }

                InManualCellCommit = false;
            }
        }

        private void DataGrid_CurrentCellChanged(object sender, EventArgs e)
        {

        }

        private void Click_Browse7zDestination(object sender, RoutedEventArgs e)
        {
            string path;
            if ((path = SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFile("7Zip Executable|7z.exe")) == null) return;

            SettingsViewModel.Instance.PathTo7z = path;
            e.Handled = true;
        }
    }
}
