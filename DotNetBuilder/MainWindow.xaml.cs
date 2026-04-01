using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.ViewModels;
using DotNetBuilder.Services;

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
            DataContext = AppHost.GetRequiredService<MainViewModel>();
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
    }
}
