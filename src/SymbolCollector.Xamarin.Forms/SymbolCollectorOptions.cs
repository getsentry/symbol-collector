using System;
using System.Collections.Generic;

namespace SymbolCollector.Xamarin.Forms
{
    public class SymbolCollectorOptions
    {
        public Uri? ServerEndpoint { get; set; }
        public string? ClientName { get; set; } = "SymbolCollector/?.?.?";
        public int ParallelTasks { get; set; } = 10;
        public HashSet<string> BlackListedPaths { get; set; }= new HashSet<string>();
    }
}
