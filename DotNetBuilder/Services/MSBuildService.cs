using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DotNetBuilder.Models;

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

            // Visual Studio 2022 及更高版本
            AddMSBuildFromVisualStudio(versions, @"C:\Program Files\Microsoft Visual Studio\2022",
                new[] { "Enterprise", "Professional", "Community", "Preview" });

            // Visual Studio 2019
            AddMSBuildFromVisualStudio(versions, @"C:\Program Files (x86)\Microsoft Visual Studio\2019",
                new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" });

            // Visual Studio 2017
            AddMSBuildFromVisualStudio(versions, @"C:\Program Files (x86)\Microsoft Visual Studio\2017",
                new[] { "Enterprise", "Professional", "Community", "BuildTools" });

            // .NET Framework SDK (通过 registry 或默认安装路径)
            AddMSBuildFromNETFramework(versions);

            // 如果没有找到任何版本，添加 dotnet msbuild 作为备选
            if (versions.Count == 0)
            {
                versions.Add(new MSBuildVersion
                {
                    DisplayName = "dotnet msbuild (默认)",
                    Path = "dotnet",
                    Version = "dotnet",
                    VisualStudioVersion = "Latest"
                });
            }

            return versions.OrderByDescending(v => v.Version).ThenBy(v => v.VisualStudioVersion).ToList();
        }

        private void AddMSBuildFromVisualStudio(List<MSBuildVersion> versions, string basePath, string[] editions)
        {
            if (!Directory.Exists(basePath))
                return;

            foreach (var edition in editions)
            {
                var editionPath = Path.Combine(basePath, edition);
                if (!Directory.Exists(editionPath))
                    continue;

                // 查找 MSBuild 目录
                var msbuildPath = Path.Combine(editionPath, @"MSBuild\Current\Bin\MSBuild.exe");
                if (File.Exists(msbuildPath))
                {
                    var version = GetMSBuildVersionInfo(msbuildPath);
                    versions.Add(new MSBuildVersion
                    {
                        DisplayName = $"VS {GetVSVersionName(basePath)} {edition}",
                        Path = msbuildPath,
                        Version = version,
                        VisualStudioVersion = $"{GetVSVersionName(basePath)} {edition}"
                    });
                }

                // 查找其他版本
                var currentPath = Path.Combine(editionPath, @"MSBuild\Current\Bin");
                if (Directory.Exists(currentPath))
                {
                    var subDirs = Directory.GetDirectories(Path.Combine(editionPath, "MSBuild"));
                    foreach (var subDir in subDirs)
                    {
                        var binPath = Path.Combine(subDir, "Bin", "MSBuild.exe");
                        if (File.Exists(binPath) && !versions.Any(v => v.Path == binPath))
                        {
                            var version = GetMSBuildVersionInfo(binPath);
                            var dirName = Path.GetFileName(subDir);
                            versions.Add(new MSBuildVersion
                            {
                                DisplayName = $"VS {GetVSVersionName(basePath)} {edition} ({dirName})",
                                Path = binPath,
                                Version = version,
                                VisualStudioVersion = $"{GetVSVersionName(basePath)} {edition}"
                            });
                        }
                    }
                }
            }
        }

        private void AddMSBuildFromNETFramework(List<MSBuildVersion> versions)
        {
            // .NET Framework MSBuild
            var netFrameworkPaths = new[]
            {
                @"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe",
                @"C:\Program Files\MSBuild\14.0\Bin\MSBuild.exe",
                @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
            };

            foreach (var path in netFrameworkPaths)
            {
                if (File.Exists(path) && !versions.Any(v => v.Path == path))
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

        private string GetVSVersionName(string basePath)
        {
            if (basePath.Contains("2022")) return "2022";
            if (basePath.Contains("2019")) return "2019";
            if (basePath.Contains("2017")) return "2017";
            return "Unknown";
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
                string arguments;
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

                // 构建参数
                if (msbuildPath == "dotnet")
                {
                    arguments = $"build \"{buildTarget}\" -c {configuration}";
                }
                else
                {
                    arguments = $"\"{buildTarget}\" /t:Build /p:Configuration={configuration} /nr:false";
                }

                progress?.Report($"[{project.Name}] 开始构建 (配置: {configuration})...");
                progress?.Report($"使用 MSBuild: {msbuildPath}");

                var output = await RunMSBuildAsync(msbuildPath, arguments);

                result.Output = output;
                result.Success = output.Contains("0 Error(s)") || output.Contains("Build succeeded");
                result.Duration = stopwatch.Elapsed;

                if (result.Success)
                {
                    progress?.Report($"[{project.Name}] 构建成功 (耗时: {result.Duration.TotalSeconds:F1}s)");
                }
                else
                {
                    result.ErrorMessage = ExtractErrors(output);
                    progress?.Report($"[{project.Name}] 构建失败");
                    progress?.Report(result.ErrorMessage ?? "未知错误");
                }
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

        private async Task<string> RunMSBuildAsync(string msbuildPath, string arguments)
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
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
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
