using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class ObjectFileParserTests
    {
        private class Fixture
        {
            public ObjectFileParserOptions ObjectFileParserOptions { get; set; } = new ObjectFileParserOptions();
            public FatBinaryReader? FatBinaryReader { get; set; } =
                new FatBinaryReader(Substitute.For<ILogger<FatBinaryReader>>());

            public ClientMetrics Metrics { get; set; } = new ClientMetrics();
            public ILogger<ObjectFileParser> Logger { get; set; } = Substitute.For<ILogger<ObjectFileParser>>();

            public ObjectFileParser GetSut() =>
                new ObjectFileParser(
                    Metrics,
                    Options.Create(ObjectFileParserOptions),
                    Logger,
                    FatBinaryReader);
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
                    Assert.Equal(Path.GetFileName(expectedFile.Path), Path.GetFileName(actualFile.Path));
                    expectedFile.Path = actualFile.Path;
                    AssertObjectFileResult(expectedFile, actualFile);
                }
            }
            else
            {
                AssertObjectFileResult(testCase.Expected, actual!);
                if (testCase.ExpectedSymsorterFileName is {} name)
                {
                    Assert.Equal(name, actual!.ObjectKind.ToSymsorterFileName());
                }
            }
        }

        [Fact]
        public void GetFallbackDebugId_NullArg_ReturnsNull() => Assert.Null(_fixture.GetSut().GetFallbackDebugId(null!));

        [Fact]
        public void TryParse_NoFallback_DoesntParse()
        {
            var files = ObjectFileResultTestCases.GetObjectFileResultTestCases().ToList();
            _fixture.ObjectFileParserOptions.UseFallbackObjectFileParser = false;
            var sut = _fixture.GetSut();
            var elf = files.First(f => f.Expected?.FileFormat == FileFormat.Elf);
            var machO = files.First(f => f.Expected?.FileFormat == FileFormat.MachO);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.False(sut.TryParse(elf.Path, out _));
                Assert.True(sut.TryParse(machO.Path, out _));
            }
            else
            {
                Assert.True(sut.TryParse(elf.Path, out _));
                Assert.False(sut.TryParse(machO.Path, out _));
            }
        }

        [Fact]
        public void GetFallbackDebugId_EmptyArg_ReturnsNull() => Assert.Null(_fixture.GetSut().GetFallbackDebugId(new byte[0]));

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
