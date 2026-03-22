using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace DotNetBuilder.ViewModels
{

	/// <summary>
	/// 新建项目对话框 ViewModel
	/// </summary>
	public class NewProjectDialogViewModel : ViewModelBase
	{
		private string _newProjectName = "新项目";
		private string _newProjectRootPath = string.Empty;
		private bool _showDialog;

		public NewProjectDialogViewModel()
		{
			BrowseCommand = new RelayCommand(_ => Browse());
			ConfirmCommand = new RelayCommand(_ => Confirm());
			CancelCommand = new RelayCommand(_ => Cancel());
		}

		public event Action? OnConfirm;
		public event Action? OnCancel;

		public string NewProjectName
		{
			get => _newProjectName;
			set => SetProperty(ref _newProjectName, value);
		}

		public string NewProjectRootPath
		{
			get => _newProjectRootPath;
			set
			{
				if (SetProperty(ref _newProjectRootPath, value))
				{
					OnPropertyChanged(nameof(CanConfirm));
				}
			}
		}

		public bool ShowDialog
		{
			get => _showDialog;
			set => SetProperty(ref _showDialog, value);
		}

		public bool CanConfirm => !string.IsNullOrWhiteSpace(NewProjectName) &&
								  !string.IsNullOrWhiteSpace(NewProjectRootPath);

		public ICommand BrowseCommand { get; }
		public ICommand ConfirmCommand { get; }
		public ICommand CancelCommand { get; }

		public void Show()
		{
			NewProjectName = "新项目";
			NewProjectRootPath = string.Empty;
			ShowDialog = true;
		}

		public void Confirm()
		{
			if (!CanConfirm) return;
			ShowDialog = false;
			OnConfirm?.Invoke();
		}

		public void Cancel()
		{
			ShowDialog = false;
			NewProjectName = "新项目";
			NewProjectRootPath = string.Empty;
			OnCancel?.Invoke();
		}

		private void Browse()
		{
			var dialog = new Microsoft.Win32.OpenFolderDialog
			{
				Title = "选择包含 Git 项目的根目录"
			};

			if (dialog.ShowDialog() == true)
			{
				NewProjectRootPath = dialog.FolderName;
			}
		}
	}
}
