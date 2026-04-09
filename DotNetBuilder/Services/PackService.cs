using System.IO;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 打包服务 - 使用 SharpCompress 实现 7z 压缩
    /// </summary>
    public class PackService
    {
        /// <summary>
        /// 获取目录中的所有文件和文件夹（用于 TreeView）
        /// </summary>
        public List<FileTreeNode> GetDirectoryTree(string path)
        {
            var nodes = new List<FileTreeNode>();

            if (!Directory.Exists(path))
                return nodes;

            try
            {
                var entries = Directory.GetFileSystemEntries(path);
                foreach (var entry in entries)
                {
                    var node = CreateTreeNode(entry);
                    nodes.Add(node);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无权限访问的目录
            }
            catch (Exception)
            {
                // 忽略其他错误
            }

            return nodes;
        }

        private FileTreeNode CreateTreeNode(string path)
        {
            var isDirectory = Directory.Exists(path);
            var name = Path.GetFileName(path);

            var node = new FileTreeNode
            {
                Name = name,
                FullPath = path,
                IsDirectory = isDirectory,
                IsSelected = false,
                IsExpanded = false
            };

            if (isDirectory)
            {
                try
                {
                    var entries = Directory.GetFileSystemEntries(path);
                    foreach (var entry in entries)
                    {
                        var childNode = CreateTreeNode(entry);
                        node.Children.Add(childNode);
                    }
                }
                catch
                {
                    // 忽略错误
                }
            }

            return node;
        }

        /// <summary>
        /// 使用 SharpCompress 压缩文件
        /// </summary>
        public async Task<PackResult> PackAsync(
            string outputPath,
            IEnumerable<string> files,
            string? password = null,
            string? baseDirectory = null,
            IProgress<string>? progress = null)
        {
            var fileList = files.ToList();

            if (fileList.Count == 0)
            {
                return new PackResult
                {
                    Success = false,
                    ErrorMessage = "没有选择要压缩的文件"
                };
            }

            try
            {
                progress?.Report($"正在压缩 {fileList.Count} 个文件...");

                // 确保输出目录存在
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // 删除已存在的文件
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                return await Task.Run(() =>
                {
                    try
                    {
                        using var stream = File.Create(outputPath);
                        var options = new SevenZipWriterOptions();
                        using var writer = new SevenZipWriter(stream, options);

                        int processed = 0;
                        foreach (var file in fileList)
                        {
                            if (!File.Exists(file))
                                continue;

                            var fileName = !string.IsNullOrEmpty(baseDirectory)
                                ? Path.GetRelativePath(baseDirectory, file)
                                : Path.GetFileName(file);

                            var fileInfo = new FileInfo(file);
                            using var fileStream = File.OpenRead(file);
                            writer.Write(fileName, fileStream, fileInfo.LastWriteTime);
                            processed++;
                            progress?.Report($"已压缩 {processed}/{fileList.Count} 个文件...");
                        }

                        progress?.Report("压缩完成");
                        return new PackResult { Success = true };
                    }
                    catch (Exception ex)
                    {
                        return new PackResult
                        {
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                    }
                });
            }
            catch (Exception ex)
            {
                return new PackResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 使用 SharpCompress 压缩单个目录
        /// </summary>
        public async Task<PackResult> PackDirectoryAsync(
            string directoryPath,
            string outputPath,
            string? password = null,
            IProgress<string>? progress = null)
        {
            if (!Directory.Exists(directoryPath))
            {
                return new PackResult
                {
                    Success = false,
                    ErrorMessage = $"目录不存在: {directoryPath}"
                };
            }

            try
            {
                progress?.Report($"正在压缩目录: {directoryPath}");

                // 确保输出目录存在
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                return await Task.Run(() =>
                {
                    try
                    {
                        using var stream = File.Create(outputPath);
                        var options = new SevenZipWriterOptions();
                        using var writer = new SevenZipWriter(stream, options);

                        var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                        int processed = 0;

                        foreach (var file in files)
                        {
                            var relativePath = Path.GetRelativePath(directoryPath, file);
                            var fileInfo = new FileInfo(file);
                            using var fileStream = File.OpenRead(file);
                            writer.Write(relativePath, fileStream, fileInfo.LastWriteTime);
                            processed++;
                            if (processed % 10 == 0)
                            {
                                progress?.Report($"已压缩 {processed}/{files.Length} 个文件...");
                            }
                        }

                        progress?.Report("压缩完成");
                        return new PackResult { Success = true };
                    }
                    catch (Exception ex)
                    {
                        return new PackResult
                        {
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                    }
                });
            }
            catch (Exception ex)
            {
                return new PackResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 获取所有选中的文件路径
        /// </summary>
        public static IEnumerable<string> GetSelectedFiles(FileTreeNode node, string? basePath = null)
        {
            var files = new List<string>();

            if (node.IsSelected && !node.IsDirectory)
            {
                files.Add(node.FullPath);
            }

            foreach (var child in node.Children)
            {
                files.AddRange(GetSelectedFiles(child, basePath));
            }

            return files;
        }

        /// <summary>
        /// 全选/取消全选所有子节点
        /// </summary>
        public static void SetChildrenSelected(FileTreeNode node, bool selected)
        {
            node.IsSelected = selected;
            foreach (var child in node.Children)
            {
                SetChildrenSelected(child, selected);
            }
        }
    }

    /// <summary>
    /// 文件树节点
    /// </summary>
    public class FileTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool IsSelected { get; set; }
        public bool IsExpanded { get; set; }
        public List<FileTreeNode> Children { get; set; } = new();
    }

    /// <summary>
    /// 压缩结果
    /// </summary>
    public class PackResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
