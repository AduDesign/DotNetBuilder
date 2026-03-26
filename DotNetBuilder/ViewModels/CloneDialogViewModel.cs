using AduSkin.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 克隆仓库对话框 ViewModel
    /// </summary>
    public partial class CloneDialogViewModel : ObservableObject
    {
        private Func<string, string, IProgress<string>?, CancellationToken?, Task<bool>>? _onCloneCallback;
        private Action<string, string>? _onCloneSuccessCallback;
        private CancellationTokenSource? _cloneCts;
        private readonly StringBuilder _progressHistory = new();
        private string _lastErrorFromProgress = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
        private string _repositoryUrl = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
        private string _localDirectory = string.Empty;

        [ObservableProperty]
        private bool _showDialog;

        [ObservableProperty]
        private bool _isCloning;

        [ObservableProperty]
        private bool _isCloneSuccess;

        [ObservableProperty]
        private string _cloneStatusMessage = string.Empty;

        [ObservableProperty]
        private string _lastErrorMessage = string.Empty;

        // 进度历史（用于调试）
        public string ProgressHistory => _progressHistory.ToString();

        public bool CanClone() => !string.IsNullOrWhiteSpace(RepositoryUrl) &&
                                !string.IsNullOrWhiteSpace(LocalDirectory) &&
                                !IsCloning;

        [RelayCommand]
        private void Browse()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择本地存储目录"
            };

            if (dialog.ShowDialog() == true)
            {
                LocalDirectory = dialog.FolderName;
            }
        }

        [RelayCommand(CanExecute = nameof(CanClone))]
        private async Task CloneAsync()
        {
            if (string.IsNullOrWhiteSpace(RepositoryUrl) || string.IsNullOrWhiteSpace(LocalDirectory))
                return;
            if (!Directory.Exists(LocalDirectory))
                Directory.CreateDirectory(LocalDirectory);

            IsCloning = true;
            IsCloneSuccess = false;
            CloneStatusMessage = "正在连接仓库...";
            LastErrorMessage = string.Empty;
            _lastErrorFromProgress = string.Empty;
            _progressHistory.Clear();

            _cloneCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    // 记录所有进度
                    _progressHistory.AppendLine(msg);
                    OnPropertyChanged(nameof(ProgressHistory));

                    // 保存可能的错误信息
                    if (msg.Contains("错误") || msg.Contains("error") || msg.Contains("failed") || msg.StartsWith("fatal"))
                    {
                        _lastErrorFromProgress = msg;
                    }

                    // 显示最新消息
                    CloneStatusMessage = msg;
                });

                var success = await _onCloneCallback?.Invoke(RepositoryUrl, LocalDirectory, progress, _cloneCts.Token)!;

                IsCloneSuccess = success;

                if (success)
                {
                    CloneStatusMessage = "克隆成功！正在添加到项目列表...";
                    // 调用成功回调（添加到项目列表等）
                    _onCloneSuccessCallback?.Invoke(RepositoryUrl, LocalDirectory);
                    CloneStatusMessage = "克隆成功！";
                }
                else
                {
                    // 克隆失败，显示错误信息
                    CloneStatusMessage = "克隆失败";
                    if (!string.IsNullOrEmpty(_lastErrorFromProgress))
                    {
                        LastErrorMessage = _lastErrorFromProgress;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CloneStatusMessage = "已取消克隆";
            }
            catch (Exception ex)
            {
                CloneStatusMessage = "克隆异常";
                LastErrorMessage = ex.Message;
            }
            finally
            {
                IsCloning = false;
                _cloneCts = null;
            }
        }

        [RelayCommand]
        private void CancelClone()
        {
            _cloneCts?.Cancel();
        }

        [RelayCommand]
        private void Close()
        {
            _cloneCts?.Cancel();
            ShowDialog = false;
            ResetState();
        }

        private void ResetState()
        {
            RepositoryUrl = string.Empty;
            LocalDirectory = string.Empty;
            IsCloning = false;
            IsCloneSuccess = false;
            CloneStatusMessage = string.Empty;
            LastErrorMessage = string.Empty;
            _progressHistory.Clear();
            _lastErrorFromProgress = string.Empty;
            _cloneCts?.Dispose();
            _cloneCts = null;
        }

        /// <summary>
        /// 设置克隆回调（克隆操作）
        /// </summary>
        public void SetOnCloneCallback(Func<string, string, IProgress<string>?, CancellationToken?, Task<bool>> callback)
        {
            _onCloneCallback = callback;
        }

        /// <summary>
        /// 设置克隆成功回调（后续处理）
        /// </summary>
        public void SetOnCloneSuccessCallback(Action<string, string> callback)
        {
            _onCloneSuccessCallback = callback;
        }

        public void Show()
        {
            ResetState();
            ShowDialog = true;
        }

        /// <summary>
        /// 显示对话框并设置默认目录
        /// </summary>
        public void ShowWithDefaultDirectory(string defaultDirectory)
        {
            ResetState();
            LocalDirectory = defaultDirectory;
            ShowDialog = true;
        }
    }
}
