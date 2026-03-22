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
    /// 对话框服务实现
    /// </summary>
    public class DialogService : IDialogService
    {
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
    }
}
