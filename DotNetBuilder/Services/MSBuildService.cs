using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        /// MSBuild 主版本探测表：key=匹配模式，value=主版本号
        /// Pattern 按优先级从高到低排列，先匹配优先
        /// </summary>
        private static readonly List<(IList<string> PathPatterns, int Major)> _msbuildVersionPatterns = new()
        {
            (new[] { @"msbuild\18.0", "2026" },     18),
            (new[] { @"msbuild\17.0", "2022" },     17),
            (new[] { @"msbuild\16.0", "2019" },     16),
            (new[] { @"msbuild\15.0", "2017" },     15),
            (new[] { @"msbuild\14.0", "net framework" }, 14),
        };

        /// <summary>
        /// 单条评分规则
        /// </summary>
        /// <param name="Condition">项目特征是否满足此规则：参数=(targetNetMajor, solutionMajor, msbuildMajor)</param>
        /// <param name="Usable">此 MSBuild 版本是否可用于该项目：参数=(msbuildMajor, solutionMajor)</param>
        /// <param name="Score">评分函数，基于 msbuildMajor 计算</param>
        private class ScoreRule
        {
            public Func<int?, int?, int?, bool> Condition { get; init; } = (_, _, _) => false;
            public Func<int?, int?, bool> Usable { get; init; } = (_, _) => false;
            public Func<int?, int?> Score { get; init; } = _ => null;
        }

        private static readonly List<ScoreRule> _scoreRules = BuildScoreRules();

        private static List<ScoreRule> BuildScoreRules()
        {
            return new List<ScoreRule>
            {
                // ---- .NET Framework 项目 ----
                new() { Condition = (_, __, ___) => true, Usable = (m, _) => m >= 16, Score = m => 100 + m },  // VS 2019/2022
                new() { Condition = (_, __, ___) => true, Usable = (m, _) => m == 14, Score = _ => 50 },      // MSBuild 14.0

                // ---- .NET 5+/Core 项目，目标框架已解析 ----
                new() { Condition = (tf, _, __) => tf.HasValue && tf < 5, Usable = (m, _) => m == 0, Score = _ => 100 },           // dotnet SDK
                new() { Condition = (tf, _, __) => tf.HasValue, Usable = (m, _) => m >= 17, Score = m => 80 + m },                // VS 2022+
                new() { Condition = (tf, _, __) => tf.HasValue && tf <= 3, Usable = (m, _) => m == 16, Score = _ => 60 },          // VS 2019

                // ---- 解决方案格式版本已解析 ----
                new() { Condition = (_, sln, __) => sln.HasValue, Usable = (m, _) => m == 0, Score = _ => 70 },                                                                              // dotnet SDK（通用备选）
                new() { Condition = (_, sln, __) => sln.HasValue, Usable = (m, sln) => m == sln, Score = _ => 200 },                                                                           // 精确版本
                new() { Condition = (_, sln, __) => sln.HasValue, Usable = (m, sln) => Math.Abs((m ?? 0) - (sln ?? 0)) <= 1, Score = _ => 90 },  // 邻近版本
            };
        }

        /// <summary>
        /// 从 MSBuildVersion 中探测主版本号，失败返回 null
        /// </summary>
        private int? DetectMSBuildMajor(MSBuildVersion v)
        {
            var pathLower = v.Path.ToLower();
            var displayLower = v.DisplayName.ToLower();

            if (v.VisualStudioVersion == ".NET SDK" || pathLower == "dotnet")
                return 0;

            foreach (var (patterns, major) in _msbuildVersionPatterns)
            {
                if (patterns.Any(p => pathLower.Contains(p) || displayLower.Contains(p)))
                    return major;
            }
            return null;
        }

        /// <summary>
        /// 根据项目文件信息，匹配最合适的 MSBuild 版本
        /// </summary>
        /// <param name="projectInfo">解析后的项目文件信息</param>
        /// <param name="availableVersions">可选，指定 MSBuild 版本列表，默认使用已检测的版本</param>
        /// <returns>最匹配的 MSBuild 版本，未找到则返回 null</returns>
        public MSBuildVersion? MatchBestMSBuild(ProjectFileInfo? projectInfo, IList<MSBuildVersion>? availableVersions = null)
        {
            if (projectInfo == null)
                return null;

            var versions = availableVersions ?? DetectMSBuildVersions();
            if (versions.Count == 0)
                return null;

            // 提取项目特征
            int? targetNetMajor = null;
            if (!string.IsNullOrEmpty(projectInfo.TargetFramework))
            {
                var m = Regex.Match(projectInfo.TargetFramework, @"net(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int major))
                    targetNetMajor = major;
            }

            int? solutionMajor = null;
            if (!string.IsNullOrEmpty(projectInfo.SolutionFormatVersion))
            {
                var m = Regex.Match(projectInfo.SolutionFormatVersion, @"v(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int sm))
                    solutionMajor = sm;
            }

            bool isNetFramework = !string.IsNullOrEmpty(projectInfo.TargetFrameworkVersion) ||
                (targetNetMajor == null && solutionMajor == null);

            MSBuildVersion? best = null;
            int bestScore = int.MinValue;

            foreach (var v in versions)
            {
                int? msbuildMajor = DetectMSBuildMajor(v);
                if (msbuildMajor == null)
                    continue;

                int? effectiveTargetMajor = isNetFramework ? null : targetNetMajor;
                int? effectiveSlnMajor = isNetFramework ? null : solutionMajor;

                foreach (var rule in _scoreRules)
                {
                    if (!rule.Condition(effectiveTargetMajor, effectiveSlnMajor, msbuildMajor))
                        continue;

                    if (!rule.Usable(msbuildMajor, effectiveSlnMajor))
                        continue;

                    var rawScore = rule.Score(msbuildMajor);
                    if (rawScore == null)
                        continue;
                    int score = rawScore.Value;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = v;
                    }
                    break; // 规则已按优先级排列，匹配到即停
                }
            }

            return best;
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
        /// 解析 .sln 文件，获取其中包含的所有项目路径
        /// </summary>
        public List<string> ParseSlnProjects(string slnPath)
        {
            var projects = new List<string>();
            try
            {
                if (!File.Exists(slnPath))
                    return projects;

                var lines = File.ReadAllLines(slnPath);
                var slnDir = Path.GetDirectoryName(slnPath) ?? "";

                foreach (var line in lines)
                {
                    // 格式: Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ProjectName", "Path\Project.csproj", "{GUID}"
                    var match = Regex.Match(line, @"Project\("".*?""\) = "".*?"", ""(.+?)"", """);
                    if (match.Success)
                    {
                        var relativePath = match.Groups[1].Value;
                        var fullPath = Path.GetFullPath(Path.Combine(slnDir, relativePath));
                        if (File.Exists(fullPath))
                        {
                            projects.Add(fullPath);
                        }
                    }
                }
            }
            catch { }
            return projects;
        }

        /// <summary>
        /// 解析 .csproj 文件，获取其 ProjectReference 列表
        /// </summary>
        private List<string> ParseProjectReferences(string csprojPath)
        {
            var references = new List<string>();
            try
            {
                if (!File.Exists(csprojPath))
                    return references;

                var doc = XDocument.Load(csprojPath);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                foreach (var pr in doc.Descendants(ns + "ProjectReference"))
                {
                    var include = pr.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                    {
                        var csprojDir = Path.GetDirectoryName(csprojPath) ?? "";
                        var refPath = Path.GetFullPath(Path.Combine(csprojDir, include));
                        references.Add(refPath);
                    }
                }
            }
            catch { }
            return references;
        }

        /// <summary>
        /// 按依赖顺序对项目进行拓扑排序（被依赖的先构建）
        /// </summary>
        private List<string> SortProjectsByDependencies(List<string> csprojFiles)
        {
            // 规范化所有路径
            var normalizedFiles = csprojFiles
                .Select(f => Path.GetFullPath(f))
                .ToList();

            var normalizedSet = normalizedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // 构建依赖图：graph[A] = A依赖的项目集合
            foreach (var csproj in normalizedFiles)
            {
                graph[csproj] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var refPath in ParseProjectReferences(csproj))
                {
                    // 规范化引用路径后再匹配
                    var normalizedRef = Path.GetFullPath(refPath);
                    if (normalizedSet.Contains(normalizedRef))
                        graph[csproj].Add(normalizedRef);
                }
            }

            var result = new List<string>();
            var inDegree = normalizedFiles.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);

            // 计算入度：inDegree[X] = X 被多少个项目依赖
            foreach (var deps in graph.Values)
            {
                foreach (var dep in deps)
                {
                    if (inDegree.ContainsKey(dep))
                        inDegree[dep]++;
                }
            }

            // 从入度为0的开始（不被任何项目依赖的叶子项目）
            var queue = new Queue<string>();
            foreach (var csproj in normalizedFiles.Where(c => inDegree[c] == 0))
                queue.Enqueue(csproj);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);
                foreach (var kvp in graph.Where(k => k.Value.Contains(current)))
                {
                    inDegree[kvp.Key]--;
                    if (inDegree[kvp.Key] == 0)
                        queue.Enqueue(kvp.Key);
                }
            }

            // 处理循环依赖或无法排序的项目（放到最后）
            foreach (var csproj in normalizedFiles.Where(c => !result.Contains(c)))
                result.Add(csproj);

            return result;
        }

        /// <summary>
        /// 获取目录下所有 .csproj 文件（递归搜索）
        /// </summary>
        public List<string> GetAllCsprojFiles(string rootPath)
        {
            try
            {
                return Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories).ToList();
            }
            catch
            {
                return new List<string>();
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
                var selectedVersion = version ?? SelectedVersion;
                var msbuildPath = selectedVersion?.Path ?? "dotnet";

                // 确定构建策略
                string? slnPath = project.SolutionPath;
                List<string> csprojFiles = new();
                bool useSolutionBuild = !string.IsNullOrEmpty(slnPath) && File.Exists(slnPath);

                if (useSolutionBuild)
                {
                    // 策略1: 有 .sln，直接构建整个解决方案（行为最接近 Visual Studio）
                    progress?.Report($"========================================");
                    progress?.Report($"[{project.Name}] 检测到解决方案: {Path.GetFileName(slnPath)}");

                    // 获取工作目录（.sln 所在目录）
                    var slnWorkingDir = Path.GetDirectoryName(slnPath) ?? project.Path;
                    progress?.Report($"[{project.Name}] 开始构建 (配置: {configuration})");
                    progress?.Report($"使用 MSBuild: {msbuildPath}");
                    progress?.Report($"========================================");

                    // 还原 NuGet 包
                    progress?.Report($"[NuGet] 正在还原包...");
                    await RestoreNuGetPackagesAsync(project.Name, msbuildPath, slnPath!, slnWorkingDir, progress);

                    // 直接构建 .sln 文件
                    progress?.Report($"\n--- 构建解决方案: {Path.GetFileName(slnPath)} ---");

                    string args = msbuildPath == "dotnet"
                        ? $"build \"{slnPath}\" -c {configuration} /p:AllowUnsafeBlocks=true /v:n"
                        : $"\"{slnPath}\" /t:Build /p:Configuration={configuration} /p:AllowUnsafeBlocks=true /nr:false /v:n";

                    var output = await RunMSBuildAsync(msbuildPath, args, slnWorkingDir, progress);

                    // 检查构建结果
                    var hasErrors = output.Contains("error CS") || (output.Contains("Error(s)") && !output.Contains("0 Error(s)"));
                    var hasSuccess = output.Contains("Build succeeded") ||
                                     output.Contains("Build SUCCEEDED") ||
                                     output.Contains("成功生成") ||
                                     (output.Contains("Error(s)") && output.Contains("0 Error(s)"));

                    if (!hasSuccess || hasErrors)
                    {
                        result.Success = false;
                        result.ErrorMessage = "解决方案构建失败";
                        progress?.Report($"[失败] 解决方案构建失败");
                    }
                    else
                    {
                        result.Success = true;
                        progress?.Report($"[成功] 解决方案构建成功");
                    }

                    result.Duration = stopwatch.Elapsed;
                    progress?.Report($"\n========================================");
                    progress?.Report($"[{project.Name}] 构建完成 (耗时: {result.Duration.TotalSeconds:F1}s)");
                    progress?.Report($"========================================");
                    return result;
                }
                else
                {
                    // 策略2: 没有 .sln，收集所有 .csproj 并按依赖顺序构建
                    progress?.Report($"========================================");
                    progress?.Report($"[{project.Name}] 未检测到解决方案，搜索 .csproj 文件...");

                    csprojFiles = GetAllCsprojFiles(project.Path);
                    progress?.Report($"[{project.Name}] 找到 {csprojFiles.Count} 个 .csproj");

                    if (csprojFiles.Count == 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "未找到可构建的项目文件";
                        return result;
                    }

                    // 按依赖顺序排序
                    csprojFiles = SortProjectsByDependencies(csprojFiles);

                    // 输出构建顺序
                    progress?.Report($"[{project.Name}] 构建顺序（按依赖关系）:");
                    for (int i = 0; i < csprojFiles.Count; i++)
                    {
                        var displayPath = GetRelativePath(csprojFiles[i], project.Path);
                        progress?.Report($"  [{i + 1}] {displayPath}");
                    }
                    progress?.Report($"[{project.Name}] 开始构建 (配置: {configuration})");
                    progress?.Report($"使用 MSBuild: {msbuildPath}");
                    progress?.Report($"========================================");

                    // 还原 NuGet 包（对第一个项目执行一次即可）
                    progress?.Report($"[NuGet] 正在还原包...");
                    await RestoreNuGetPackagesAsync(project.Name, msbuildPath, csprojFiles[0], project.Path, progress);

                    // 逐个构建所有项目
                    bool allSuccess = true;
                    var failedProjects = new List<string>();
                    var successCount = 0;

                    for (int i = 0; i < csprojFiles.Count; i++)
                    {
                        var csprojPath = csprojFiles[i];
                        var csprojName = Path.GetFileNameWithoutExtension(csprojPath);
                        var relDir = Path.GetDirectoryName(csprojPath) ?? "";
                        var displayPath = relDir.StartsWith(project.Path)
                            ? relDir.Substring(project.Path.Length).TrimStart('\\', '/') + "\\" + Path.GetFileName(csprojPath)
                            : Path.GetFileName(relDir) + "\\" + Path.GetFileName(csprojPath);

                        progress?.Report($"\n--- [{i + 1}/{csprojFiles.Count}] 构建: {displayPath} ---");

                        string args = msbuildPath == "dotnet"
                            ? $"build \"{csprojPath}\" -c {configuration} /p:AllowUnsafeBlocks=true /v:n"
                            : $"\"{csprojPath}\" /t:Build /p:Configuration={configuration} /p:AllowUnsafeBlocks=true /nr:false /v:n";

                        var output = await RunMSBuildAsync(msbuildPath, args, project.Path, progress);

                        var hasErrors = output.Contains("error CS") || (output.Contains("Error(s)") && !output.Contains("0 Error(s)"));
                        var hasSuccess = output.Contains("Build succeeded") ||
                                         output.Contains("Build SUCCEEDED") ||
                                         output.Contains("成功生成") ||
                                         (output.Contains("Error(s)") && output.Contains("0 Error(s)"));

                        if (!hasSuccess || hasErrors)
                        {
                            allSuccess = false;
                            failedProjects.Add(displayPath);
                            progress?.Report($"[失败] {displayPath}");
                        }
                        else
                        {
                            successCount++;
                            progress?.Report($"[成功] {displayPath}");
                        }
                    }

                    result.Success = allSuccess;
                    result.Duration = stopwatch.Elapsed;

                    progress?.Report($"\n========================================");
                    if (result.Success)
                    {
                        progress?.Report($"[{project.Name}] 全部构建成功 ({csprojFiles.Count} 个项目，耗时: {result.Duration.TotalSeconds:F1}s)");
                    }
                    else
                    {
                        result.ErrorMessage = $"{failedProjects.Count} 个项目构建失败:\n" + string.Join("\n", failedProjects);
                        progress?.Report($"[{project.Name}] 构建完成，{successCount}/{csprojFiles.Count} 个成功，{failedProjects.Count} 个失败:");
                        foreach (var fp in failedProjects)
                            progress?.Report($"  - {fp}");
                    }
                }

                // 显示输出
                var outputDirs = FindOutputDirectories(project.Path, configuration);
                if (outputDirs.Any())
                {
                    progress?.Report($"[Output] 输出目录:");
                    foreach (var dir in outputDirs.Take(5))
                        progress?.Report($"    {dir}");
                }

                var exeFiles = FindOutputExecutables(project.Path, configuration);
                if (exeFiles.Any())
                {
                    progress?.Report($"[Exe] 可执行文件:");
                    foreach (var exe in exeFiles.Take(10))
                        progress?.Report($"    {exe}");
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
        private async Task RestoreNuGetPackagesAsync(string projectName, string msbuildPath, string buildTarget, string workingDirectory, IProgress<string>? progress)
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
                    progress?.Report($"[{projectName}] [NuGet] 包还原成功");
                }
                else if (output.Contains("Nothing to restore"))
                {
                    progress?.Report($"[{projectName}] [NuGet] 无需还原的包");
                }
                else if (output.Contains("error"))
                {
                    progress?.Report($"[{projectName}] [NuGet] 包还原有警告或错误，请查看上方日志");
                }
                else
                {
                    progress?.Report($"[{projectName}] [NuGet] 包还原完成");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"[{projectName}] [NuGet] 还原异常: {ex.Message}");
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

        /// <summary>
        /// 获取相对路径用于显示
        /// </summary>
        private string GetRelativePath(string fullPath, string basePath)
        {
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var rel = fullPath.Substring(basePath.Length).TrimStart('\\', '/');
                return string.IsNullOrEmpty(rel) ? Path.GetFileName(fullPath) : rel;
            }
            return fullPath;
        }
    }
}
