# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DotNetBuilder is a Windows desktop application (WPF, C#, .NET 10) that batch manages multiple Git repositories and .NET projects. It allows syncing code (git add/commit/pull) and building multiple .NET solutions in parallel using detected MSBuild versions.

## Build

```bash
cd DotNetBuilder
dotnet build
```

For Release build:
```bash
dotnet build -c Release
```

The built executable is at `DotNetBuilder/bin/Debug/net10.0-windows/DotNetBuilder.exe` (Debug) or `DotNetBuilder/bin/Release/net10.0-windows/DotNetBuilder.exe` (Release).

No test framework or linting tools are configured.

**Runtime requirements:** .NET 10.0 Runtime, Git for Windows (in PATH), Visual Studio or .NET SDK (for MSBuild)

## Architecture

**Coordinator pattern** with MVVM - `MainViewModel` orchestrates child ViewModels:

- **ViewModels/** - `MainViewModel` (coordinator), `WelcomeViewModel`, `ToolbarViewModel`, `ProjectListViewModel`, `OutputViewModel`, `ConflictDialogViewModel`, `NewProjectDialogViewModel`. `RelayCommand`/`AsyncRelayCommand`, `ViewModelBase`
- **Models/** - `GitProject` (core model with INotifyPropertyChanged), `BuildModels` (MSBuildVersion, BuildResult, GitSyncResult, RemoteStatusInfo, enums)
- **Views/** - XAML views, `CommitMessageDialog`, `ProjectListView`, `OutputView`
- **Services/** - `GitService` (scanning), `GitSyncService` (sync operations), `MSBuildService`, `ConfigService`, `ProjectService`, `FileAssociationService`, `DialogService`
- **Converters/** - XAML converters for status visualization

## Key Services

**GitService** - Scans for `.git` folders recursively, detects .NET projects (`.sln`/`.csproj`/`.vbproj`/`.fsproj`), runs `git status --porcelain` for status.

**GitSyncService** - Performs `git add . -> git commit -> git pull` sync with conflict detection and resolution strategies (stash, abort, prompt).

**MSBuildService** - 5-layer MSBuild detection: vswhere.exe, registry, common path scan (C:-G: drives), .NET Framework MSBuild, `dotnet msbuild`. Runs NuGet restore then MSBuild compile.

**ConfigService** - Persists `config.json` alongside the executable. Stores per-project: MSBuild version, execute file, configuration, sort order, root path, pull strategy, conflict action.

**ProjectService** - Manages recent projects and project list persistence.

## Key Models

**GitProject** - Core model with manual INotifyPropertyChanged. Key properties: `IsSelected`, `HasChanges`, `ChangesCount`, `IsExpanded`, `IsSyncing`, `IsBuilding`, `ErrorMessage`, `CommitMessage`, `SelectedMSBuildVersion`, `ExecuteFile`, `Configuration`, `PullStrategy`, `ConflictAction`, `AutoCommitWhenNoMessage`.

**MSBuildVersion** - Holds `DisplayName`, `Path`, `Version`, `VisualStudioVersion`.

**GitSyncResult** - Contains `Success`, `HasCommit`, `HasConflict`, `NeedsCommitMessage`, `RemoteStatus` (RemoteStatusInfo with fast-forward/merge/rebase detection).

**BuildModels enums** - `PullStrategy` (Auto/Merge/Rebase/CommitOnly), `ConflictAction` (Prompt/AutoStash/Abort).

## UI Components

- **AduSkin** - Third-party WPF UI library (loaded from `ThirdParty/AduSkin.dll`)
- XAML converters handle status visualization (color coding: green=normal, orange=changes, red=error)
- Log output is auto-classified into 5 types: `Error`, `Warning`, `Build` (MSBuild output), `Git` (git/nuget messages), `Message`

## Dependencies

- **AduSkin.dll** - Located in `ThirdParty/` folder (not a NuGet package)
- No other NuGet dependencies beyond .NET 10