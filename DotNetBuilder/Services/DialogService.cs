using System.Windows;
using AduSkin.Controls;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 对话框服务接口
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// 显示消息对话框
        /// </summary>
        Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton button, MessageBoxImage image);

        /// <summary>
        /// 显示提交信息对话框
        /// </summary>
        Task<string?> ShowCommitMessageDialogAsync(string projectName, int changesCount);
    }

    /// <summary>
    /// 对话框服务 - 显示消息框、提交信息对话框、项目相关对话框
    /// </summary>
    public class DialogService : IDialogService
    {
        private readonly Action? _showNewProjectDialog;
        private readonly Action<string, List<string>>? _showConflictDialog;

        public DialogService() : this(null, null)
        {
        }

        public DialogService(Action? showNewProjectDialog, Action<string, List<string>>? showConflictDialog)
        {
            _showNewProjectDialog = showNewProjectDialog;
            _showConflictDialog = showConflictDialog;
        }

        public Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton button, MessageBoxImage image)
        {
            var result = AduMessageBox.Show(message, title, button, image);
            return Task.FromResult(result);
        }

        public Task<string?> ShowCommitMessageDialogAsync(string projectName, int changesCount)
        {
            var dialog = new Views.CommitMessageDialog(projectName, changesCount);

            if (dialog.ShowDialog() == true)
            {
                return Task.FromResult<string?>(dialog.CommitMessage);
            }

            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// 显示新建项目对话框
        /// </summary>
        public void ShowNewProjectDialog()
        {
            _showNewProjectDialog?.Invoke();
        }

        /// <summary>
        /// 显示冲突对话框
        /// </summary>
        public void ShowConflictDialog(string projectName, List<string> conflictFiles)
        {
            _showConflictDialog?.Invoke(projectName, conflictFiles);
        }
    }
}
