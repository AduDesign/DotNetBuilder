using Microsoft.Extensions.DependencyInjection;
using DotNetBuilder.Services;
using DotNetBuilder.ViewModels;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 应用程序主机 - 负责依赖注入容器的配置和构建
    /// </summary>
    public static class AppHost
    {
        private static IServiceProvider? _serviceProvider;

        /// <summary>
        /// 获取服务提供程序
        /// </summary>
        public static IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("AppHost 尚未初始化，请先调用 Build() 方法");

        /// <summary>
        /// 构建依赖注入容器
        /// </summary>
        public static IServiceProvider Build()
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();
            return _serviceProvider;
        }

        /// <summary>
        /// 配置服务注册
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // 注册所有服务为单例
            services.AddSingleton<GitService>();
            services.AddSingleton<GitSyncService>();
            services.AddSingleton<MSBuildService>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<ProjectService>();
            services.AddSingleton<FileAssociationService>();
            services.AddSingleton<NavigationService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<ProjectLifecycleService>();

            // 注册所有 ViewModel 为单例（因为它们维护状态）
            services.AddSingleton<OutputViewModel>();
            services.AddSingleton<ConflictDialogViewModel>();
            services.AddSingleton<ProjectListViewModel>();
            services.AddSingleton<NewProjectDialogViewModel>();
            services.AddSingleton<CloneDialogViewModel>();
            services.AddSingleton<WelcomeViewModel>();
            services.AddSingleton<ToolbarViewModel>();
            services.AddSingleton<MainViewModel>();
        }

        /// <summary>
        /// 获取指定类型的服务
        /// </summary>
        public static T GetRequiredService<T>() where T : notnull
        {
            return Services.GetRequiredService<T>();
        }
    }
}
