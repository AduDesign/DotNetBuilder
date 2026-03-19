using System.ComponentModel;
using System.Runtime.CompilerServices;
using DotNetBuilder.Services;

namespace DotNetBuilder.Models
{
    /// <summary>
    /// Git项目模型
    /// </summary>
    public class GitProject : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private bool _hasChanges;
        private int _changesCount;
        private int _sortOrder;

        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 项目路径
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 是否.NET项目（包含.csproj或.sln文件）
        /// </summary>
        public bool IsDotNetProject { get; set; }

        /// <summary>
        /// 项目类型描述
        /// </summary>
        public string ProjectType { get; set; } = string.Empty;

        /// <summary>
        /// 是否勾选
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isExpanded;
        /// <summary>
        /// 折叠
        /// </summary>
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { _isExpanded = value; }
        }

        /// <summary>
        /// 是否有未提交的更改
        /// </summary>
        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                if (_hasChanges != value)
                {
                    _hasChanges = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 更改文件数量
        /// </summary>
        public int ChangesCount
        {
            get => _changesCount;
            set
            {
                if (_changesCount != value)
                {
                    _changesCount = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 排序顺序（用于拖拽排序）
        /// </summary>
        public int SortOrder
        {
            get => _sortOrder;
            set
            {
                if (_sortOrder != value)
                {
                    _sortOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 解决方案文件路径（如果有）
        /// </summary>
        public string? SolutionPath { get; set; }

        /// <summary>
        /// 项目文件路径（如果有）
        /// </summary>
        public string? ProjectFilePath { get; set; }

        private string _commitMessage = string.Empty;

        /// <summary>
        /// 提交信息（每个项目单独的提交信息）
        /// </summary>
        public string CommitMessage
        {
            get => _commitMessage;
            set
            {
                if (_commitMessage != value)
                {
                    _commitMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private MSBuildVersion? _selectedMSBuildVersion;

        /// <summary>
        /// 该项目选择的MSBuild版本
        /// </summary>
        public MSBuildVersion? SelectedMSBuildVersion
        {
            get => _selectedMSBuildVersion;
            set
            {
                if (_selectedMSBuildVersion != value)
                {
                    _selectedMSBuildVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
