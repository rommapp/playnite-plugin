using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
namespace RomM.DownloadQueue
{
    public partial class DownloadQueueView : UserControl
    {
        private readonly RomM Plugin;

        public DownloadQueueView(RomM plugin)
        {
            Plugin = plugin;
            InitializeComponent();
        }

        private void CancelDownload(object sender, EventArgs e)
        {
            Plugin.DownloadQueue.CancelInstall();
        }

        private void BackToLibrary(object sender, RoutedEventArgs e)
        {
            Plugin.PlayniteApi.MainView.SwitchToLibraryView();
        }

        private void RemoveItem(object sender, RoutedEventArgs e)
        {
            var hyperlink = (Hyperlink)sender;

            Plugin.DownloadQueue.Remove(hyperlink.DataContext as DownloadQueueItem);
        }

        private void Play(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var item = button.DataContext as DownloadQueueItem;

            Plugin.PlayniteApi.StartGame(item.Game.Id);
        }

        private void ViewInLibrary(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var item = button.DataContext as DownloadQueueItem;

            Plugin.PlayniteApi.MainView.SwitchToLibraryView();
            Plugin.PlayniteApi.MainView.SelectGame(item.Game.Id);
        }
    }
}