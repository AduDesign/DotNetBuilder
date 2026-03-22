using System.Windows.Input;
using Microsoft.Win32;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 工具栏 ViewModel
    /// </summary>
    public class ToolbarViewModel : ViewModelBase
    {
        private string _projectDisplayName = string.Empty;
        private string _selectedPath = string.Empty;
        private bool _hasProject;
        private bool _isEnabled = true;

        public event Action? OnNewProjectRequested;
        public event Action? OnOpenProjectRequested;
        public event Action? OnSaveProjectRequested;
        public event Action? OnSelectDirectoryRequested;
        public event Action? OnAddProjectRequested;
        public event Action? OnRefreshStatusRequested;

        public ToolbarViewModel()
        {
            NewProjectCommand = new RelayCommand(_ => OnNewProjectRequested?.Invoke());
            OpenProjectCommand = new RelayCommand(_ => OnOpenProjectRequested?.Invoke());
            SaveProjectCommand = new RelayCommand(_ => OnSaveProjectRequested?.Invoke(), _ => HasProject);
            SelectDirectoryCommand = new RelayCommand(_ => OnSelectDirectoryRequested?.Invoke());
            AddProjectCommand = new RelayCommand(_ => OnAddProjectRequested?.Invoke(), _ => HasProject && IsEnabled);
            RefreshStatusCommand = new RelayCommand(_ => OnRefreshStatusRequested?.Invoke(), _ => HasProject && IsEnabled);
        }

        public string ProjectDisplayName
        {
            get => _projectDisplayName;
            set => SetProperty(ref _projectDisplayName, value);
        }

        public string SelectedPath
        {
            get => _selectedPath;
            set => SetProperty(ref _selectedPath, value);
        }

        public bool HasProject
        {
            get => _hasProject;
            set
            {
                if (SetProperty(ref _hasProject, value))
                {
                    OnPropertyChanged(nameof(ShowToolbar));
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool ShowToolbar => HasProject;

        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SelectDirectoryCommand { get; }
        public ICommand AddProjectCommand { get; }
        public ICommand RefreshStatusCommand { get; }
    }
}
