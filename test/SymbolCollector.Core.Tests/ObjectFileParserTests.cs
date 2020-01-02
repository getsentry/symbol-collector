using System;
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

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // x86 Android Emulator
        [InlineData("581e55d2-ebcc-89b6-5e3c-3b3959c12180", "TestFiles/libchrome.so")]
        [InlineData("00118f10-6432-4966-8e65-5588e72a3e1e", "TestFiles/libdl.so")]
        // SymbolCollector.Console Linux x86_64 unwind
        [InlineData("9a149f5b-112a-5447-ec43-4b38e0ec7d54", "TestFiles/System.Net.Security.Native.so")]
        [InlineData("72a49017-aea7-e862-3ac8-eb00d97749d3", "TestFiles/System.Net.Http.Native.so")]
        // SymbolCollector.Console Linux arm symtab, unwind
        [InlineData("74c165bb-c9e6-805a-3926-1e2c63f12f9a", "TestFiles/System.Globalization.Native.so")]
        // SymbolCollector.Android Android armeabi-v7a symtab
        [InlineData("df3a9df5-26a8-d63d-88ad-820f74a325b5", "TestFiles/libxamarin-app.so")]
        // SymbolCollector.Android Android arm64 symtab
        [InlineData("09752176-f337-f80b-e756-cec46b960391", "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Android 4.4.4 device, no debug-id (using hash of .text section) "difutil says not usable)
        [InlineData("637aa379-d34e-d455-c314-d646b8f3eaec", "TestFiles/libqcbassboost.so")]
        // SymbolCollector.Console macOS x86_x64 symtab, unwind
        [InlineData("c5ff520a-e05c-3099-921e-a8229f808696", "TestFiles/System.Net.Http.Native.dylib")]
        // From macOS Catalina x86_64 /usr/lib/ symtab
        [InlineData("844b7887-09b3-3d12-acde-c4eb8f53dc62", "TestFiles/libutil.dylib")]

        public void TryParse_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            Assert.True(sut.TryParse(path, out var actual));
            Assert.NotNull(actual);
            Assert.Equal(new Guid(debugId).ToString(), actual!.BuildId);
        }

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // x86 Android Emulator
        [InlineData("581e55d2-ebcc-89b6-5e3c-3b3959c12180", "TestFiles/libchrome.so")]
        [InlineData("00118f10-6432-4966-8e65-5588e72a3e1e", "TestFiles/libdl.so")]
        // SymbolCollector.Console Linux x86_64 unwind
        [InlineData("9a149f5b-112a-5447-ec43-4b38e0ec7d54", "TestFiles/System.Net.Security.Native.so")]
        [InlineData("72a49017-aea7-e862-3ac8-eb00d97749d3", "TestFiles/System.Net.Http.Native.so")]
        // SymbolCollector.Console Linux arm symtab, unwind
        [InlineData("74c165bb-c9e6-805a-3926-1e2c63f12f9a", "TestFiles/System.Globalization.Native.so")]
        // SymbolCollector.Android Android armeabi-v7a symtab
        [InlineData("df3a9df5-26a8-d63d-88ad-820f74a325b5", "TestFiles/libxamarin-app.so")]
        // SymbolCollector.Android Android arm64 symtab
        [InlineData("09752176-f337-f80b-e756-cec46b960391", "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Android 4.4.4 device, no debug-id (using hash of .text section) "difutil says not usable)
        [InlineData("637aa379-d34e-d455-c314-d646b8f3eaec", "TestFiles/libqcbassboost.so")]
        // Not an ELF file (came from an Android x86 emulator)
        [InlineData(null, "TestFiles/libdl.bc")]
        // macOS file
        [InlineData(null, "TestFiles/System.Net.Http.Native.dylib")]
        public void TryParseElfFile_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var parsed = sut.TryParseElfFile(path, out var actual);
            if (debugId is null)
            {
                Assert.False(parsed);
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                Assert.Equal(new Guid(debugId).ToString(), actual!.BuildId);
                if (path.EndsWith("libqcbassboost.so"))
                {
                    Assert.Equal(BuildIdType.TextSectionHash, actual.BuildIdType);
                }
                else
                {
                    Assert.Equal(BuildIdType.GnuBuildId, actual.BuildIdType);
                }
            }
        }

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // SymbolCollector.Console macOS x86_x64 symtab, unwind
        [InlineData("c5ff520a-e05c-3099-921e-a8229f808696", "TestFiles/System.Net.Http.Native.dylib")]
        // From macOS Catalina x86_64 /usr/lib/ symtab
        [InlineData("844b7887-09b3-3d12-acde-c4eb8f53dc62", "TestFiles/libutil.dylib")]
        // ELF file:
        [InlineData(null, "TestFiles/libxamarin-app-arm64-v8a.so")]
        public void TryParseMachOFile_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var parsed = sut.TryParseMachOFile(path, out var actual);
            if (debugId is null)
            {
                Assert.False(parsed);
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                Assert.Equal(new Guid(debugId).ToString(), actual!.BuildId);
                Assert.Equal(BuildIdType.Uuid, actual.BuildIdType);
            }
        }


        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        [InlineData("9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22", "TestFiles/libswiftObjectiveC.dylib")]
        [InlineData("b18e9245-d333-326c-af0b-29285a0b3a6d", "TestFiles/libswiftObjectiveC.dylib")]
        // libswiftObjectiveC.dylib Fat Mach-O (symtab, unwind) has 2 debug ids:
        // > 9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22 (x86)
        // > b18e9245-d333-326c-af0b-29285a0b3a6d (x86_64)
        // ELF file:
        [InlineData(null, "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Java class file which has the same magic bytes as a Fat Mach-O file
        [InlineData(null, "TestFiles/DiskBuffer$1.class")]
        public void TryParseFatMachO_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var parsed = sut.TryParseFatMachO(path, out var actual);


            if (debugId is null)
            {
                Assert.False(parsed);
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                Assert.NotNull(actual!.BuildId);
                Assert.Equal(BuildIdType.None, actual.BuildIdType);
                Assert.Equal(2,actual.InnerFiles.Count());
                foreach (var objectFileResult in actual.InnerFiles)
                {
                    Assert.Equal(BuildIdType.Uuid, objectFileResult.BuildIdType);
                }
            }
        }
    }
}
