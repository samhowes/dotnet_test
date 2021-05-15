using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.FileSystem;

namespace Binary
{
    public class BazelFileSystem : MSBuildFileSystemBase
    {
        public string TranslatePath(string path)
        {
            if (path.Length < 2 || path[1] != '$') return path;
            var firstSegmentIndex = path.IndexOf('/', 1);
            var firstSegment = path[1..firstSegmentIndex];
            var replacement = firstSegment switch
            {
                "$project_dir" => $"/Users/samh/dev/sandbox/{Program.SandboxId}/binary",
                "$src" => $"/Users/samh/dev/sandbox/{Program.SandboxId}",
                _ => null
            };
            if (replacement == null) return path;

            return replacement + path[firstSegmentIndex..];
        }

        public override TextReader ReadFile(string path)
        {
            Console.WriteLine($"ReadFile: {path}");
            return new StreamReader(path);
        }

        public override Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            Console.WriteLine($"GetFileStream: {path}");
            return new FileStream(TranslatePath(path), mode, access, share);
        }

        public override string ReadFileAllText(string path)
        {
            Console.WriteLine($"ReadFileAllText: {path}");
            return File.ReadAllText(path);
        }

        public override byte[] ReadFileAllBytes(string path)
        {
            Console.WriteLine($"ReadFileAllBytes: {path}");
            return File.ReadAllBytes(path);
        }

        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            Console.WriteLine($"EnumerateFiles: {path}");
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }

        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            Console.WriteLine($"EnumerateDirectories: {path}");
            return Directory.EnumerateDirectories(path, searchPattern, searchOption);
        }

        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            Console.WriteLine($"EnumerateFileSystemEntries: {path}");
            return Directory.EnumerateFileSystemEntries(path, searchPattern, searchOption);
        }

        public override FileAttributes GetAttributes(string path)
        {
            Console.WriteLine($"GetAttributes: {path}");
            return File.GetAttributes(path);
        }

        public override DateTime GetLastWriteTimeUtc(string path)
        {
            Console.WriteLine($"GetLastWriteTimeUtc: {path}");
            return File.GetLastWriteTimeUtc(path);
        }

        public override bool DirectoryExists(string path)
        {
            Console.WriteLine($"DirectoryExists: {path}");
            return Directory.Exists(path);
        }

        public override bool FileExists(string path)
        {
            Console.WriteLine($"FileExists: {path}");
            return File.Exists(TranslatePath(path));
        }

        public override bool FileOrDirectoryExists(string path)
        {
            Console.WriteLine($"FileOrDirectoryExists: {path}");
            return FileExists(path) || DirectoryExists(path);
        }
    }
}