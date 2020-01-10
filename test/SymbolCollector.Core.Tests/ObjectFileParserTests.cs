using System.Collections.Generic;
using System.Diagnostics;
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
        [MemberData(nameof(GetSymbolsTestCases))]
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
                    AssertObjectFileResult(expectedFiles[i], actualFiles[i]);
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
            Assert.Equal(expected.BuildId, actual.BuildId);
            Assert.Equal(expected.FileFormat, actual.FileFormat);
            Assert.Equal(expected.Architecture, actual.Architecture);
            Assert.Equal(expected.ObjectKind, actual.ObjectKind);
            // Assert.Equal(expected.Hash, actual.Hash);
            // Assert.Equal(expected.Path, actual.Path);
            // Assert.Equal(expected.CodeId, actual.CodeId);
            // Assert.Equal(expected.DebugId, actual.DebugId);
        }

        public static IEnumerable<object[]> GetSymbolsTestCases() =>
            GetObjectFileResultTestCases().Select(symbolsTestCase => new object[] {symbolsTestCase});

        // Relevant data was checked against with: 'sentry-cli difutil check' and the `symsorter` output
        private static IEnumerable<ObjectFileResultTestCase> GetObjectFileResultTestCases()
        {
            yield return new ObjectFileResultTestCase(Path.GetTempFileName()); // A new, 0 byte file
            yield return new ObjectFileResultTestCase(Path.GetRandomFileName()); // file doesn't exist

            yield return new ObjectFileResultTestCase("TestFiles/libchrome.so")
            {
                // x86 Android Emulator, unwind
                // libchrome.so (x86) -> ./output/t/d2/551e58ccebb6895e3c3b3959c12180/executable,
                Expected = new ObjectFileResult(
                    "581e55d2-ebcc-89b6-5e3c-3b3959c12180",
                    null!,
                    null!,
                    "TestFiles/libchrome.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.X86)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libdl.so")
            {
                // unwind
                // libdl.so (x86) -> ./output/t/10/8f1100326466498e655588e72a3e1e/executable
                Expected = new ObjectFileResult(
                    "00118f10-6432-4966-8e65-5588e72a3e1e",
                    null!,
                    null!,
                    "TestFiles/libdl.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.X86)
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Security.Native.so")
            {
                // SymbolCollector.Console Linux x86_64 unwind
                // System.Net.Security.Native.so (x86_64) -> ./output/t/5b/9f149a2a114754ec434b38e0ec7d54c89b716c/executable
                Expected = new ObjectFileResult(
                    "9a149f5b-112a-5447-ec43-4b38e0ec7d54",
                    null!,
                    null!,
                    "TestFiles/System.Net.Security.Native.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Amd64)
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Http.Native.so")
            {
                // unwind
                // System.Net.Http.Native.so (x86_64) -> ./output/t/17/90a472a7ae62e83ac8eb00d97749d37eeee1ff/executable
                Expected = new ObjectFileResult(
                    "72a49017-aea7-e862-3ac8-eb00d97749d3",
                    null!,
                    null!,
                    "TestFiles/System.Net.Http.Native.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Amd64)
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Globalization.Native.so")
            {
                // SymbolCollector.Console Linux arm symtab, unwind
                // System.Globalization.Native.so (arm) -> ./output/t/bb/65c174e6c95a8039261e2c63f12f9addbdb03e/executable
                Expected = new ObjectFileResult(
                    "74c165bb-c9e6-805a-3926-1e2c63f12f9a",
                    null!,
                    null!,
                    "TestFiles/System.Globalization.Native.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libxamarin-app.so")
            {
                // SymbolCollector.Android Android armeabi-v7a symtab
                // libxamarin-app.so (arm) -> ./output/t/f5/9d3adfa8263dd688ad820f74a325b540dcf6b4/executable
                Expected = new ObjectFileResult(
                    "df3a9df5-26a8-d63d-88ad-820f74a325b5",
                    null!,
                    null!,
                    "TestFiles/libxamarin-app.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libxamarin-app-arm64-v8a.so")
            {
                // SymbolCollector.Android Android arm64 symtab
                // libxamarin-app-arm64-v8a.so (arm64) -> ./output/t/76/21750937f30bf8e756cec46b960391f9f57b26/debuginfo
                Expected = new ObjectFileResult(
                    "09752176-f337-f80b-e756-cec46b960391",
                    null!,
                    null!,
                    "TestFiles/libxamarin-app-arm64-v8a.so",
                    null!,
                    BuildIdType.GnuBuildId,
                    ObjectKind.Debug,
                    FileFormat.Elf,
                    Architecture.Arm64)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libqcbassboost.so")
            {
                // Android 4.4.4 device, no debug-id (using hash of .text section) (difutil says 'not usable')
                // libqcbassboost.so (arm) -> ./output/t/63/7aa379d34ed455c314d646b8f3eaec0/executable
                Expected = new ObjectFileResult(
                    "637aa379-d34e-d455-c314-d646b8f3eaec",
                    null!,
                    null!,
                    "TestFiles/libqcbassboost.so",
                    null!,
                    BuildIdType.TextSectionHash,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm)
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Http.Native.dylib")
            {
                // SymbolCollector.Console macOS x86_x64 symtab
                // System.Net.Http.Native.dylib (x86_64) -> ./output/t/c5/ff520ae05c3099921ea8229f808696/executable
                Expected = new ObjectFileResult(
                    "c5ff520a-e05c-3099-921e-a8229f808696",
                    null!,
                    null!,
                    "TestFiles/System.Net.Http.Native.dylib",
                    null!,
                    BuildIdType.Uuid,
                    ObjectKind.Library,
                    FileFormat.MachO,
                    Architecture.Amd64)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libutil.dylib")
            {
                // From macOS Catalina x86_64 /usr/lib/ symtab
                // libutil.dylib (x86_64) -> ./output/t/84/4b788709b33d12acdec4eb8f53dc62/executable
                Expected = new ObjectFileResult(
                    "844b7887-09b3-3d12-acde-c4eb8f53dc62",
                    null!,
                    null!,
                    "TestFiles/libutil.dylib",
                    null!,
                    BuildIdType.Uuid,
                    ObjectKind.Library,
                    FileFormat.MachO,
                    Architecture.Amd64)
            };

            yield return new ObjectFileResultTestCase("TestFiles/libswiftObjectiveC.dylib")
            {
                // // libswiftObjectiveC.dylib Fat Mach-O (symtab, unwind) has 2 debug ids:
                // // > 9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22 (x86)
                // // > b18e9245-d333-326c-af0b-29285a0b3a6d (x86_64)
                Expected = new FatMachOFileResult(
                    string.Empty,
                    null!,
                    null!,
                    "TestFiles/libutil.dylib",
                    null!,
                    new[]
                    {
                        new ObjectFileResult(
                            "9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22",
                            null!,
                            null!,
                            "temp-path/libswiftObjectiveC.dylib",
                            null!,
                            BuildIdType.Uuid,
                            ObjectKind.Library,
                            FileFormat.MachO,
                            Architecture.X86),
                        new ObjectFileResult(
                            "b18e9245-d333-326c-af0b-29285a0b3a6d",
                            null!,
                            null!,
                            "temp-path/libswiftObjectiveC.dylib",
                            null!,
                            BuildIdType.Uuid,
                            ObjectKind.Library,
                            FileFormat.MachO,
                            Architecture.Amd64)
                    }
                )
            };
            // Java class file which has the same magic bytes as a Fat Mach-O file
            yield return new ObjectFileResultTestCase("TestFiles/DiskBuffer$1.class") {Expected = null!,};
        }

        [DebuggerDisplay("{Path} - {Expected}")]
        public class ObjectFileResultTestCase
        {
            public ObjectFileResult? Expected { get; set; }
            public string Path { get;}

            public ObjectFileResultTestCase(string path)
            {
                Path = path;
            }
        }
    }
}
