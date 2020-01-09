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
        // x86 Android Emulator, unwind
        // libchrome.so (x86) -> ./output/t/d2/551e58ccebb6895e3c3b3959c12180/executable
        [InlineData("581e55d2-ebcc-89b6-5e3c-3b3959c12180", FileFormat.Elf, Architecture.X86, ObjectKind.Library, "TestFiles/libchrome.so")]
        // unwind
        // libdl.so (x86) -> ./output/t/10/8f1100326466498e655588e72a3e1e/executable
        [InlineData("00118f10-6432-4966-8e65-5588e72a3e1e", FileFormat.Elf, Architecture.X86, ObjectKind.Library, "TestFiles/libdl.so")]
        // SymbolCollector.Console Linux x86_64 unwind
        // System.Net.Security.Native.so (x86_64) -> ./output/t/5b/9f149a2a114754ec434b38e0ec7d54c89b716c/executable
        [InlineData("9a149f5b-112a-5447-ec43-4b38e0ec7d54", FileFormat.Elf, Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Security.Native.so")]
        // unwind
        // System.Net.Http.Native.so (x86_64) -> ./output/t/17/90a472a7ae62e83ac8eb00d97749d37eeee1ff/executable
        [InlineData("72a49017-aea7-e862-3ac8-eb00d97749d3", FileFormat.Elf, Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Http.Native.so")]
        // SymbolCollector.Console Linux arm symtab, unwind
        // System.Globalization.Native.so (arm) -> ./output/t/bb/65c174e6c95a8039261e2c63f12f9addbdb03e/executable
        [InlineData("74c165bb-c9e6-805a-3926-1e2c63f12f9a", FileFormat.Elf, Architecture.Arm, ObjectKind.Library, "TestFiles/System.Globalization.Native.so")]
        // SymbolCollector.Android Android armeabi-v7a symtab
        // libxamarin-app.so (arm) -> ./output/t/f5/9d3adfa8263dd688ad820f74a325b540dcf6b4/executable
        [InlineData("df3a9df5-26a8-d63d-88ad-820f74a325b5", FileFormat.Elf, Architecture.Arm, ObjectKind.Library, "TestFiles/libxamarin-app.so")]
        // SymbolCollector.Android Android arm64 symtab
        // libxamarin-app-arm64-v8a.so (arm64) -> ./output/t/76/21750937f30bf8e756cec46b960391f9f57b26/debuginfo
        [InlineData("09752176-f337-f80b-e756-cec46b960391", FileFormat.Elf, Architecture.Arm64, ObjectKind.Debug, "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Android 4.4.4 device, no debug-id (using hash of .text section) (difutil says 'not usable')
        // libqcbassboost.so (arm) -> ./output/t/63/7aa379d34ed455c314d646b8f3eaec0/executable
        [InlineData("637aa379-d34e-d455-c314-d646b8f3eaec", FileFormat.Elf, Architecture.Arm, ObjectKind.Library, "TestFiles/libqcbassboost.so")]
        // SymbolCollector.Console macOS x86_x64 symtab
        // System.Net.Http.Native.dylib (x86_64) -> ./output/t/c5/ff520ae05c3099921ea8229f808696/executable
        [InlineData("c5ff520a-e05c-3099-921e-a8229f808696", FileFormat.MachO, Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Http.Native.dylib")]
        // From macOS Catalina x86_64 /usr/lib/ symtab
        // libutil.dylib (x86_64) -> ./output/t/84/4b788709b33d12acdec4eb8f53dc62/executable
        [InlineData("844b7887-09b3-3d12-acde-c4eb8f53dc62", FileFormat.MachO, Architecture.Amd64, ObjectKind.Library, "TestFiles/libutil.dylib")]
        public void TryParse_TestFile_CorrectId(string debugId, FileFormat fileFormat, Architecture arch, ObjectKind objectKind, string path)
        {
            var sut = _fixture.GetSut();
            Assert.True(sut.TryParse(path, out var actual));
            Assert.NotNull(actual);
            Assert.Equal(new Guid(debugId).ToString(), actual!.BuildId);
            Assert.Equal(fileFormat, actual.FileFormat);
            Assert.Equal(arch, actual.Architecture);
            Assert.Equal(fileFormat, actual.FileFormat);
            Assert.Equal(objectKind, actual.ObjectKind);
        }

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // x86 Android Emulator, unwind
        // libchrome.so (x86) -> ./output/t/d2/551e58ccebb6895e3c3b3959c12180/executable
        [InlineData("581e55d2-ebcc-89b6-5e3c-3b3959c12180", Architecture.X86, ObjectKind.Library, "TestFiles/libchrome.so")]
        // unwind
        // libdl.so (x86) -> ./output/t/10/8f1100326466498e655588e72a3e1e/executable
        [InlineData("00118f10-6432-4966-8e65-5588e72a3e1e", Architecture.X86, ObjectKind.Library, "TestFiles/libdl.so")]
        // SymbolCollector.Console Linux x86_64 unwind
        // System.Net.Security.Native.so (x86_64) -> ./output/t/5b/9f149a2a114754ec434b38e0ec7d54c89b716c/executable
        [InlineData("9a149f5b-112a-5447-ec43-4b38e0ec7d54", Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Security.Native.so")]
        // unwind
        // System.Net.Http.Native.so (x86_64) -> ./output/t/17/90a472a7ae62e83ac8eb00d97749d37eeee1ff/executable
        [InlineData("72a49017-aea7-e862-3ac8-eb00d97749d3", Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Http.Native.so")]
        // SymbolCollector.Console Linux arm symtab, unwind
        // System.Globalization.Native.so (arm) -> ./output/t/bb/65c174e6c95a8039261e2c63f12f9addbdb03e/executable
        [InlineData("74c165bb-c9e6-805a-3926-1e2c63f12f9a", Architecture.Arm, ObjectKind.Library, "TestFiles/System.Globalization.Native.so")]
        // SymbolCollector.Android Android armeabi-v7a symtab
        // libxamarin-app.so (arm) -> ./output/t/f5/9d3adfa8263dd688ad820f74a325b540dcf6b4/executable
        [InlineData("df3a9df5-26a8-d63d-88ad-820f74a325b5", Architecture.Arm, ObjectKind.Library, "TestFiles/libxamarin-app.so")]
        // SymbolCollector.Android Android arm64 symtab
        // libxamarin-app-arm64-v8a.so (arm64) -> ./output/t/76/21750937f30bf8e756cec46b960391f9f57b26/debuginfo
        [InlineData("09752176-f337-f80b-e756-cec46b960391", Architecture.Arm64, ObjectKind.Debug, "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Android 4.4.4 device, no debug-id (using hash of .text section) (difutil says 'not usable')
        // libqcbassboost.so (arm) -> ./output/t/63/7aa379d34ed455c314d646b8f3eaec0/executable
        [InlineData("637aa379-d34e-d455-c314-d646b8f3eaec", Architecture.Arm, ObjectKind.Library, "TestFiles/libqcbassboost.so")]
        // Not an ELF file (came from an Android x86 emulator)
        [InlineData(null, Architecture.Unknown, ObjectKind.None, "TestFiles/libdl.bc")]
        // macOS file
        [InlineData(null, Architecture.Unknown, ObjectKind.None, "TestFiles/System.Net.Http.Native.dylib")]
        public void TryParseElfFile_TestFile_CorrectId(string debugId, Architecture arch, ObjectKind objectKind, string path)
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
                Assert.Equal(FileFormat.Elf, actual.FileFormat);
                Assert.Equal(arch, actual.Architecture);
                Assert.Equal(objectKind, actual.ObjectKind);

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
        // SymbolCollector.Console macOS x86_x64 symtab
        // System.Net.Http.Native.dylib (x86_64) -> ./output/t/c5/ff520ae05c3099921ea8229f808696/executable
        [InlineData("c5ff520a-e05c-3099-921e-a8229f808696", Architecture.Amd64, ObjectKind.Library, "TestFiles/System.Net.Http.Native.dylib")]
        // From macOS Catalina x86_64 /usr/lib/ symtab
        // libutil.dylib (x86_64) -> ./output/t/84/4b788709b33d12acdec4eb8f53dc62/executable
        [InlineData("844b7887-09b3-3d12-acde-c4eb8f53dc62", Architecture.Amd64, ObjectKind.Library, "TestFiles/libutil.dylib")]
        // ELF file:
        [InlineData(null, Architecture.Unknown, ObjectKind.None, "TestFiles/libxamarin-app-arm64-v8a.so")]
        public void TryParseMachOFile_TestFile_CorrectId(string debugId, Architecture arch, ObjectKind objectKind, string path)
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
                Assert.Equal(FileFormat.MachO, actual.FileFormat);
                Assert.Equal(BuildIdType.Uuid, actual.BuildIdType);
                Assert.Equal(arch, actual.Architecture);
                Assert.Equal(objectKind, actual.ObjectKind);
            }
        }

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // libswiftObjectiveC.dylib (x86) -> ./output/t/97/87d26c6cd630a9b5a1e9c7c42ddb22/executable
        [InlineData("9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22", Architecture.X86, ObjectKind.Library, "TestFiles/libswiftObjectiveC.dylib")]
        // libswiftObjectiveC.dylib (x86_64) -> ./output/t/b1/8e9245d333326caf0b29285a0b3a6d/executable
        [InlineData("b18e9245-d333-326c-af0b-29285a0b3a6d", Architecture.Amd64, ObjectKind.Library, "TestFiles/libswiftObjectiveC.dylib")]
        // libswiftObjectiveC.dylib Fat Mach-O (symtab, unwind) has 2 debug ids:
        // > 9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22 (x86)
        // > b18e9245-d333-326c-af0b-29285a0b3a6d (x86_64)
        // ELF file:
        [InlineData(null, Architecture.Unknown, ObjectKind.None, "TestFiles/libxamarin-app-arm64-v8a.so")]
        // Java class file which has the same magic bytes as a Fat Mach-O file
        [InlineData(null, Architecture.Unknown, ObjectKind.None, "TestFiles/DiskBuffer$1.class")]
        public void TryParseFatMachO_TestFile_CorrectId(string debugId, Architecture arch, ObjectKind objectKind, string path)
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
                Assert.Equal(FileFormat.FatMachO, actual.FileFormat);
                Assert.Equal(BuildIdType.None, actual.BuildIdType);
                Assert.Equal(2, actual.InnerFiles.Count());
                Assert.Equal(ObjectKind.None, actual.ObjectKind);
                foreach (var objectFileResult in actual.InnerFiles)
                {
                    if (debugId == actual!.BuildId)
                    {
                        Assert.Equal(FileFormat.MachO, objectFileResult.FileFormat);
                        Assert.Equal(BuildIdType.Uuid, objectFileResult.BuildIdType);
                        Assert.Equal(arch, objectFileResult.Architecture);
                        Assert.Equal(objectKind, objectFileResult.ObjectKind);
                    }
                }
            }
        }
    }
}
