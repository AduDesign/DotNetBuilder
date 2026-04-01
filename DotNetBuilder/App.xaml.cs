using System.Configuration;
using System.Data;
using System.Windows;
using DotNetBuilder.Services;

namespace DotNetBuilder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化依赖注入容器
            AppHost.Build();
        }
    }

}
