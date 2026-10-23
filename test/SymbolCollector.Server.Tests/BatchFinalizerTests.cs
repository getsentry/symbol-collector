using Xunit;

namespace SymbolCollector.Server.Tests;

public class BatchFinalizerTests
{
    [Theory]
    [InlineData("Sorted 0 debug files", 0)]
    [InlineData("Sorted 42 debug files", 42)]
    [InlineData("Done.", null)]
    public void ParseSortedFilesCount_OutputLine_ReturnsExpectedCount(string outputLine, int? expected)
    {
        var actual = SymsorterBatchFinalizer.ParseSortedFilesCount(outputLine);

        Assert.Equal(expected, actual);
    }
}
