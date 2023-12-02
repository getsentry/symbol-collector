using Xunit;

namespace SymbolCollector.Core.Tests;

public class BundleIdGeneratorTests
{
    [Fact]
    public void CreateBundleId_SameFriendlyName_DifferentBundleIdsWithoutSpacesAndSlashes()
    {
        var suffixGenerator = new SuffixGenerator();
        var target = new BundleIdGenerator(suffixGenerator);

        var friendlyName = @".this is the : _ // """"
? * a friendly name";
        var actual = target.CreateBundleId(friendlyName);
        Assert.NotEqual(target.CreateBundleId(friendlyName), actual);
        Assert.StartsWith("this_is_the_a_friendly_name_", actual);
    }
}