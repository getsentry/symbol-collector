using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class ObjectFileParserTests
    {
        private class Fixture
        {
            public FatBinaryReader? FatBinaryReader { get; set; } =
                new FatBinaryReader(Substitute.For<ILogger<FatBinaryReader>>());

            public ClientMetrics? Metrics { get; set; }
            public ILogger<ObjectFileParser>? Logger { get; set; }

            public ObjectFileParser GetSut() =>
                new ObjectFileParser(
                    FatBinaryReader,
                    Metrics,
                    Logger);
        }

        private readonly Fixture _fixture = new Fixture();

        [Theory]
        [ClassData(typeof(ObjectFileResultTestCases))]
        public void TryParse_TestFile_CorrectId(ObjectFileResultTestCase testCase)
        {
            var sut = _fixture.GetSut();
            if (testCase.Expected is null)
            {
                Assert.False(sut.TryParse(testCase.Path, out var actualNull));
                Assert.Null(actualNull);
                return;
            }

            Assert.True(sut.TryParse(testCase.Path, out var actual));
            Assert.NotNull(actual);

            if (testCase.Expected is FatMachOFileResult expectedFatMachO)
            {
                var actualFatMatchO = Assert.IsType<FatMachOFileResult>(actual);
                AssertObjectFileResult(expectedFatMachO, actualFatMatchO);

                var expectedFiles = expectedFatMachO.InnerFiles.ToArray();
                var actualFiles = actualFatMatchO.InnerFiles.ToArray();
                Assert.Equal(expectedFiles.Length, actualFiles.Length);
                for (var i = 0; i < expectedFiles.Length; i++)
                {
                    // Fat Mach-O files path are defined in temp folders. Only the actual file name is expected to match.
                    var expectedFile = expectedFiles[i];
                    var actualFile = actualFiles[i];
                    Assert.Equal(Path.GetFileName(expectedFile.Path),Path.GetFileName(actualFile.Path));
                    expectedFile.Path = actualFile.Path;
                    AssertObjectFileResult(expectedFile, actualFile);
                }
            }
            else
            {
                AssertObjectFileResult(testCase.Expected, actual!);
            }
        }

        private static void AssertObjectFileResult(ObjectFileResult expected, ObjectFileResult actual)
        {
            Assert.Equal(expected.BuildIdType, actual.BuildIdType);
            Assert.Equal(expected.FileFormat, actual.FileFormat);
            Assert.Equal(expected.Architecture, actual.Architecture);
            Assert.Equal(expected.ObjectKind, actual.ObjectKind);
            Assert.Equal(expected.Hash, actual.Hash);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.CodeId, actual.CodeId);
            Assert.Equal(expected.DebugId, actual.DebugId);
            Assert.Equal(expected.UnifiedId, actual.UnifiedId);
        }
    }
}
