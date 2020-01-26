using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SymbolCollector.Core.Tests
{
    public class ObjectFileResultTestCases : IEnumerable<object[]>
    {
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
                    "d2551e58ccebb6895e3c3b3959c12180",
                    "TestFiles/libchrome.so",
                    "8d9c389bd724bd50fbeaf41775d9f4898f0e36e1e9202f692f1997b38cd901e9",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.X86),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libdl.so")
            {
                // unwind
                // libdl.so (x86) -> ./output/t/10/8f1100326466498e655588e72a3e1e/executable
                Expected = new ObjectFileResult(
                    "00118f10-6432-4966-8e65-5588e72a3e1e",
                    "108f1100326466498e655588e72a3e1e",
                    "TestFiles/libdl.so",
                    "4b3951069fa18a37de6f6e563ae725d89eeb07ec535ab74e2fdd7d68e0f5c7cf",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.X86),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Security.Native.so")
            {
                // SymbolCollector.Console Linux x86_64 unwind
                // System.Net.Security.Native.so (x86_64) -> ./output/t/5b/9f149a2a114754ec434b38e0ec7d54c89b716c/executable
                Expected = new ObjectFileResult(
                    "9a149f5b-112a-5447-ec43-4b38e0ec7d54",
                    "5b9f149a2a114754ec434b38e0ec7d54c89b716c",
                    "TestFiles/System.Net.Security.Native.so",
                    "cd44e201e13b61820a7aa5f6c1ed05c7c9a84bc0e25555c74d1b7204a4166ec6",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Amd64),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Http.Native.so")
            {
                // unwind
                // System.Net.Http.Native.so (x86_64) -> ./output/t/17/90a472a7ae62e83ac8eb00d97749d37eeee1ff/executable
                Expected = new ObjectFileResult(
                    "72a49017-aea7-e862-3ac8-eb00d97749d3",
                    "1790a472a7ae62e83ac8eb00d97749d37eeee1ff",
                    "TestFiles/System.Net.Http.Native.so",
                    "c28ea2ee61d5b7c1b6338b50fd77e31b4b306b08435587f77626a75d37153024",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Amd64),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Globalization.Native.so")
            {
                // SymbolCollector.Console Linux arm symtab, unwind
                // System.Globalization.Native.so (arm) -> ./output/t/bb/65c174e6c95a8039261e2c63f12f9addbdb03e/executable
                Expected = new ObjectFileResult(
                    "74c165bb-c9e6-805a-3926-1e2c63f12f9a",
                    "bb65c174e6c95a8039261e2c63f12f9addbdb03e",
                    "TestFiles/System.Globalization.Native.so",
                    "2838c2268b7303cb89ee3c9fe5c610405accfadb8167ef685432dc3952ae38c4",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libxamarin-app.so")
            {
                // SymbolCollector.Android Android armeabi-v7a symtab
                // libxamarin-app.so (arm) -> ./output/t/f5/9d3adfa8263dd688ad820f74a325b540dcf6b4/executable
                Expected = new ObjectFileResult(
                    "df3a9df5-26a8-d63d-88ad-820f74a325b5",
                    "f59d3adfa8263dd688ad820f74a325b540dcf6b4",
                    "TestFiles/libxamarin-app.so",
                    "1a40a2db7c6b4dd59e3bcecd9b53cf3c7fc544afc311e25c41ee01bc4bb99a96",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libxamarin-app-arm64-v8a.so")
            {
                // SymbolCollector.Android Android arm64 symtab
                // libxamarin-app-arm64-v8a.so (arm64) -> ./output/t/76/21750937f30bf8e756cec46b960391f9f57b26/debuginfo
                Expected = new ObjectFileResult(
                    "09752176-f337-f80b-e756-cec46b960391",
                    "7621750937f30bf8e756cec46b960391f9f57b26",
                    "TestFiles/libxamarin-app-arm64-v8a.so",
                    "5fb23797a8cb482bac325eabdcb3d7e70b89fe0ec51035010e9be3a7b76fff84",
                    BuildIdType.GnuBuildId,
                    ObjectKind.Debug,
                    FileFormat.Elf,
                    Architecture.Arm64),
                ExpectedSymsorterFileName = "debuginfo"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libqcbassboost.so")
            {
                // Android 4.4.4 device, no debug-id (using hash of .text section) (difutil says 'not usable')
                // libqcbassboost.so (arm) -> ./output/t/63/7aa379d34ed455c314d646b8f3eaec0/executable
                Expected = new ObjectFileResult(
                    "637aa379-d34e-d455-c314-d646b8f3eaec",
                    "637aa379d34ed455c314d646b8f3eaec",
                    "TestFiles/libqcbassboost.so",
                    "6c303e667a39256bc8c4ad1ac58d5fcdcce8c35e25b6bbea9f0af259dad989a4",
                    BuildIdType.TextSectionHash,
                    ObjectKind.Library,
                    FileFormat.Elf,
                    Architecture.Arm),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/System.Net.Http.Native.dylib")
            {
                // SymbolCollector.Console macOS x86_x64 symtab
                // System.Net.Http.Native.dylib (x86_64) -> ./output/t/c5/ff520ae05c3099921ea8229f808696/executable
                Expected = new ObjectFileResult(
                    "c5ff520a-e05c-3099-921e-a8229f808696",
                    "c5ff520ae05c3099921ea8229f808696",
                    "TestFiles/System.Net.Http.Native.dylib",
                    "38e19513fb0693d07545c0fd399f1af06af478fb61bb2c882b962441503cefbf",
                    BuildIdType.Uuid,
                    ObjectKind.Library,
                    FileFormat.MachO,
                    Architecture.Amd64),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libutil.dylib")
            {
                // From macOS Catalina x86_64 /usr/lib/ symtab
                // libutil.dylib (x86_64) -> ./output/t/84/4b788709b33d12acdec4eb8f53dc62/executable
                Expected = new ObjectFileResult(
                    "844b7887-09b3-3d12-acde-c4eb8f53dc62",
                    "844b788709b33d12acdec4eb8f53dc62",
                    "TestFiles/libutil.dylib",
                    "8a1ef4651d0572cbc4defe54b684589a357f22b4389f65d948a6cc36ec9e654b",
                    BuildIdType.Uuid,
                    ObjectKind.Library,
                    FileFormat.MachO,
                    Architecture.Amd64),
                ExpectedSymsorterFileName = "executable"
            };

            yield return new ObjectFileResultTestCase("TestFiles/libswiftObjectiveC.dylib")
            {
                // libswiftObjectiveC.dylib Fat Mach-O (symtab, unwind) has 2 debug ids:
                // > 9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22 (x86)
                // > b18e9245-d333-326c-af0b-29285a0b3a6d (x86_64)
                Expected = new FatMachOFileResult(
                    string.Empty,
                    string.Empty,
                    "TestFiles/libswiftObjectiveC.dylib",
                    "84b77d90f34bffbd88c3fe45b4f2b64b364dd2fc9cefe69ca073de4dea100366",
                    new[]
                    {
                        new ObjectFileResult(
                            "9787d26c-6cd6-30a9-b5a1-e9c7c42ddb22",
                            "9787d26c6cd630a9b5a1e9c7c42ddb22",
                            "temp-path/libswiftObjectiveC.dylib",
                            "40eb972e254cd097d1f57c678dc531084f232da4f5db383f96449c589db76d83",
                            BuildIdType.Uuid,
                            ObjectKind.Library,
                            FileFormat.MachO,
                            Architecture.X86),
                        new ObjectFileResult(
                            "b18e9245-d333-326c-af0b-29285a0b3a6d",
                            "b18e9245d333326caf0b29285a0b3a6d",
                            "temp-path/libswiftObjectiveC.dylib",
                            "0364787021438a401bf732d804e2b1911d9d279d14d40bda7ab25193a00ca870",
                            BuildIdType.Uuid,
                            ObjectKind.Library,
                            FileFormat.MachO,
                            Architecture.Amd64)
                    }
                )
            };
            // Java class file which has the same magic bytes as a Fat Mach-O file
            yield return new ObjectFileResultTestCase("TestFiles/DiskBuffer$1.class") {Expected = null!};
        }

        public IEnumerator<object[]> GetEnumerator() =>
            GetObjectFileResultTestCases().Select(symbolsTestCase => new object[] {symbolsTestCase}).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [DebuggerDisplay("{Path} - {Expected}")]
    public class ObjectFileResultTestCase
    {
        public ObjectFileResult? Expected { get; set; }
        public string? ExpectedSymsorterFileName { get; set; }
        public string Path { get; }

        public ObjectFileResultTestCase(string path) => Path = path;
    }
}
