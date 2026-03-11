using Playnite.SDK;
using Playnite.SDK.Controls;
using RomM.Models.RomM.Rom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace RomM.VersionSelector
{
    public partial class RomMVersionSelector : PluginUserControl
    {

        public ObservableCollection<RomMSavedSibing> RomVersions { get; set; }
        public bool Cancelled { get; set; } = true;

        public RomMVersionSelector(List<RomMSavedSibing> romVersions)
        {

            RomVersions = new ObservableCollection<RomMSavedSibing>(romVersions);

            InitializeComponent();  
        }

        private void Click_Cancel(object sender, RoutedEventArgs e)
        {
            ((Window)Parent).Close();
        }

        private void Click_Install(object sender, RoutedEventArgs e)
        {
            Cancelled = false;
            ((Window)Parent).Close();
        }
    }
}