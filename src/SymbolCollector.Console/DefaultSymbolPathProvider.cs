using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SymbolCollector.Console
{
    internal static class DefaultSymbolPathProvider
    {
        public static IEnumerable<string> GetDefaultPaths()
        {
            // TODO: Get the paths via parameter or config file/env var?
            var paths = new List<string> {"/usr/lib/", "/usr/local/lib/"};
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: Add per OS paths
                paths.Add("/System/Library/Frameworks/");
            }
            else
            {
                paths.Add("/lib/");
            }

            return paths;
        }
    }
}
