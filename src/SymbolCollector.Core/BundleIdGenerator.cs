using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SymbolCollector.Core
{
    public class BundleIdGenerator
    {
        private readonly SuffixGenerator _suffixGenerator;
        private readonly Regex _removeCharsRegex;

        public BundleIdGenerator(SuffixGenerator suffixGenerator)
        {
            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] {' ', '*', '?', '"', '\n'});
            var pattern = Regex.Escape(new string(invalidChars.ToArray()));
            _removeCharsRegex = new Regex($"[{pattern}]", RegexOptions.Compiled);
            _suffixGenerator = suffixGenerator;
        }

        public string CreateBundleId(string friendlyName) =>
            _removeCharsRegex
                .Replace(friendlyName, "_")
                .Trim('.') + _suffixGenerator.Generate();
    }
}
