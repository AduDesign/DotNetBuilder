using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace DotNetBuilder.Views
{
    /// <summary>
    /// CommitMessageDialog.xaml 的交互逻辑
    /// </summary>
    public partial class CommitMessageDialog : Window, INotifyPropertyChanged
    {
        private string _commitMessage = string.Empty;
        private readonly string _projectName;
        private readonly int _changesCount;

        public CommitMessageDialog(string projectName, int changesCount)
        {
            _projectName = projectName;
            _changesCount = changesCount;

            InitializeComponent();
            DataContext = this;

            CommitMessageBox.Focus();
        }

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

        public string ProjectName => _projectName;
        public int ChangesCount => _changesCount;

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            // 跳过提交，标记为空字符串返回
            CommitMessage = string.Empty;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
