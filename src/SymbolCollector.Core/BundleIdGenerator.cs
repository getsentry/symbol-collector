using System.Text.RegularExpressions;

namespace SymbolCollector.Core
{
    public class BundleIdGenerator
    {
        private readonly SuffixGenerator _suffixGenerator;
        private readonly Regex _removeCharsRegex;

        public BundleIdGenerator(SuffixGenerator suffixGenerator)
        {
            // https://github.com/getsentry/symbolicator/blob/e21a5b103a1fcedd61c48f8120f661282ee67bc7/symsorter/src/utils.rs#L12
            _removeCharsRegex = new Regex("[^a-zA-Z0-9.,-]+", RegexOptions.Compiled);
            _suffixGenerator = suffixGenerator;
        }

        public string CreateBundleId(string friendlyName) =>
            $"{_removeCharsRegex.Replace(friendlyName, "_").Trim('.', '_')}_{_suffixGenerator.Generate()}";
    }
}
