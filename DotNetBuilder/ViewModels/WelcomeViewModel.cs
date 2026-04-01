using AduSkin.AdditionalAttributes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 欢迎页 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class WelcomeViewModel : ObservableObject
    {
        private readonly ProjectService _projectService;
        private readonly NavigationService _navigationService;
        private readonly ConfigService _configService;

        // 回调函数
        private Action? _onNewProject;

        [ObservableProperty]
        private ObservableCollection<RecentProject> _recentProjects = new();

        [ObservableProperty]
        private RecentProject _selectedProject;

        public WelcomeViewModel(ProjectService projectService, NavigationService navigationService, ConfigService configService)
        {
            _projectService = projectService;
            _navigationService = navigationService;
            _configService = configService;

            _ = LoadRecentProjectsAsync();
        }

        [RelayCommand]
        private void NewProject() => _onNewProject?.Invoke();

        [RelayCommand]
        private void OpenProject()
        {
            var filePath = _navigationService.OpenFilePicker();
            if (!string.IsNullOrEmpty(filePath))
            {
                _navigationService.RequestOpenProject(filePath);
            }
        }

        /// <summary>
        /// 设置新建项目回调
        /// </summary>
        public void SetOnNewProject(Action callback)
        {
            _onNewProject = callback;
        }

        public async Task LoadRecentProjectsAsync()
        {
            var recent = await _projectService.GetRecentProjectsAsync();
            RecentProjects.Clear();
            foreach (var item in recent)
            {
                RecentProjects.Add(item);
            }
        }
        [RelayCommand]
        public async Task OpenRecentProject(RecentProject recent)
        {
            // 更新最后打开时间
            recent.LastOpenedAt = DateTime.Now;
            await _projectService.AddRecentProjectAsync(recent.Name, recent.FilePath);

            _navigationService.RequestOpenProject(recent.FilePath);
        }

        [RelayCommand]
        private async Task RemoveProjectAsync(RecentProject? recent)
        {
            if (recent == null) return;

            RecentProjects.Remove(recent);

            // 更新 App 配置
            var config = await _projectService.LoadAppConfigAsync();
            config.RecentProjects.RemoveAll(p => p.FilePath == recent.FilePath);
            await _projectService.SaveAppConfigAsync(config);
        }

        #region 引导
        [RelayCommand]
        public void Loaded(FrameworkElement sender)
        {
            if (sender == null) return;

            // 检查是否已经显示过引导
            var config = _configService.LoadConfig();
            if (config?.HasShownGuide == true)
                return;

            var window = Window.GetWindow(sender);
            if (window == null) return;

            // 启动引导，显示上一步、下一步、跳过按钮
            window.StartGuide("New", g =>
            {
                g.ShowSkipButton = true;
                g.ShowPreviousButton = true;
            });

            // 标记引导已显示
            _configService.SetHasShownGuide();
        }
        #endregion
    }
}
