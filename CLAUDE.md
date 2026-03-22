# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DotNetBuilder is a Windows desktop application (WPF, C#, .NET 10) that batch manages multiple Git repositories and .NET projects. It allows syncing code (git add/commit/pull) and building multiple .NET solutions in parallel using detected MSBuild versions.

## Build

```bash
cd DotNetBuilder
dotnet build
```

The built executable is at `DotNetBuilder/bin/Debug/net10.0-windows/DotNetBuilder.exe`.

No test framework or linting tools are configured.

## Architecture

**MVVM pattern** with three layers:

- **Models/** - `GitProject` (represents a repo/project with status), `BuildModels` (MSBuildVersion, BuildResult, GitSyncResult)
- **ViewModels/** - `MainViewModel` (central logic, 1100+ lines), `RelayCommand`/`AsyncRelayCommand`, `ViewModelBase`
- **Views/** - XAML views, `CommitMessageDialog`
- **Services/** - `GitService`, `MSBuildService`, `ConfigService`
- **Converters/** - `BoolToVisibilityConverter`, `HasChangesToBrushConverter`, etc.

## Key Services

**GitService** - Scans for `.git` folders recursively, detects .NET projects (`.sln`/`.csproj`/`.vbproj`/`.fsproj`), runs `git status --porcelain` for status, performs `git add . -> git commit -> git pull` sync.

**MSBuildService** - 5-layer MSBuild detection: vswhere.exe, registry, common path scan (C:-G: drives), .NET Framework MSBuild, `dotnet msbuild`. Runs NuGet restore then MSBuild compile.

**ConfigService** - Persists `config.json` alongside the executable (in `AppDomain.CurrentDomain.BaseDirectory`). Stores per-project: MSBuild version, execute file, configuration (Release/Debug), sort order, root path.

## Key Models

**GitProject** - Implements `INotifyPropertyChanged` manually. Key properties: `IsSelected`, `HasChanges`, `ChangesCount`, `IsExpanded`, `IsSyncing`, `IsBuilding`, `ErrorMessage`, `CommitMessage`, `SelectedMSBuildVersion`, `ExecuteFile`, `Configuration`.

**MSBuildVersion** - Holds `DisplayName`, `Path`, `Version`, `VisualStudioVersion`.

## UI Components

- **AduSkin** - Third-party WPF UI library (loaded from `ThirdParty/AduSkin.dll`)
- XAML converters handle status visualization (color coding: green=normal, orange=changes, red=error)
- Log output is auto-classified into 5 types: `Error`, `Warning`, `Build` (MSBuild output), `Git` (git/nuget messages), `Message`

## Dependencies

- **AduSkin.dll** - Located in `ThirdParty/` folder (not a NuGet package)
- No other NuGet dependencies beyond .NET 10