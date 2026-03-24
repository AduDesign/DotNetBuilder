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
        private bool _isSyncing;
        private bool _isBuilding;
        private bool _isRemoved;
        private string _errorMessage = string.Empty;

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
                SetIsSelected(value);
            }
        }
        public void SetIsSelected(bool value)
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
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
        /// 是否正在同步代码
        /// </summary>
        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                if (_isSyncing != value)
                {
                    _isSyncing = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否正在构建
        /// </summary>
        public bool IsBuilding
        {
            get => _isBuilding;
            set
            {
                if (_isBuilding != value)
                {
                    _isBuilding = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否已从列表移除（移除后保存将不再加载）
        /// </summary>
        public bool IsRemoved
        {
            get => _isRemoved;
            set
            {
                if (_isRemoved != value)
                {
                    _isRemoved = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// 清除错误状态
        /// </summary>
        public void ClearError()
        {
            ErrorMessage = string.Empty;
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

        private string _executeFile = string.Empty;

        /// <summary>
        /// 该项目选择的可执行程序路径
        /// </summary>
        public string ExecuteFile
        {
            get => _executeFile;
            set
            {
                if (_executeFile != value)
                {
                    _executeFile = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _configuration = "Release";

        /// <summary>
        /// 构建类型 (Release / Debug)
        /// </summary>
        public string Configuration
        {
            get => _configuration;
            set
            {
                if (_configuration != value)
                {
                    _configuration = value;
                    OnPropertyChanged();
                }
            }
        }

        private PullStrategy _pullStrategy = PullStrategy.Auto;

        /// <summary>
        /// Pull 策略
        /// </summary>
        public PullStrategy PullStrategy
        {
            get => _pullStrategy;
            set
            {
                if (_pullStrategy != value)
                {
                    _pullStrategy = value;
                    OnPropertyChanged();
                }
            }
        }

        private ConflictAction _conflictAction = ConflictAction.Prompt;

        /// <summary>
        /// 冲突时的处理方式
        /// </summary>
        public ConflictAction ConflictAction
        {
            get => _conflictAction;
            set
            {
                if (_conflictAction != value)
                {
                    _conflictAction = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _autoCommitWhenNoMessage = false;

        /// <summary>
        /// 无提交信息时是否自动提交
        /// </summary>
        public bool AutoCommitWhenNoMessage
        {
            get => _autoCommitWhenNoMessage;
            set
            {
                if (_autoCommitWhenNoMessage != value)
                {
                    _autoCommitWhenNoMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private RemoteStatusInfo? _remoteStatus;

        /// <summary>
        /// 远程仓库状态
        /// </summary>
        public RemoteStatusInfo? RemoteStatus
        {
            get => _remoteStatus;
            set
            {
                if (_remoteStatus != value)
                {
                    _remoteStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RemoteStatusDisplay));
                    OnPropertyChanged(nameof(HasRemoteChanges));
                    OnPropertyChanged(nameof(HasLocalUnpushedChanges));
                }
            }
        }

        /// <summary>
        /// 远程状态显示文本
        /// </summary>
        public string RemoteStatusDisplay
        {
            get
            {
                if (_remoteStatus == null)
                    return "";

                var parts = new List<string>();
                if (_remoteStatus.LocalAheadCount > 0)
                    parts.Add($"↑{_remoteStatus.LocalAheadCount}");
                if (_remoteStatus.RemoteAheadCount > 0)
                    parts.Add($"↓{_remoteStatus.RemoteAheadCount}");

                return parts.Count > 0 ? string.Join(" ", parts) : "";
            }
        }

        /// <summary>
        /// 远程是否有更新
        /// </summary>
        public bool HasRemoteChanges => _remoteStatus?.HasRemoteChanges ?? false;

        /// <summary>
        /// 本地是否有未推送的提交
        /// </summary>
        public bool HasLocalUnpushedChanges => _remoteStatus?.HasLocalUnpushedChanges ?? false;

        private bool _isCommitting;

        /// <summary>
        /// 是否正在提交
        /// </summary>
        public bool IsCommitting
        {
            get => _isCommitting;
            set
            {
                if (_isCommitting != value)
                {
                    _isCommitting = value;
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
