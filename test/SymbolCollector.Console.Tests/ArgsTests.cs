using System.Threading;
using SymbolCollector.Core;
using Xunit;

namespace SymbolCollector.Console.Tests
{
    public class ArgsTests
    {
        private class Fixture
        {
            public Args NewArgsWithBatchType(string? batchType)
                => new Args(null, null, null, null, null, batchType, null, string.Empty, default, new CancellationTokenSource());
        }

        private readonly Fixture _fixture = new Fixture();

        [Fact]
        public void Ctor_NullBatchType_BatchTypeNotSet()
        {
            //Act
            var arg = _fixture.NewArgsWithBatchType(null);

            //Assert
            Assert.Null(arg.BatchType);
        }

        [Fact]
        public void Ctor_UnknownBatchType_BatchTypeNotSet()
        {
            //Act
            var arg = _fixture.NewArgsWithBatchType(null);

            //Assert
            Assert.Null(arg.BatchType);
        }

        [Theory]
        [InlineData("android", BatchType.Android)]
        [InlineData("Android", BatchType.Android)]
        [InlineData("ANDROID", BatchType.Android)]
        [InlineData("ios", BatchType.IOS)]
        [InlineData("watchos", BatchType.WatchOS)]
        [InlineData("linux", BatchType.Linux)]
        [InlineData("macos", BatchType.MacOS)]
        public void Ctor_ValidBatchType_BatchTypeSet(string batchType, BatchType expectedType )
        {
            //Act
            var arg = _fixture.NewArgsWithBatchType(batchType);

            //Assert
            Assert.Equal(expectedType, arg.BatchType);

        }
    }
}
