using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 工具栏 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class ToolbarViewModel : ObservableObject
    {
        private readonly NavigationService _navigationService;

        // 回调函数
        private Action? _onNewProject;
        private Action? _onSaveProject;
        private Func<Task>? _onAddProject;
        private Func<Task>? _onRefreshStatus;

        [ObservableProperty]
        private string _projectDisplayName = string.Empty;

        [ObservableProperty]
        private string _selectedPath = string.Empty;

        [ObservableProperty]
        private bool _hasProject;

        [ObservableProperty]
        private bool _isEnabled = true;

        public bool ShowToolbar => HasProject;

        public ToolbarViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;
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

        [RelayCommand(CanExecute = nameof(CanSaveProject))]
        private void SaveProject() => _onSaveProject?.Invoke();
        private bool CanSaveProject() => HasProject;


        [RelayCommand(CanExecute = nameof(CanAddProject))]
        private async Task AddProject()
        {
            if (_onAddProject != null)
                await _onAddProject();
        }
        private bool CanAddProject() => HasProject && IsEnabled;

        [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
        private async Task RefreshStatus()
        {
            if (_onRefreshStatus != null)
                await _onRefreshStatus();
        }
        private bool CanRefreshStatus() => HasProject && IsEnabled;

        /// <summary>
        /// 设置新建项目回调
        /// </summary>
        public void SetOnNewProject(Action callback)
        {
            _onNewProject = callback;
        }

        /// <summary>
        /// 设置保存项目回调
        /// </summary>
        public void SetOnSaveProject(Action callback)
        {
            _onSaveProject = callback;
        }

        /// <summary>
        /// 设置添加项目回调
        /// </summary>
        public void SetOnAddProject(Func<Task> callback)
        {
            _onAddProject = callback;
        }

        /// <summary>
        /// 设置刷新状态回调
        /// </summary>
        public void SetOnRefreshStatus(Func<Task> callback)
        {
            _onRefreshStatus = callback;
        }

        partial void OnHasProjectChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowToolbar));
            SaveProjectCommand.NotifyCanExecuteChanged();
            AddProjectCommand.NotifyCanExecuteChanged();
            RefreshStatusCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsEnabledChanged(bool value)
        {
            AddProjectCommand.NotifyCanExecuteChanged();
            RefreshStatusCommand.NotifyCanExecuteChanged();
        }
    }
}
