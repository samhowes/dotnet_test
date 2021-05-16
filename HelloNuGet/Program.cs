using System;
using Newtonsoft.Json;

namespace HelloNuGet
{
    class Program
    {
        static void Main(string[] args)
        {
            JsonConvert.DeserializeObject("{}");
            Console.WriteLine("Hello World!");
        }
    }
}
