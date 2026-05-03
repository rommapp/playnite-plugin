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

        public ObservableCollection<RomMRevision> RomVersions { get; set; }
        public bool Cancelled { get; set; } = true;

        public RomMVersionSelector(List<RomMRevision> romVersions)
        {

            RomVersions = new ObservableCollection<RomMRevision>(romVersions);

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