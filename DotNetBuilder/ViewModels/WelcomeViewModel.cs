using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 欢迎页 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class WelcomeViewModel : ObservableObject
    {
        private readonly ProjectService _projectService;
        private readonly NavigationService _navigationService;

        // 回调函数
        private Action? _onNewProject;

        [ObservableProperty]
        private ObservableCollection<RecentProject> _recentProjects = new();
         
        [ObservableProperty]
        private RecentProject _selectedProject;

        public WelcomeViewModel(ProjectService projectService, NavigationService navigationService)
        {
            _projectService = projectService;
            _navigationService = navigationService;

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
        public void OpenRecentProject(RecentProject recent)
        {
            _navigationService.RequestOpenProject(recent.FilePath);
        }
    }
}
