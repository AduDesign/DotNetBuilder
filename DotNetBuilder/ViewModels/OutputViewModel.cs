using System.Collections.ObjectModel;
using System.Windows.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 日志类型枚举
    /// </summary>
    public enum LogType
    {
        Message,
        Error,
        Warning,
        Git,
        Build
    }

    /// <summary>
    /// 输出面板 ViewModel
    /// </summary>
    public class OutputViewModel : ViewModelBase
    {
        private string _logOutput = string.Empty;
        private bool _showErrorLog = true;
        private bool _showWarningLog = true;
        private bool _showMessageLog = true;
        private bool _showGitLog = true;
        private bool _showBuildLog = false;

        private PullStrategy _globalPullStrategy = PullStrategy.Auto;
        private ConflictAction _globalConflictAction = ConflictAction.Prompt;
        private bool _globalAutoCommitWhenNoMessage = false;

        public OutputViewModel()
        {
            ClearLogCommand = new RelayCommand(_ => LogOutput = string.Empty);

            PullStrategies = new ObservableCollection<PullStrategy>
            {
                PullStrategy.Auto,
                PullStrategy.Merge,
                PullStrategy.Rebase,
                PullStrategy.CommitOnly
            };

            ConflictActions = new ObservableCollection<ConflictAction>
            {
                ConflictAction.Prompt,
                ConflictAction.AutoStash,
                ConflictAction.Abort
            };
        }

        public ObservableCollection<PullStrategy> PullStrategies { get; }
        public ObservableCollection<ConflictAction> ConflictActions { get; }

        public string LogOutput
        {
            get => _logOutput;
            set => SetProperty(ref _logOutput, value);
        }

        public bool ShowErrorLog
        {
            get => _showErrorLog;
            set => SetProperty(ref _showErrorLog, value);
        }

        public bool ShowWarningLog
        {
            get => _showWarningLog;
            set => SetProperty(ref _showWarningLog, value);
        }

        public bool ShowMessageLog
        {
            get => _showMessageLog;
            set => SetProperty(ref _showMessageLog, value);
        }

        public bool ShowGitLog
        {
            get => _showGitLog;
            set => SetProperty(ref _showGitLog, value);
        }

        public bool ShowBuildLog
        {
            get => _showBuildLog;
            set => SetProperty(ref _showBuildLog, value);
        }

        public PullStrategy GlobalPullStrategy
        {
            get => _globalPullStrategy;
            set => SetProperty(ref _globalPullStrategy, value);
        }

        public ConflictAction GlobalConflictAction
        {
            get => _globalConflictAction;
            set => SetProperty(ref _globalConflictAction, value);
        }

        public bool GlobalAutoCommitWhenNoMessage
        {
            get => _globalAutoCommitWhenNoMessage;
            set => SetProperty(ref _globalAutoCommitWhenNoMessage, value);
        }

        public ICommand ClearLogCommand { get; }

        public SyncOptions GetSyncOptions() => new()
        {
            PullStrategy = GlobalPullStrategy,
            ConflictAction = GlobalConflictAction,
            AutoCommitWhenNoMessage = GlobalAutoCommitWhenNoMessage
        };

        public void AppendLog(string message)
        {
            // 分隔符标题始终显示
            if (message.Contains("=========="))
            {
                LogOutput += message;
                return;
            }

            var logType = ClassifyLogType(message);
            bool shouldShow = logType switch
            {
                LogType.Error => ShowErrorLog,
                LogType.Warning => ShowWarningLog,
                LogType.Message => ShowMessageLog,
                LogType.Git => ShowGitLog,
                LogType.Build => ShowBuildLog,
                _ => true
            };

            if (shouldShow)
            {
                LogOutput += message;
            }
        }

        private LogType ClassifyLogType(string message)
        {
            if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("错误") ||
                message.Contains("异常") ||
                message.Contains("失败") ||
                message.Contains("FAILED") ||
                message.Contains("FAIL:"))
            {
                return LogType.Error;
            }

            if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("警告") ||
                message.Contains("WARN:"))
            {
                return LogType.Warning;
            }

            if (IsBuildDetailedLog(message))
            {
                return LogType.Build;
            }

            if (message.Contains("[NuGet]", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("NuGet") ||
                message.Contains("git", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Git") ||
                message.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("push", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("暂存", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("提交", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("拉取", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("同步", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在扫描", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("扫描完成", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("==========", StringComparison.OrdinalIgnoreCase))
            {
                return LogType.Git;
            }

            return LogType.Message;
        }

        private bool IsBuildDetailedLog(string message)
        {
            message = message.Trim();
            if (message.Length < 10 || message.Length > 500)
                return false;

            var fileExtensions = new[] { ".cs", ".xaml", ".resx", ".csproj", ".sln", ".json", ".xml", ".config" };
            bool hasFileExtension = fileExtensions.Any(ext =>
                message.Contains(ext, StringComparison.OrdinalIgnoreCase));

            bool hasFilePath = (message.Contains(":\\") || message.Contains("\\bin\\") ||
                                message.Contains("\\obj\\") || message.Contains("\\Properties\\"));

            bool hasBuildKeyword = message.Contains("->") || message.Contains("Copy ") ||
                                   message.Contains("Generate") || message.Contains("Task ") ||
                                   message.Contains("Target ") || message.Contains("CoreCompile") ||
                                   message.Contains("ResolveAssemblyReferences") ||
                                   message.Contains(".dll") || message.Contains(".exe");

            if (hasFileExtension && hasFilePath)
                return true;

            if (hasBuildKeyword && hasFilePath)
                return true;

            if (hasFilePath && message.Contains("\n"))
                return true;

            return false;
        }
    }
}
