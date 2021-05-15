using System;
using Xunit;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Logging;

// using Newtonsoft.Json;
    
namespace PackageDeps
{
    public class Program
    {
        static void Main()
        {
            var options = new ProjectOptions()
            {

            };
            var project = Project.FromFile("../../../test.targets", options);

            Log(project.AllEvaluatedProperties.Where(p => p.Xml != null),
                p => $"{p.Name}: {p.Xml.Value}; {p.Xml.Condition}");
            
            Log(project.AllEvaluatedItems, i => $"{i.ItemType}: {i.Xml.Metadata}");
            Log(project.Targets.Values, t => $"{t.Name}");
            foreach (var (k, target) in project.Targets)
            {
                Console.WriteLine($"Target key: {k}");
                Log(target.Tasks, t => t.Name);
                // project.Build(target.Name, new []{new ConsoleLogger()});
                Console.WriteLine(target.Inputs);
                Console.WriteLine(target.Outputs);
                Log(target.Children, c =>
                {
                    if (c is ProjectItemGroupTaskInstance itemGroup)
                    {
                        Log(itemGroup.Items, i => $"{i.ItemType} Include={i.Include}");
                    }
                    return $"{c}";
                });

            }
            Log(project.Items, i => $"{i.ItemType}");
            Log(project.ItemTypes, i => i);
        }

        private static void Log<T>(IEnumerable<T> items, Func<T, string> getText)
        {
            Indent++;
            var prefix = "============================"[0..(Indent * 2)] + "> ";
            Console.WriteLine($"{prefix} {typeof(T).Name} ========");
            foreach (var item in items)
            {
                Console.WriteLine(prefix + getText(item));
            }
            Console.WriteLine(prefix + "-----------------");
            Indent--;
        }

        public static int Indent { get; set; }
    }
    
    public abstract class TransitiveTestBase
    {

        // public void Foo()
        // {
        //     var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>("");
        // }
    }
}