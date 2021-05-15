using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
            string mnemonic = null;
            string project = null;
            switch (SandboxId)
            {
                case 1:
                    mnemonic = "restore";
                    project = "library";
                    _cacheFiles = null;
                    break;
                case 2:
                    mnemonic = "build";
                    project = "library";
                    _cacheFiles = new List<string>() {CacheFile("library", "restore")};
                    break;
                case 3:
                    mnemonic = "restore";
                    project = "binary";
                    _cacheFiles = new List<string>() {CacheFile("library", "restore")};
                    break;
                case 4:
                    mnemonic = "build";
                    project = "binary";
                    _cacheFiles = new List<string>()
                    {
                        CacheFile("library", "build"),
                        CacheFile("binary", "restore")
                    };
                    break;
            }
            
            var files = new BazelFileSystem();
            var logger = new ConsoleLogger(LoggerVerbosity.Normal);
            // logger.Parameters = "SHOWPROJECTFILE=TRUE;";

            var pc = ProjectCollection.GlobalProjectCollection;
            pc.RegisterLogger(logger);
            pc.RegisterLogger(new BazelLogger());
            pc.SetGlobalProperty("ImportDirectoryBuildProps", "false");
            if (mnemonic == "restore")
            {
                // this one is auto-set by NuGet.targets in Restore when restoring a referenced project. If we don't set it
                // ahead of time, there will be a cache miss on the restored project.
                pc.SetGlobalProperty("ExcludeRestorePackageImports", "true");
            }
                
            
            await BuildProject(files, project, mnemonic);
        }

        private static Task BuildProject(BazelFileSystem files, string projectName, string mnemonic)
        {
            var templatePath = $"/$src/{projectName}/{projectName}.csproj";
            var projectPath = files.TranslatePath(templatePath);
            var collection = ProjectCollection.GlobalProjectCollection;
            var evaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared);
            var project = Project.FromFile(projectPath, new ProjectOptions()
            {
                ProjectCollection = collection,
                EvaluationContext = evaluationContext,
            });
            
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
                    targets = new[] {"_GenerateRestoreProjectPathWalk", "Restore"};
                    break;
                case "build":
                    targets = new[]
                        {"GetTargetFrameworks", "Build", "GetCopyToOutputDirectoryItems", "GetNativeManifest"};
                    break;
            }

            var data = new BuildRequestData(project.CreateProjectInstance(), targets, null,
                // replace the existing config that we'll load from cache
                // not setting this results in MSBuild setting a global unique property to protect against 
                // https://github.com/dotnet/msbuild/issues/1748
                BuildRequestDataFlags.ReplaceExistingProjectInstance
                );
            
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

        private static void FixPaths(BuildManager buildManager, bool useOverride, string target, string replacement)
        {
            FixConfigResults(buildManager, useOverride, target, replacement);
            FixBuildResults(buildManager, useOverride, target, replacement);
        }
        
        private static void FixConfigResults(BuildManager buildManager, bool isRead, string target, string replacement)
        {
            var cacheField = typeof(BuildManager).GetField("_configCache", BindingFlags.NonPublic | BindingFlags.Instance);
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
                metadataPathField ??= metadata.GetType().GetField("_projectFullPath", BindingFlags.NonPublic | BindingFlags.Instance);
                configurationIdsByMetadata.Remove(metadata);
                metadataPathField!.SetValue(metadata,
                    ((string)metadataPathField.GetValue(metadata))!
                    .Replace(target, replacement));
                configurationIdsByMetadata[metadata] = configId;
            }
            
            foreach (var config in underlyingCache as IEnumerable<object>)
            {
                pathField ??= config.GetType().GetField("_projectFullPath", BindingFlags.NonPublic | BindingFlags.Instance);
                var path = pathField!.GetValue(config) as string;
                if (path == null) continue;
                if (!path.Contains(target)) continue;
                configCount++;
                Console.WriteLine(path);
                pathField.SetValue(config, path.Replace(target, replacement));   
            }
            Console.WriteLine(configCount);
        }

        private static void FixBuildResults(BuildManager buildManager, bool useOverride, string target, string replacement)
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
                    Console.WriteLine(targetName);
                    foreach (var item in targetResult.Items)
                    {
                        foreach (var name in item.MetadataNames.Cast<string>())
                        {
                            var meta = item.GetMetadata(name);

                            if (meta.Contains(target))
                            {
                                Console.WriteLine($"==> {name}: {meta}");
                                targetCount++;
                                item.SetMetadata(name, meta.Replace(target, replacement));
                            }
                        }


                        if (item.ItemSpec.Contains(target))
                        {
                            Console.WriteLine(item.ItemSpec);
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
            eventSource.TargetStarted += (sender, args) =>
            {
                if (args.TargetName == "ResolveAssemblyReferences")
                {
                }
            };
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