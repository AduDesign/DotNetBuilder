using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 打包窗口的 ViewModel
    /// </summary>
    public class PackWindowViewModel : ObservableObject
    {
        private readonly PackService _packService;

        public PackWindowViewModel()
        {
            _packService = new PackService();
            FileNodes = new ObservableCollection<FileTreeNodeViewModel>();
            PackCommand = new RelayCommand(async () => await ExecutePackAsync(), () => CanPack);
            CancelCommand = new RelayCommand(() => CloseAction?.Invoke(false));
            SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
            UnselectAllCommand = new RelayCommand(() => SetAllSelected(false));
        }
        public Action<bool>? CloseAction; 

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // 属性
        private string _projectName = string.Empty;
        public string ProjectName
        {
            get => _projectName;
            set => SetField(ref _projectName, value);
        }

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                if (SetField(ref _sourcePath, value))
                {
                    LoadDirectoryTree();
                }
            }
        }

        private string _outputPath = string.Empty;
 
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                SetField(ref _outputPath, value);
                OnPropertyChanged(nameof(CanPack));
            }
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetField(ref _password, value);
        }

        private bool _usePassword;
        public bool UsePassword
        {
            get => _usePassword;
            set => SetField(ref _usePassword, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        private bool _isPacking;
        public bool IsPacking
        {
            get => _isPacking;
            set
            {
                if (SetField(ref _isPacking, value))
                {
                    OnPropertyChanged(nameof(CanPack));
                    ((RelayCommand)PackCommand).NotifyCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<FileTreeNodeViewModel> FileNodes { get; }

        public bool CanPack => !IsPacking && !string.IsNullOrEmpty(OutputPath) && HasSelectedFiles;

        private bool _hasSelectedFiles;
        public bool HasSelectedFiles
        {
            get => _hasSelectedFiles;
            private set
            {
                if (SetField(ref _hasSelectedFiles, value))
                {
                    OnPropertyChanged(nameof(CanPack));
                    ((RelayCommand)PackCommand).NotifyCanExecuteChanged();
                }
            }
        }

        // 命令
        public ICommand PackCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand UnselectAllCommand { get; }

        // 方法
        public void Initialize(string projectName, string sourcePath, string? defaultOutputDir = null)
        {
            ProjectName = projectName;
            SourcePath = sourcePath;

            // 设置默认输出路径
            if (string.IsNullOrEmpty(defaultOutputDir))
            {
                defaultOutputDir = Path.GetDirectoryName(sourcePath) ?? sourcePath;
            }
            OutputPath = Path.Combine(defaultOutputDir, $"{projectName}.7z");
        }

        private void LoadDirectoryTree()
        {
            FileNodes.Clear();

            if (!Directory.Exists(SourcePath))
            {
                StatusMessage = "目录不存在";
                return;
            }

            var nodes = _packService.GetDirectoryTree(SourcePath);
            foreach (var node in nodes)
            {
                var vm = CreateNodeViewModel(node);
                vm.PropertyChanged += OnNodeSelectionChanged;
                FileNodes.Add(vm);
            }

            HasSelectedFiles = false;
            StatusMessage = $"共 {CountAllNodes(FileNodes)} 个项目";
        }

        private FileTreeNodeViewModel CreateNodeViewModel(FileTreeNode node)
        {
            var vm = new FileTreeNodeViewModel(node);
            foreach (var child in node.Children)
            {
                var childVm = CreateNodeViewModel(child);
                childVm.PropertyChanged += OnNodeSelectionChanged;
                vm.Children.Add(childVm);
            }
            return vm;
        }

        private void OnNodeSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileTreeNodeViewModel.IsChecked))
            {
                HasSelectedFiles = FileNodes.Any(n => HasAnySelected(n));
            }
        }

        private bool HasAnySelected(FileTreeNodeViewModel node)
        {
            if (node.IsChecked && !node.IsDirectory)
                return true;
            return node.Children.Any(c => HasAnySelected(c));
        }

        private int CountAllNodes(IEnumerable<FileTreeNodeViewModel> nodes)
        {
            int count = 0;
            foreach (var node in nodes)
            {
                count++;
                count += CountAllNodes(node.Children);
            }
            return count;
        }

        private void SetAllSelected(bool selected)
        {
            foreach (var node in FileNodes)
            {
                SetNodeAndChildrenSelected(node, selected);
            }
        }

        private void SetNodeAndChildrenSelected(FileTreeNodeViewModel node, bool selected)
        {
            if (node == null)
                return;
            node.IsChecked = selected;
            foreach (var child in node.Children)
            {
                SetNodeAndChildrenSelected(child, selected);
            }
        }

        private List<string> GetSelectedFiles()
        {
            var files = new List<string>();
            foreach (var node in FileNodes)
            {
                CollectSelectedFiles(node, files);
            }
            return files;
        }

        private void CollectSelectedFiles(FileTreeNodeViewModel node, List<string> files)
        {
            if (node.IsChecked && !node.IsDirectory)
            {
                files.Add(node.FullPath);
            }
            foreach (var child in node.Children)
            {
                CollectSelectedFiles(child, files);
            }
        }

        private async Task ExecutePackAsync()
        {
            if (string.IsNullOrEmpty(OutputPath))
            {
                StatusMessage = "请设置输出路径";
                return;
            }

            IsPacking = true;
            StatusMessage = "正在准备压缩...";

            try
            {
                var progress = new Progress<string>(msg => StatusMessage = msg);
                var files = GetSelectedFiles();

                if (files.Count == 0)
                {
                    StatusMessage = "请选择要压缩的文件";
                    IsPacking = false;
                    return;
                }

                // 确保输出目录存在
                var outputDir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var password = UsePassword ? Password : null;
                var result = await _packService.PackAsync(OutputPath, files, password, SourcePath, progress);

                if (result.Success)
                {
                    StatusMessage = "压缩完成!";

                    // 获取文件大小
                    if (File.Exists(OutputPath))
                    {
                        var fileInfo = new FileInfo(OutputPath);
                        StatusMessage = $"压缩完成! 大小: {FormatFileSize(fileInfo.Length)}";
                    }

                    CloseAction?.Invoke(true);
                }
                else
                {
                    StatusMessage = $"压缩失败: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"压缩异常: {ex.Message}";
            }
            finally
            {
                IsPacking = false;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// 文件树节点 ViewModel
    /// </summary>
    public class FileTreeNodeViewModel : INotifyPropertyChanged
    {
        public FileTreeNodeViewModel(FileTreeNode node)
        {
            Name = node.Name;
            FullPath = node.FullPath;
            IsDirectory = node.IsDirectory;
            IsExpanded = node.IsExpanded;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }

        private bool _IsChecked;
        public bool IsChecked
        {
            get => _IsChecked;
            set
            {
                if (_IsChecked != value)
                {
                    _IsChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<FileTreeNodeViewModel> Children { get; } = new();
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}