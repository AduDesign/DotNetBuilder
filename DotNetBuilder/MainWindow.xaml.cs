using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.ViewModels;

namespace DotNetBuilder
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : AduWindow
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].EndsWith(".bdproj", StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.OpenProjectAsync(args[1]);
            }
        }

        private async void RecentProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is RecentProject recent)
            {
                await ViewModel.OpenProjectAsync(recent.FilePath);
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            AduMessageBox.Show(
                ".NET Project Builder v1.0\n\n批量管理 Git 仓库和 .NET 项目\n支持并行构建和冲突处理",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
