using System.Linq;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class FatBinaryReaderTest
    {
        private readonly byte[] _fatMachO =
        {
            0xca, 0xfe, 0xba, 0xbe, 0x00, 0x00, 0x00, 0x02, 0x01, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x03, 0x00,
            0x00, 0x10, 0x00, 0x00, 0x00, 0x5e, 0xe0, 0x00, 0x00, 0x00, 0x0c, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00,
            0x00, 0x03, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x5c, 0xf0, 0x00, 0x00, 0x00, 0x0c
        };

        [Fact]
        public void IsFatBinary_ValidFatMachO_True() => Assert.False(FatBinaryReader.IsFatBinary(_fatMachO));

        [Fact]
        public void IsFatBinary_BufferTooShort_False() => Assert.False(FatBinaryReader.IsFatBinary(new byte[] { 0xca, 0xfe, 0xba }));

        [Fact]
        public void IsFatBinary_NotFatMachO_False() => Assert.False(FatBinaryReader.IsFatBinary(new byte[] { 0xca, 0xfe, 0xba, 0x0c }));

        [Fact]
        public void ParseHeader_ValidFatBinary_True()
        {
            var header = FatBinaryReader.ParseHeader(_fatMachO);

            Assert.True(header.HasValue);
#nullable disable
            Assert.Equal(FatBinaryReader.FatObjectMagic, header.Value.Magic);
#nullable enable
            Assert.Equal(2u, header.Value.FatArchCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(new byte[] { })] // empty buffer
        [InlineData(new byte[] { 0xca, 0xfe, 0xba })] // invalid magic
        [InlineData(new byte[] { 0xca, 0xfe, 0xba, 0xbe })] // Valid magic but no arch count
        [InlineData(new byte[] { 0xca, 0xfe, 0xba, 0xbe, 0x00, 0x00, 0x00 })] // Valid magic, byte short
        public void ParseHeader_BufferTooSmall_ReturnsNull(byte[] input) => Assert.Null(FatBinaryReader.ParseHeader(input));

        [Fact]
        public void GetFatArches_()
        {
            var arches = FatBinaryReader.GetFatArches(_fatMachO, 2).ToList();
            Assert.Equal(2, arches.Count);
            Assert.True(arches[0].Is64Bit());
            Assert.False(arches[1].Is64Bit());
        }
    }
}
