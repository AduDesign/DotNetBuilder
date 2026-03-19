using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DotNetBuilder.Models;
using Microsoft.Win32;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// MSBuild服务
    /// </summary>
    public class MSBuildService
    {
        private MSBuildVersion? _selectedVersion;

        /// <summary>
        /// 当前选中的MSBuild版本
        /// </summary>
        public MSBuildVersion? SelectedVersion
        {
            get => _selectedVersion;
            set => _selectedVersion = value;
        }

        /// <summary>
        /// 检测系统中安装的所有MSBuild版本
        /// </summary>
        public List<MSBuildVersion> DetectMSBuildVersions()
        {
            var versions = new List<MSBuildVersion>();

            // 1. 使用 vswhere.exe 查找所有 VS 安装（最可靠）
            FindMSBuildWithVswhere(versions);

            // 2. 从注册表查找 Visual Studio 安装路径（备用）
            var vsPaths = GetVisualStudioInstallPathsFromRegistry();
            foreach (var vsPath in vsPaths)
            {
                AddMSBuildFromVisualStudioPath(versions, vsPath);
            }

            // 3. 扫描常见安装位置（备用方法）
            ScanCommonVisualStudioPaths(versions);

            // 4. .NET Framework MSBuild
            AddMSBuildFromNETFramework(versions);

            // 5. dotnet msbuild
            AddDotnetMSBuild(versions);

            // 去重
            versions = versions.GroupBy(v => v.Path).Select(g => g.First()).ToList();

            return versions.OrderByDescending(v => v.Version).ThenBy(v => v.VisualStudioVersion).ToList();
        }

        /// <summary>
        /// 使用 vswhere.exe 查找所有已安装的 Visual Studio
        /// </summary>
        private void FindMSBuildWithVswhere(List<MSBuildVersion> versions)
        {
            try
            {
                // vswhere.exe 的常见位置
                var vswherePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Visual Studio\Installer\vswhere.exe"),
                    @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
                    @"D:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
                };

                string? vswherePath = null;
                foreach (var path in vswherePaths)
                {
                    if (File.Exists(path))
                    {
                        vswherePath = path;
                        break;
                    }
                }

                if (vswherePath == null)
                {
                    // 尝试从 PATH 中查找 vswhere
                    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    foreach (var dir in pathEnv.Split(';'))
                    {
                        var testPath = Path.Combine(dir.Trim(), "vswhere.exe");
                        if (File.Exists(testPath))
                        {
                            vswherePath = testPath;
                            break;
                        }
                    }
                }

                if (vswherePath == null)
                    return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = vswherePath,
                    Arguments = "-prerelease -format json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // 解析 JSON 输出
                var jsonResults = System.Text.Json.JsonSerializer.Deserialize<List<VsWhereResult>>(
                    output,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (jsonResults == null) return;

                foreach (var result in jsonResults)
                {
                    if (string.IsNullOrEmpty(result.InstallationPath))
                        continue;

                    // 查找 MSBuild.exe
                    var msbuildPatterns = new[]
                    {
                        Path.Combine(result.InstallationPath, @"MSBuild\Current\Bin\MSBuild.exe"),
                        Path.Combine(result.InstallationPath, @"MSBuild\18.0\Bin\MSBuild.exe"),
                        Path.Combine(result.InstallationPath, @"MSBuild\17.0\Bin\MSBuild.exe"),
                        Path.Combine(result.InstallationPath, @"MSBuild\16.0\Bin\MSBuild.exe"),
                        Path.Combine(result.InstallationPath, @"MSBuild\15.0\Bin\MSBuild.exe"),
                    };

                    foreach (var pattern in msbuildPatterns)
                    {
                        if (File.Exists(pattern) && !versions.Any(v => v.Path.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                        {
                            var version = GetMSBuildVersionInfo(pattern);
                            var versionDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pattern)) ?? "");
                            var edition = ExtractVSEdition(result.DisplayName ?? result.InstallationPath);

                            versions.Add(new MSBuildVersion
                            {
                                DisplayName = $"{result.DisplayName} ({versionDir})",
                                Path = pattern,
                                Version = version,
                                VisualStudioVersion = result.DisplayName ?? "VS"
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private class VsWhereResult
        {
            public string? InstallationPath { get; set; }
            public string? DisplayName { get; set; }
            public string? ProductId { get; set; }
        }

        /// <summary>
        /// 从注册表获取 Visual Studio 安装路径
        /// </summary>
        private List<string> GetVisualStudioInstallPathsFromRegistry()
        {
            var paths = new List<string>();

            // VS 2022 及更高版本 (64位注册表)
            AddVSPathsFromRegistry(paths, @"SOFTWARE\Microsoft\VisualStudio\SxS\VS7", "15.0", "16.0", "17.0", "18.0");

            // VS 2019/2017 (32位注册表，在64位系统上)
            AddVSPathsFromRegistry(paths, @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS\VS7", "15.0", "16.0", "17.0");

            // VS 2019/2017 (32位注册表)
            AddVSPathsFromRegistry(paths, @"SOFTWARE\Microsoft\VisualStudio\SxS\VS7", "15.0", "16.0", "17.0");

            return paths.Distinct().ToList();
        }

        private void AddVSPathsFromRegistry(List<string> paths, string registryPath, params string[] versionKeys)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(registryPath);
                if (baseKey == null) return;

                foreach (var versionKey in versionKeys)
                {
                    var path = baseKey.GetValue(versionKey) as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            catch { }

            // 也尝试用户注册表
            try
            {
                using var baseKey = Registry.CurrentUser.OpenSubKey(registryPath);
                if (baseKey == null) return;

                foreach (var versionKey in versionKeys)
                {
                    var path = baseKey.GetValue(versionKey) as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 扫描常见的 Visual Studio 安装路径
        /// </summary>
        private void ScanCommonVisualStudioPaths(List<MSBuildVersion> versions)
        {
            // VS 安装的驱动器可能不同
            var drives = new[] { "C", "D", "E", "F", "G" };

            foreach (var drive in drives)
            {
                // VS 2026
                ScanVSPath(versions, $@"{drive}:\Program Files\Microsoft Visual Studio\2026",
                    "2026", new[] { "Enterprise", "Professional", "Community", "Preview" });

                // VS 2022
                ScanVSPath(versions, $@"{drive}:\Program Files\Microsoft Visual Studio\2022",
                    "2022", new[] { "Enterprise", "Professional", "Community", "Preview" });

                // VS 2019
                ScanVSPath(versions, $@"{drive}:\Program Files (x86)\Microsoft Visual Studio\2019",
                    "2019", new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" });

                // VS 2017
                ScanVSPath(versions, $@"{drive}:\Program Files (x86)\Microsoft Visual Studio\2017",
                    "2017", new[] { "Enterprise", "Professional", "Community", "BuildTools" });
            }
        }

        private void ScanVSPath(List<MSBuildVersion> versions, string basePath, string version, string[] editions)
        {
            if (!Directory.Exists(basePath))
                return;

            foreach (var edition in editions)
            {
                var editionPath = Path.Combine(basePath, edition);
                if (Directory.Exists(editionPath))
                {
                    AddMSBuildFromVisualStudioPath(versions, editionPath);
                }
            }
        }

        private void AddMSBuildFromVisualStudioPath(List<MSBuildVersion> versions, string vsRootPath)
        {
            if (!Directory.Exists(vsRootPath))
                return;

            // 提取版本号
            var version = ExtractVSVersion(vsRootPath);
            var edition = ExtractVSEdition(vsRootPath);

            // 查找 MSBuild 目录
            var msbuildPatterns = new[]
            {
                Path.Combine(vsRootPath, @"MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(vsRootPath, @"MSBuild\18.0\Bin\MSBuild.exe"),
                Path.Combine(vsRootPath, @"MSBuild\17.0\Bin\MSBuild.exe"),
                Path.Combine(vsRootPath, @"MSBuild\16.0\Bin\MSBuild.exe"),
                Path.Combine(vsRootPath, @"MSBuild\15.0\Bin\MSBuild.exe"),
            };

            foreach (var pattern in msbuildPatterns)
            {
                if (File.Exists(pattern) && !versions.Any(v => v.Path.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    var ver = GetMSBuildVersionInfo(pattern);
                    var dirName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pattern)) ?? "");

                    versions.Add(new MSBuildVersion
                    {
                        DisplayName = $"VS {version} {edition} ({dirName})",
                        Path = pattern,
                        Version = ver,
                        VisualStudioVersion = $"{version} {edition}"
                    });
                }
            }
        }

        private string ExtractVSVersion(string path)
        {
            if (path.Contains("2026")) return "2026";
            if (path.Contains("2022")) return "2022";
            if (path.Contains("2019")) return "2019";
            if (path.Contains("2017")) return "2017";
            if (path.Contains("16.0")) return "2019";
            if (path.Contains("17.0")) return "2022";
            if (path.Contains("18.0")) return "2026";
            return "Unknown";
        }

        private string ExtractVSEdition(string path)
        {
            if (path.Contains("Enterprise")) return "Enterprise";
            if (path.Contains("Professional")) return "Professional";
            if (path.Contains("Community")) return "Community";
            if (path.Contains("BuildTools")) return "BuildTools";
            if (path.Contains("Preview")) return "Preview";
            return "Unknown";
        }

        private void AddMSBuildFromNETFramework(List<MSBuildVersion> versions)
        {
            // .NET Framework MSBuild - 扫描多个驱动器
            var drives = new[] { "C", "D", "E", "F" };
            var netFrameworkPaths = new List<string>();

            foreach (var drive in drives)
            {
                netFrameworkPaths.Add($@"{drive}:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe");
                netFrameworkPaths.Add($@"{drive}:\Program Files\MSBuild\14.0\Bin\MSBuild.exe");
                netFrameworkPaths.Add($@"{drive}:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe");
                netFrameworkPaths.Add($@"{drive}:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe");
            }

            foreach (var path in netFrameworkPaths)
            {
                if (File.Exists(path) && !versions.Any(v => v.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    versions.Add(new MSBuildVersion
                    {
                        DisplayName = $"MSBuild {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))}",
                        Path = path,
                        Version = "4.0",
                        VisualStudioVersion = ".NET Framework"
                    });
                }
            }
        }

        private void AddDotnetMSBuild(List<MSBuildVersion> versions)
        {
            // 检查 dotnet 是否可用
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var version = process.StandardOutput.ReadLine();
                    process.WaitForExit();

                    // 添加 dotnet msbuild
                    if (!versions.Any(v => v.Path == "dotnet"))
                    {
                        versions.Add(new MSBuildVersion
                        {
                            DisplayName = $"dotnet SDK ({version ?? "unknown"})",
                            Path = "dotnet",
                            Version = version ?? "dotnet",
                            VisualStudioVersion = ".NET SDK"
                        });
                    }
                }
            }
            catch { }
        }

        private string GetMSBuildVersionInfo(string msbuildPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = msbuildPath,
                    Arguments = "/version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "Unknown";

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // 解析版本号
                var match = Regex.Match(output, @"(\d+\.\d+\.\d+\.\d+)");
                if (match.Success)
                    return match.Groups[1].Value;

                // 如果是 dotnet msbuild
                if (output.Contains("dotnet"))
                    return "dotnet";

                return output.Trim();
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// 构建项目
        /// </summary>
        /// <param name="project">项目</param>
        /// <param name="configuration">配置</param>
        /// <param name="version">MSBuild版本（可选，优先使用）</param>
        /// <param name="progress">进度回调</param>
        public async Task<BuildResult> BuildProjectAsync(GitProject project, string configuration = "Release",
            MSBuildVersion? version = null, IProgress<string>? progress = null)
        {
            var result = new BuildResult { ProjectName = project.Name };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 优先使用传入的版本，否则使用服务选中的版本
                var selectedVersion = version ?? SelectedVersion;
                var msbuildPath = selectedVersion?.Path ?? "dotnet";
                string buildTarget;

                // 确定构建目标
                if (project.SolutionPath != null && File.Exists(project.SolutionPath))
                {
                    buildTarget = project.SolutionPath;
                }
                else if (project.ProjectFilePath != null && File.Exists(project.ProjectFilePath))
                {
                    buildTarget = project.ProjectFilePath;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "未找到可构建的项目文件";
                    progress?.Report($"[{project.Name}] 未找到可构建的项目文件");
                    return result;
                }

                progress?.Report($"========================================");
                progress?.Report($"[{project.Name}] 开始构建 (配置: {configuration})");
                progress?.Report($"使用 MSBuild: {msbuildPath}");
                progress?.Report($"构建目标: {buildTarget}");
                progress?.Report($"========================================");

                // 1. 还原 NuGet 包
                progress?.Report($"[NuGet] 正在还原包...");
                await RestoreNuGetPackagesAsync(msbuildPath, buildTarget, project.Path, progress);

                // 2. 执行构建
                string arguments;
                if (msbuildPath == "dotnet")
                {
                    arguments = $"build \"{buildTarget}\" -c {configuration} /p:AllowUnsafeBlocks=true /v:n";
                }
                else
                {
                    arguments = $"\"{buildTarget}\" /t:Build /p:Configuration={configuration} /p:AllowUnsafeBlocks=true /nr:false /v:n";
                }

                progress?.Report($"[Build] 正在编译...");
                var output = await RunMSBuildAsync(msbuildPath, arguments, project.Path, progress);

                result.Output = output;

                // 解析输出获取错误和警告
                var errors = ExtractErrors(output);
                var warnings = ExtractWarnings(output);

                // dotnet build 和 MSBuild 成功输出的判断
                var hasErrors = output.Contains("error CS") || output.Contains("Error(s)") && output.Contains("Error(s) 0") == false;
                var hasSuccess = output.Contains("Build succeeded") ||
                                 output.Contains("Build SUCCEEDED") ||
                                 output.Contains("成功生成") ||
                                 (output.Contains("Error(s)") && output.Contains("0 Error(s)"));

                result.Success = hasSuccess && !hasErrors;
                result.Duration = stopwatch.Elapsed;

                // 3. 显示构建结果摘要
                progress?.Report($"========================================");
                if (result.Success)
                {
                    progress?.Report($"[{project.Name}] 构建成功 (耗时: {result.Duration.TotalSeconds:F1}s)");

                    // 4. 显示输出目录
                    var outputDirs = FindOutputDirectories(project.Path, configuration);
                    if (outputDirs.Any())
                    {
                        progress?.Report($"[Output] 输出目录:");
                        foreach (var dir in outputDirs)
                        {
                            progress?.Report($"    {dir}");
                        }
                    }

                    // 显示生成的 exe 文件
                    var exeFiles = FindOutputExecutables(project.Path, configuration);
                    if (exeFiles.Any())
                    {
                        progress?.Report($"[Exe] 可执行文件:");
                        foreach (var exe in exeFiles)
                        {
                            progress?.Report($"    {exe}");
                        }
                    }
                }
                else
                {
                    result.ErrorMessage = string.IsNullOrWhiteSpace(errors) ? "构建失败，请查看上方日志" : errors;
                    progress?.Report($"[{project.Name}] 构建失败");
                    if (!string.IsNullOrEmpty(errors))
                    {
                        progress?.Report($"[Error] {errors}");
                    }
                }
                progress?.Report($"========================================");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = stopwatch.Elapsed;
                progress?.Report($"[{project.Name}] 构建异常: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 还原 NuGet 包
        /// </summary>
        private async Task RestoreNuGetPackagesAsync(string msbuildPath, string buildTarget, string workingDirectory, IProgress<string>? progress)
        {
            try
            {
                string arguments;
                if (msbuildPath == "dotnet")
                {
                    arguments = $"restore \"{buildTarget}\"";
                }
                else
                {
                    arguments = $"\"{buildTarget}\" /t:Restore /nr:false";
                }

                var output = await RunMSBuildAsync(msbuildPath, arguments, workingDirectory, null);

                // 检查还原结果
                if (output.Contains("Restore succeeded") || output.Contains("Restore SUCCEEDED") ||
                    output.Contains("已成功生成") || output.Contains("0 Error(s)"))
                {
                    progress?.Report($"[NuGet] 包还原成功");
                }
                else if (output.Contains("Nothing to restore"))
                {
                    progress?.Report($"[NuGet] 无需还原的包");
                }
                else if (output.Contains("error"))
                {
                    progress?.Report($"[NuGet] 包还原有警告或错误，请查看上方日志");
                }
                else
                {
                    progress?.Report($"[NuGet] 包还原完成");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"[NuGet] 还原异常: {ex.Message}");
            }
        }

        private async Task<string> RunMSBuildAsync(string msbuildPath, string arguments, string? workingDirectory = null, IProgress<string>? progress = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = msbuildPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(msbuildPath)
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    // 实时输出日志
                    progress?.Report(e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    // 实时输出错误日志
                    progress?.Report(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return output.ToString();
        }

        private string? ExtractErrors(string output)
        {
            var lines = output.Split('\n');
            var errors = lines.Where(l => l.Contains("error CS") || l.Contains("Error(s)") || l.Contains("error:")).Take(5);
            return string.Join("\n", errors);
        }

        private string? ExtractWarnings(string output)
        {
            var lines = output.Split('\n');
            var warnings = lines.Where(l => l.Contains("warning CS") || l.Contains("Warning(s)")).Take(3);
            return string.Join("\n", warnings);
        }

        /// <summary>
        /// 查找输出目录
        /// </summary>
        public List<string> FindOutputDirectories(string projectPath, string configuration = "Release")
        {
            var directories = new List<string>();

            // 常见的输出目录
            var outputPaths = new[]
            {
                Path.Combine(projectPath, "bin", configuration),
                Path.Combine(projectPath, "bin", configuration, "net*"),
                Path.Combine(projectPath, "out", configuration),
                Path.Combine(projectPath, "artifacts", "bin", configuration),
                Path.Combine(projectPath, "artifacts", configuration),
            };

            foreach (var pathPattern in outputPaths)
            {
                if (pathPattern.Contains("*"))
                {
                    var basePath = Path.GetDirectoryName(pathPattern);
                    var pattern = Path.GetFileName(pathPattern);
                    if (basePath != null && Directory.Exists(basePath))
                    {
                        try
                        {
                            var dirs = Directory.GetDirectories(basePath, pattern);
                            directories.AddRange(dirs);
                        }
                        catch { }
                    }
                }
                else if (Directory.Exists(pathPattern))
                {
                    directories.Add(pathPattern);
                }
            }

            return directories.Distinct().ToList();
        }

        /// <summary>
        /// 查找项目生成的exe文件
        /// </summary>
        public List<string> FindOutputExecutables(string projectPath, string configuration = "Release")
        {
            var executables = new List<string>();

            // 常见的输出目录
            var outputPaths = new[]
            {
                Path.Combine(projectPath, "bin", configuration),
                Path.Combine(projectPath, "bin", configuration, "netcoreapp*"),
                Path.Combine(projectPath, "bin", configuration, "net*"),
                Path.Combine(projectPath, "bin", configuration, "win-x64"),
                Path.Combine(projectPath, "bin", configuration, "win-x86"),
                Path.Combine(projectPath, "bin", configuration, "linux-x64"),
                Path.Combine(projectPath, "bin", configuration, "osx-x64"),
                Path.Combine(projectPath, "out", configuration),
                Path.Combine(projectPath, "target", configuration),
                Path.Combine(projectPath, "obj", configuration, "bin"),
                Path.Combine(projectPath, "artifacts", "bin", configuration),
            };

            foreach (var pathPattern in outputPaths)
            {
                if (pathPattern.Contains("*"))
                {
                    var basePath = Path.GetDirectoryName(pathPattern);
                    var pattern = Path.GetFileName(pathPattern);
                    if (basePath != null && Directory.Exists(basePath))
                    {
                        try
                        {
                            var dirs = Directory.GetDirectories(basePath, pattern);
                            foreach (var dir in dirs)
                            {
                                FindExeFiles(dir, executables);
                            }
                        }
                        catch { }
                    }
                }
                else if (Directory.Exists(pathPattern))
                {
                    FindExeFiles(pathPattern, executables);
                }
            }

            return executables.Distinct().ToList();
        }

        private void FindExeFiles(string directory, List<string> executables)
        {
            try
            {
                var exeFiles = Directory.GetFiles(directory, "*.exe");
                foreach (var exe in exeFiles)
                {
                    // 排除某些不需要的exe
                    var fileName = Path.GetFileName(exe).ToLower();
                    if (!fileName.Contains("vshost") && !fileName.Contains("unittest"))
                    {
                        executables.Add(exe);
                    }
                }
            }
            catch { }
        }
    }
}
