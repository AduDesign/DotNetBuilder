using System.Windows.Controls;
using System.Windows.Input;
using DotNetBuilder.Models;
using DotNetBuilder.ViewModels;

namespace DotNetBuilder.Views
{
    public partial class WelcomeView : UserControl
    {
        public WelcomeView()
        {
            InitializeComponent();
        }

        private void RecentProject_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is WelcomeViewModel viewModel &&
                sender is ListBox listBox &&
                listBox.SelectedItem is RecentProject recent)
            {
                viewModel.OpenRecentProject(recent);
            }
        }
    }
}
