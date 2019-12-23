using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
 using Microsoft.Extensions.Logging;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class ClientTests
    {
        private class Fixture
        {
            public Uri ServiceUri { get; set; } = new Uri("https://test.sentry/");
            public FatBinaryReader? FatBinaryReader { get; set; }
            public HttpMessageHandler? HttpMessageHandler { get; set; }
            public AssemblyName? AssemblyName { get; set; }
            public int? ParallelTasks { get; set; }
            public HashSet<string>? BlackListedPaths { get; set; }
            public ILogger<Client>? Logger { get; set; }

            public Client GetSut() =>
                new Client(
                    ServiceUri,
                    FatBinaryReader,
                    HttpMessageHandler,
                    AssemblyName,
                    ParallelTasks,
                    BlackListedPaths,
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
        // Not an ELF file (came from an Android x86 emulator)
        [InlineData(null, "TestFiles/libdl.bc")]
        // macOS file
        [InlineData(null, "TestFiles/System.Net.Http.Native.dylib")]
        public void GetElfBuildId_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var actual = sut.GetElfBuildId(path);
            if (debugId is null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.Equal(new Guid(debugId).ToString(), actual);
            }
        }

        [Fact]
        public void ParallelTasks_DefaultValue_Ten()
        {
            var sut = _fixture.GetSut();
            Assert.Equal(10, sut.ParallelTasks);
        }
    }
}
