using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DotNetBuilder.Views
{
    public partial class ProjectListView : UserControl
    {
        public ProjectListView()
        {
            InitializeComponent();
        }

        private void ListBoxItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext != null)
            {
                if (DataContext is ViewModels.ProjectListViewModel vm)
                {
                    vm.SelectedProject = element.DataContext as Models.GitProject;
                }
            }
        }
    }
}
