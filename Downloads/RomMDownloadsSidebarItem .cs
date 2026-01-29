using Playnite.SDK.Plugins;
using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomM.Downloads
{
    public class RomMDownloadsSidebarItem : SidebarItem
    {
        private readonly RomM plugin;
        private SidebarItemControl sidebarRoot;

        public RomMDownloadsSidebarItem(RomM plugin)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));

            Type = SiderbarItemType.View;
            Title = "RomM Downloads";

            Icon = new Image
            {
                Source = new BitmapImage(new Uri(RomM.Icon)),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform
            };

            Visible = true;

            Opened = () =>
            {
                // Cache the root control
                if (sidebarRoot == null)
                {
                    sidebarRoot = new SidebarItemControl();
                    sidebarRoot.SetTitle("RomM Downloads");

                    // Add the view
                    sidebarRoot.AddContent(new RomMDownloadQueueControl(plugin.DownloadQueueController));
                }

                return sidebarRoot;
            };
        }

        public void ResetView()
        {
            sidebarRoot = null;
        }
    }
}
