using System.Collections.ObjectModel;
using System.Windows.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 欢迎页 ViewModel
    /// </summary>
    public class WelcomeViewModel : ViewModelBase
    {
        private readonly ProjectService _projectService;
        private readonly Action<string> _appendLog;

        public event Action<string>? OnProjectSelected;
        public event Action? OnNewProjectRequested;
        public event Action? OnOpenProjectRequested;

        public WelcomeViewModel(ProjectService projectService, Action<string> appendLog)
        {
            _projectService = projectService;
            _appendLog = appendLog;

            NewProjectCommand = new RelayCommand(_ => OnNewProjectRequested?.Invoke());
            OpenProjectCommand = new RelayCommand(_ => OnOpenProjectRequested?.Invoke());

            _ = LoadRecentProjectsAsync();
        }

        public ObservableCollection<RecentProject> RecentProjects { get; } = new();

        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }

        public async Task LoadRecentProjectsAsync()
        {
            var recent = await _projectService.GetRecentProjectsAsync();
            RecentProjects.Clear();
            foreach (var item in recent)
            {
                RecentProjects.Add(item);
            }
        }

        public void OpenRecentProject(RecentProject recent)
        {
            OnProjectSelected?.Invoke(recent.FilePath);
        }
    }
}
