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

## Key Services

**GitService** - Scans for `.git` folders recursively, detects .NET projects (`.sln`/`.csproj`/`.vbproj`/`.fsproj`), runs `git status --porcelain` for status, performs `git add . -> git commit -> git pull` sync.

**MSBuildService** - 5-layer MSBuild detection: vswhere.exe, registry, common path scan (C:-G: drives), .NET Framework MSBuild, `dotnet msbuild`. Runs NuGet restore then MSBuild compile.

**ConfigService** - Persists `config.json` (per-project MSBuild version, execute file, configuration, sort order, root path).

## Log Classification

The app auto-classifies output into: `Error`, `Warning`, `Build` (MSBuild output), `Git` (git/nuget messages), `Message`. Filter via UI checkboxes.