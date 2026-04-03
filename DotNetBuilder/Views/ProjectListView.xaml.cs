using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotNetBuilder.Models;

namespace DotNetBuilder.Views
{
    public partial class ProjectListView : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragging;
        private GitProject? _draggedProject;

        public ProjectListView()
        {
            InitializeComponent();
        }

        private void ListBoxItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext != null)
            {
                if (DataContext is ViewModels.ProjectListViewModel vm)
                {
                    vm.SelectedProject = element.DataContext as Models.GitProject;
                }
            }
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            if (sender is FrameworkElement element)
            {
                _draggedProject = element.DataContext as GitProject;
            }
        }

        private void ListBoxItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedProject == null)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            // 如果移动超过一定距离才开始拖拽
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_isDragging) return;
                _isDragging = true;

                if (DataContext is ViewModels.ProjectListViewModel vm)
                {
                    var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                    if (listBoxItem != null)
                    {
                        DragDrop.DoDragDrop(listBoxItem, _draggedProject, DragDropEffects.Move);
                    }
                }

                _isDragging = false;
                _draggedProject = null;
            }
        }

        private void ListBoxItem_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(GitProject)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ListBoxItem_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(GitProject)))
                return;

            var droppedProject = e.Data.GetData(typeof(GitProject)) as GitProject;
            if (droppedProject == null) return;

            // 找到目标容器
            var targetContainer = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (targetContainer == null) return;

            var targetProject = targetContainer.DataContext as GitProject;
            if (targetProject == null || targetProject == droppedProject) return;

            // 计算插入位置
            var targetIndex = ProjectListBox.Items.IndexOf(targetProject);
            if (targetIndex < 0) return;

            // 根据鼠标在目标项上的位置调整插入点
            var position = e.GetPosition(targetContainer);
            if (position.Y > targetContainer.ActualHeight / 2)
            {
                // 鼠标在下半部分，插入到目标之后
                targetIndex++;
            }

            if (DataContext is ViewModels.ProjectListViewModel vm)
            {
                vm.MoveTo(droppedProject, targetIndex);
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t)
                    return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
