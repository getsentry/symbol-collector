using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class ClientTests
    {
        private class Fixture
        {
            public Uri ServiceUri { get; set; } = new Uri("https://test.sentry/");

            public FatBinaryReader? FatBinaryReader { get; set; } =
                new FatBinaryReader(Substitute.For<ILogger<FatBinaryReader>>());

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

        // Ids were checked with: 'sentry-cli difutil check'
        [Theory]
        // SymbolCollector.Console macOS x86_x64 symtab, unwind
        [InlineData("c5ff520a-e05c-3099-921e-a8229f808696", "TestFiles/System.Net.Http.Native.dylib")]
        // From macOS Catalina x86_64 /usr/lib/ symtab
        [InlineData("844b7887-09b3-3d12-acde-c4eb8f53dc62", "TestFiles/libutil.dylib")]
        // ELF file:
        [InlineData(null, "TestFiles/libxamarin-app-arm64-v8a.so")]
        public void GetMachOBuildId_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var actual = sut.GetMachOBuildId(path);
            if (debugId is null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.Equal(new Guid(debugId).ToString(), actual);
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
        public void GetMachOFromFatFile_TestFile_CorrectId(string debugId, string path)
        {
            var sut = _fixture.GetSut();
            var actual = sut.GetMachOFromFatFile(path);

            Assert.NotNull(actual);

            if (debugId is null)
            {
                Assert.Empty(actual);
            }
            else
            {
                var ids = actual.Select(a => a.debugId).ToList();
                Assert.Contains(debugId, ids);
            }
        }

        [Fact]
        public async Task UploadAllPathsAsync_TestFilesDirectory_FilesDetected()
        {
            var counter = 0;
            _fixture.HttpMessageHandler = new TestMessageHandler((message, token) =>
            {
                Interlocked.Increment(ref counter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            });

            var sut = _fixture.GetSut();
            await sut.UploadAllPathsAsync(new[] {"TestFiles"}, CancellationToken.None);
            Assert.Equal(11, counter);
            // TODO: Match exact files and their debug ids.
        }

        [Fact]
        public void ParallelTasks_DefaultValue_Ten()
        {
            var sut = _fixture.GetSut();
            Assert.Equal(10, sut.ParallelTasks);
        }

        private class TestMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

            public TestMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
                => _callback = callback;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => _callback(request, cancellationToken);
        }
    }
}
