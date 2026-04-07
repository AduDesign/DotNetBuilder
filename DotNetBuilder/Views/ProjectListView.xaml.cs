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
    }
}
