#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Framework;
using Microsoft.Build.Graph;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;
using Microsoft.Build.Tasks;
using NuGet.Commands;
using NuGet.ProjectModel;

namespace Binary
{
    class Program
    {
        public static int SandboxId = 4;
        public static string Sandbox => $"/Users/samh/dev/sandbox/{SandboxId}";

        private static List<string> _cacheFiles = new List<string>()
            {$"{Sandbox}/library/library.csproj.cache"};

        // const string SdkVersion = "3.1.201";
        const string SdkVersion = "5.0.202";

        static bool VerboseEnabled = false;

        public static void Verbose(string? message)
        {
            if (!VerboseEnabled) return;
            Console.WriteLine(message);
        }

        static async Task Main(string[] args)
        {
            MSBuildLocator.RegisterMSBuildPath($"/usr/local/share/dotnet/sdk/{SdkVersion}/");
            await Build();
        }

        static string CacheFile(string project, string mnemonic)
        {
            return $"/Users/samh/dev/sandbox/{SandboxId}/{project}/{project}.csproj.{mnemonic}.cache";
        }

        private static async Task Build()
        {
            var dirs = Directory.EnumerateDirectories("/Users/samh/dev/sandbox").Single();
            var dir = Path.GetFileNameWithoutExtension(dirs)!;
            SandboxId = Int32.Parse(dir);
            Environment.CurrentDirectory = Sandbox;

            Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(Sandbox, "packages"));
            Environment.SetEnvironmentVariable("NuGetPackageRoot", Path.Combine(Sandbox, "packages"));
            Environment.SetEnvironmentVariable("NuGetPackageFolders", Path.Combine(Sandbox, "packages"));

            string mnemonic = null;
            string project = null;
            switch (SandboxId)
            {
                case 1:
                    mnemonic = "restore";
                    project = "transitive";
                    _cacheFiles = null;
                    break;
                case 2:
                    mnemonic = "build";
                    project = "transitive";
                    _cacheFiles = new List<string>() {CacheFile("transitive", "restore")};
                    break;
                case 3:
                    mnemonic = "restore";
                    project = "library";
                    _cacheFiles = new List<string>()
                    {
                        CacheFile("transitive", "restore")
                    };
                    break;
                case 4:
                    mnemonic = "build";
                    project = "library";
                    _cacheFiles = new List<string>()
                    {
                        // CacheFile("library", "restore"),
                        CacheFile("transitive", "restore"),
                        CacheFile("transitive", "build"),
                    };
                    break;
                case 5:
                    mnemonic = "restore";
                    project = "binary";
                    _cacheFiles = new List<string>()
                    {
                        CacheFile("transitive", "restore"),
                        CacheFile("library", "restore")
                    };
                    break;
                case 6:
                    mnemonic = "build";
                    project = "binary";
                    _cacheFiles = new List<string>()
                    {
                        CacheFile("transitive", "build"),
                        CacheFile("library", "build"),
                        CacheFile("binary", "restore")
                    };
                    break;
                case 7:
                    mnemonic = "publish";
                    project = "binary";
                    _cacheFiles = new List<string>()
                    {
                        CacheFile("binary", "build")
                    };
                    break;
            }

            var files = new BazelFileSystem();
            var logger = new ConsoleLogger(LoggerVerbosity.Normal);
            // logger.Parameters = "SHOWPROJECTFILE=TRUE;";

            // globalProjectCollection loads environmentVariables on Get.
            Environment.SetEnvironmentVariable("ExecRoot", Environment.CurrentDirectory);
            var pc = ProjectCollection.GlobalProjectCollection;
            pc.RegisterLogger(logger);
            pc.RegisterLogger(new BazelLogger());
            var globalProperties = new Dictionary<string, string>();
            globalProperties["ImportDirectoryBuildProps"] = "false";
            // pc.SetGlobalProperty("ImportDirectoryBuildProps", "false");
            if (mnemonic == "restore")
            {
                // this one is auto-set by NuGet.targets in Restore when restoring a referenced project. If we don't set it
                // ahead of time, there will be a cache miss on the restored project.
                // pc.SetGlobalProperty("ExcludeRestorePackageImports", "true");
                // pc.SetGlobalProperty("RestoreRecursive", "false");
                globalProperties["ExcludeRestorePackageImports"] = "true";
                globalProperties["RestoreRecursive"] = "false";
                globalProperties["RestoreUseStaticGraphEvaluation"] = "true";
            }


            await BuildProject(files, project, mnemonic, globalProperties);
        }

        private static Task BuildProject(BazelFileSystem files, string projectName, string mnemonic,
            Dictionary<string, string> globalProperties)
        {
            var templatePath = $"/$src/{projectName}/{projectName}.csproj";
            var projectPath = files.TranslatePath(templatePath);
            var collection = ProjectCollection.GlobalProjectCollection;
            var evaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared);
            var projectOptions = new ProjectOptions()
            {
                ProjectCollection = collection,
                EvaluationContext = evaluationContext,
            };

            var graph = new ProjectGraph(projectPath, globalProperties, collection);


            // var entry = graph.EntryPointNodes.Single();

            // if (mnemonic == "restore")
            // {
            //     return Restore(entry.ProjectInstance);
            // }

            // var references = graph.ProjectNodes.Except(new[] {entry}).SelectMany(n => n.ProjectReferences);
            //
            // // var project = Project.FromFile(projectPath, projectOptions);
            // // Console.WriteLine($"Project {projectName} loaded");
            // var name = "ProjectReference";
            // foreach (var dep in references)
            // {
            //     entry.ProjectInstance.AddItem(name, dep.ProjectInstance.FullPath);
            // }
            // foreach (var item in entry.ProjectInstance.GetItems(name).ToList())
            // {
            //     var path = item.GetMetadataValue("FullPath");
            //     var dep = Project.FromFile(path, projectOptions);
            //     foreach (var tDep in dep.GetItems(name))
            //     {
            //         project.AddItemFast(name, tDep.UnevaluatedInclude);
            //     }
            // }

            Console.WriteLine($"Project {projectName} loaded");
            var outputCacheFile = CacheFile(projectName, mnemonic);

            var parameters = new BuildParameters(ProjectCollection.GlobalProjectCollection)
            {
                EnableNodeReuse = false,
                Loggers = ProjectCollection.GlobalProjectCollection.Loggers.Append(new BinaryLogger()
                    {Parameters = projectPath + ".binlog"}),
                DetailedSummary = true,
                IsolateProjects = true,
                OutputResultsCacheFile = outputCacheFile,
                InputResultsCacheFiles = _cacheFiles?.ToArray(),
                // cult-copy
                ToolsetDefinitionLocations =
                    Microsoft.Build.Evaluation.ToolsetDefinitionLocations.ConfigurationFile |
                    Microsoft.Build.Evaluation.ToolsetDefinitionLocations.Registry,
            };

            string[] targets = null;
            switch (mnemonic)
            {
                case "restore":
                    targets = new[] {"Restore"};
                    break;
                case "build":
                    targets = new[]
                        {"GetTargetFrameworks", "Build", "GetCopyToOutputDirectoryItems", "GetNativeManifest"};
                    break;
                case "publish":
                    targets = new[]
                        {"Publish"};
                    break;
            }

            var data = new GraphBuildRequestData(graph,
                targets,
                null,
                BuildRequestDataFlags.ReplaceExistingProjectInstance);

            // var data = new BuildRequestData(graph.EntryPointNodes.Single().ProjectInstance,
            //     targets, null,
            //     // replace the existing config that we'll load from cache
            //     // not setting this results in MSBuild setting a global unique property to protect against 
            //     // https://github.com/dotnet/msbuild/issues/1748
            //     BuildRequestDataFlags.ReplaceExistingProjectInstance
            // );

            // var directory = Path.GetDirectoryName(data.ProjectFullPath);
            // Directory.SetCurrentDirectory(directory!);
            //
            // var property = typeof(BuildRequestData).GetProperty(nameof(BuildRequestData.ProjectFullPath));
            // // property!.SetValue(data, templatePath);

            var buildManager = BuildManager.DefaultBuildManager;

            buildManager.BeginBuild(parameters);
            if (_cacheFiles != null)
            {
                FixPaths(buildManager, true, "/$src", $"/Users/samh/dev/sandbox/{SandboxId}");
            }

            var submission = buildManager.PendBuildRequest(data);

            var completionSource = new TaskCompletionSource();

            submission.ExecuteAsync((_) =>
            {
                Finish();
                completionSource.SetResult();
            }, new object());
            return completionSource.Task;

            void Finish()
            {
                FixPaths(buildManager, false, Sandbox, "/$src");

                buildManager.EndBuild();

                if (mnemonic == "restore")
                {
                    FixAssetsJson(projectPath);
                }

                var encoding = new ASCIIEncoding();
                using var stream = File.Open(outputCacheFile, FileMode.Open);
                // var writer = new BinaryWriter(stream);
                // writer.Write("/Users/samh/dev/sandbox/4/library/bin/Debug/net5.0/library.dll");
                // stream.Seek(0, SeekOrigin.Begin);

                stream.Seek(0, SeekOrigin.Begin);
                var targetString = Sandbox;
                var replacementString = "/$src";
                var builder = new StringBuilder();
                for (var i = 0; i < targetString.Length - replacementString.Length; i++)
                {
                    builder.Append('^');
                }

                builder.Append(replacementString);
                var replacementBytes = encoding.GetBytes(builder.ToString());
                var targetBytes = encoding.GetBytes(targetString);

                var reader = new BufferedStream(stream);
                var index = 0;
                while (reader.Position < reader.Length)
                {
                    var b = (byte) reader.ReadByte();
                    if (b != targetBytes[index])
                    {
                        index = 0;
                        continue;
                    }

                    index++;
                    if (index < targetBytes.Length) continue;

                    // we found a match
                    // var currentPosition = reader.Position;
                    reader.Seek(-targetBytes.Length, SeekOrigin.Current);
                    // var bytes = new byte[targetBytes.Length];
                    // reader.Read(bytes);
                    // Console.WriteLine(encoding.GetString(bytes));


                    reader.Write(replacementBytes);
                    reader.Flush();
                    index = 0;
                }

                reader.Close();
                // stream.Seek(0, SeekOrigin.Begin);
                //
                // var reader = new BinaryReader(stream);
                // var r = reader.ReadString();
            }
        }

        private static void FixAssetsJson(string projectPath)
        {
            var projectDir = Path.GetDirectoryName(projectPath);

            var regex = new Regex(Regex.Escape(Sandbox) + Path.DirectorySeparatorChar + @"([^\""]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var projectName = Path.GetFileName(projectPath);
            var obj = Path.Combine(projectDir, "obj");
            var filesToKeep = new []{".nuget.g.props", ".nuget.g.targets"}
                .Select(f => projectName + f)
                .Append("project.assets.json")
                .Select(f => Path.Combine(obj, f))
                .ToHashSet();
            
            foreach (var fileName in Directory.EnumerateFiles(obj))
            {
                if (!filesToKeep.Contains(fileName))
                {
                    File.Delete(fileName);
                    continue;
                }
                
                var contents = File.ReadAllText(fileName);
                var replaced = regex.Replace(contents, (match) =>
                {
                    // var thisExtension = Path.GetExtension(match.Value);
                    // if (string.Equals(extension, thisExtension, StringComparison.OrdinalIgnoreCase))
                    // {
                    //     // make sure project references 
                    // }

                    return Path.GetRelativePath(projectDir, match.Value);

                    // var path = match.Groups[1].Value;
                    // var lastSegment = Path.GetFileName(path).ToLower();
                    // switch (lastSegment)
                    // {
                    //     case "packages":
                    //     case "nuget.build.config":
                    //         return Path.GetRelativePath(projectDir, match.Value);
                    // }
                    //
                    // var ext = Path.GetExtension(lastSegment);
                    // if (ext == extension)
                    // {
                    //     var relativeBase = Path.GetRelativePath(projectDir, currentDirectory);
                    //     var relative = Path.Combine(
                    //         relativeBase,
                    //         path);
                    //     return relative;
                    // }
                    //
                    // return "foo";
                });


                File.WriteAllText(fileName, replaced);
            }
        }

        private static async Task Restore(ProjectInstance project)
        {
            // var request = new RestoreRequest(
            //     project.PackageSpec,
            //     sharedCache,
            //     restoreArgs.CacheContext,
            //     clientPolicyContext,
            //     restoreArgs.Log)
            // {
            //     // Set properties from the restore metadata
            //     ProjectStyle = project.PackageSpec.RestoreMetadata.ProjectStyle,
            //     //  Project.json is special cased to put assets file and generated .props and targets in the project folder
            //     RestoreOutputPath = "obj",
            //     DependencyGraphSpec = projectDgSpec,
            //     MSBuildProjectExtensionsPath = projectPackageSpec.RestoreMetadata.OutputPath,
            //     AdditionalMessages = projectAdditionalMessages
            // };
            // var restoreCommand = new RestoreCommand(restoreRequest);
            // await restoreCommand.ExecuteAsync();
        }

        private static void FixPaths(BuildManager buildManager, bool useOverride, string target, string replacement)
        {
            FixConfigResults(buildManager, useOverride, target, replacement);
            FixBuildResults(buildManager, useOverride, target, replacement);
        }

        private static void FixConfigResults(BuildManager buildManager, bool isRead, string target, string replacement)
        {
            var cacheField =
                typeof(BuildManager).GetField("_configCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = cacheField!.GetValue(buildManager);

            object underlyingCache = null;
            if (isRead)
            {
                var overrideField =
                    cache!.GetType().GetField("_override", BindingFlags.NonPublic | BindingFlags.Instance);
                underlyingCache = overrideField?.GetValue(cache) as IEnumerable<object> ?? Array.Empty<object>();
            }
            else
            {
                var field =
                    cache!.GetType().GetProperty("CurrentCache", BindingFlags.Public | BindingFlags.Instance);

                underlyingCache = field == null ? cache : field!.GetValue(cache);
            }


            FieldInfo pathField = null;
            FieldInfo metadataPathField = null;
            var configCount = 0;

            var configIdsByMetadataField = underlyingCache.GetType().GetField("_configurationIdsByMetadata",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var configurationIdsByMetadata = configIdsByMetadataField.GetValue(underlyingCache) as IDictionary;

            var entries = new DictionaryEntry[configurationIdsByMetadata!.Count];
            var i = 0;
            foreach (DictionaryEntry entry in configurationIdsByMetadata)
                entries[i++] = entry;


            // we can't modify the dictionary in the foreach above, so do it after the fact
            foreach (var entry in entries)
            {
                var metadata = entry.Key;
                var configId = entry.Value;
                metadataPathField ??= metadata.GetType()
                    .GetField("_projectFullPath", BindingFlags.NonPublic | BindingFlags.Instance);
                configurationIdsByMetadata.Remove(metadata);
                metadataPathField!.SetValue(metadata,
                    ((string) metadataPathField.GetValue(metadata))!
                    .Replace(target, replacement));
                configurationIdsByMetadata[metadata] = configId;
            }

            foreach (var config in underlyingCache as IEnumerable<object>)
            {
                pathField ??= config.GetType()
                    .GetField("_projectFullPath", BindingFlags.NonPublic | BindingFlags.Instance);
                var path = pathField!.GetValue(config) as string;
                if (path == null) continue;
                if (!path.Contains(target)) continue;
                configCount++;
                Verbose(path);
                pathField.SetValue(config, path.Replace(target, replacement));
            }

            Verbose(configCount.ToString());
        }

        private static void FixBuildResults(BuildManager buildManager, bool useOverride, string target,
            string replacement)
        {
            var cacheField =
                typeof(BuildManager).GetField("_resultsCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = cacheField!.GetValue(buildManager);

            if (useOverride)
            {
                var overrideField =
                    cache.GetType().GetField("_override", BindingFlags.NonPublic | BindingFlags.Instance);
                cache = overrideField?.GetValue(cache);
            }

            if (cache == null) return;

            var targetCount = 0;
            foreach (var buildResult in cache as IEnumerable<BuildResult>)
            {
                foreach (var (targetName, targetResult) in buildResult.ResultsByTarget)
                {
                    Verbose(targetName);
                    foreach (var item in targetResult.Items)
                    {
                        foreach (var name in item.MetadataNames.Cast<string>())
                        {
                            var meta = item.GetMetadata(name);

                            if (meta.Contains(target))
                            {
                                Verbose($"==> {name}: {meta}");
                                targetCount++;
                                item.SetMetadata(name, meta.Replace(target, replacement));
                            }
                        }


                        if (item.ItemSpec.Contains(target))
                        {
                            Verbose(item.ItemSpec);
                            item.ItemSpec = item.ItemSpec.Replace(target, replacement);
                            targetCount++;
                        }
                    }
                }
            }

            Console.WriteLine(targetCount);
        }
    }

    public class BazelLogger : INodeLogger
    {
        public void Initialize(IEventSource eventSource)
        {
            var targetOfInterest = "_GetAllRestoreProjectPathItems";
            var logTasks = false;
            eventSource.TargetStarted += (sender, args) =>
            {
                if (args.TargetName == targetOfInterest)
                {
                    logTasks = true;
                }
            };

            eventSource.TargetFinished += (sender, args) =>
            {
                if (args.TargetName == targetOfInterest)
                {
                    logTasks = false;
                }
            };

            eventSource.TaskStarted += ((sender, args) =>
            {
                if (logTasks)
                {
                }
            });
        }

        public void Shutdown()
        {
        }

        public LoggerVerbosity Verbosity { get; set; }
        public string Parameters { get; set; }

        public void Initialize(IEventSource eventSource, int nodeCount)
        {
            Initialize(eventSource);
        }
    }
}