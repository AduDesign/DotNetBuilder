using System.Windows;
using System.Windows.Input;
using DotNetBuilder.ViewModels;

namespace DotNetBuilder.Views
{
    public partial class PackWindow : Window
    {
        private readonly PackWindowViewModel _viewModel;

        public PackWindow()
        {
            InitializeComponent();

            _viewModel = new PackWindowViewModel();
            DataContext = _viewModel;

            _viewModel.CloseAction = result =>
            {
                DialogResult = result;
                Close();
            };

            // 密码框特殊处理（密码框不支持绑定）
            PasswordBox.PasswordChanged += PasswordBox_PasswordChanged;

            Loaded += PackWindow_Loaded;
        }

        private void PackWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置焦点
            PasswordBox.Focus();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = PasswordBox.Password;
        }

        /// <summary>
        /// 初始化打包窗口
        /// </summary>
        /// <param name="projectName">项目名称</param>
        /// <param name="sourcePath">源目录路径</param>
        /// <param name="outputDirectory">输出目录（可选）</param>
        public void Initialize(string projectName, string sourcePath, string? outputDirectory = null)
        {
            _viewModel.Initialize(projectName, sourcePath, outputDirectory);
            Title = $"打包项目 - {projectName}";
        }
    }
}
