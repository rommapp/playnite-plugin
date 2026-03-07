using Playnite.SDK;
using Playnite.SDK.Controls;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RomM.VersionSelector
{
    public partial class RomMVersionSelector : PluginUserControl
    {

        public ObservableCollection<RomMSibling> Siblings { get; set; }
        public bool Cancelled { get; set; } = false;
        public string SelectedDownloadURL;

        public RomMVersionSelector(List<RomMSibling> siblings)
        {

            Siblings = new ObservableCollection<RomMSibling>(siblings);

            InitializeComponent();  
        }

        private void Click_Cancel(object sender, RoutedEventArgs e)
        {
            Cancelled = true;
            ((Window)Parent).Close();
        }

        private void Click_Install(object sender, RoutedEventArgs e)
        {
            ((Window)Parent).Close();
        }
    }
}