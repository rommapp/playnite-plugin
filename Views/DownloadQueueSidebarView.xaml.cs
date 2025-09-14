using RomM.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RomM.Views
{
    public partial class DownloadQueueSidebarView : UserControl
    {
        public DownloadQueueSidebarView()
        {
            InitializeComponent();

            // Create a simple text-based view for now
            var stackPanel = new StackPanel();
            var title = new TextBlock 
            { 
                Text = "Download Queue", 
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5)
            };
            stackPanel.Children.Add(title);
            
            var listBox = new ListBox();
            listBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("DownloadQueue"));
            listBox.ItemTemplate = CreateItemTemplate();
            stackPanel.Children.Add(listBox);
            
            Content = stackPanel;
        }

        public DownloadQueueSidebarView(DownloadQueueSidebarViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private System.Windows.DataTemplate CreateItemTemplate()
        {
            var template = new System.Windows.DataTemplate();
            var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
            
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("GameName"));
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            stackPanelFactory.AppendChild(nameFactory);
            
            var statusFactory = new FrameworkElementFactory(typeof(TextBlock));
            statusFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Status"));
            stackPanelFactory.AppendChild(statusFactory);
            
            var progressFactory = new FrameworkElementFactory(typeof(ProgressBar));
            progressFactory.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("Progress"));
            stackPanelFactory.AppendChild(progressFactory);
            
            template.VisualTree = stackPanelFactory;
            return template;
        }
    }
}
