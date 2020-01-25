using System;
using System.IO;
using System.Linq;

namespace SymbolCollector.Core
{
    public class BundleIdGenerator
    {
        private readonly SuffixGenerator _suffixGenerator;

        public BundleIdGenerator(SuffixGenerator suffixGenerator) => _suffixGenerator = suffixGenerator;

        public string CreateBundleId(string friendlyName)
        {
            var invalids = Path.GetInvalidFileNameChars().Concat(" ").ToArray();
            return string.Join("_",
                    friendlyName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)
                        .Append(_suffixGenerator.Generate()))
                .TrimEnd('.');
        }
    }
}
