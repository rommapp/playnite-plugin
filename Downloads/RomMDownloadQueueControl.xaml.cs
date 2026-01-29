using System;
using System.Windows;
using System.Windows.Controls;

namespace RomM.Downloads
{
    public partial class RomMDownloadQueueControl : UserControl
    {
        private readonly DownloadQueueController controller;

        public RomMDownloadQueueControl(DownloadQueueController controller)
        {
            InitializeComponent();
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            DataContext = controller.ViewModel;
        }

        private void CancelClicked(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var item = btn.Tag as DownloadQueueItem;
            if (item != null)
            {
                controller.Cancel(item.GameId);
            }
        }
    }
}