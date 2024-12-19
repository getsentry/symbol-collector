using Xunit;

namespace SymbolCollector.Core.Tests;

public class FatBinaryReaderTest
{
    private readonly byte[] _fatMachO =
    {
        0xca, 0xfe, 0xba, 0xbe, 0x00, 0x00, 0x00, 0x02, 0x01, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x03, 0x00,
        0x00, 0x10, 0x00, 0x00, 0x00, 0x5e, 0xe0, 0x00, 0x00, 0x00, 0x0c, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00,
        0x00, 0x03, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x5c, 0xf0, 0x00, 0x00, 0x00, 0x0c
    };

    [Fact]
    public void IsFatBinary_ValidFatMachO_True()
    {
        Assert.True(FatBinaryReader.IsFatBinary(_fatMachO, out var magic));
        Assert.Equal(0x_cafe_babe, magic);
    }

    [Fact]
    public void IsFatBinary_ReverseMagicFatMachO_True()
    {
        Array.Reverse(_fatMachO, 0, 4);
        Assert.True(FatBinaryReader.IsFatBinary(_fatMachO, out var magic));
        Assert.Equal(0x_beba_feca, magic);
    }

    [Fact]
    public void IsFatBinary_BufferTooShort_False()
    {
        Assert.False(FatBinaryReader.IsFatBinary(new byte[] {0xca, 0xfe, 0xba}, out var magic));
        Assert.Equal(0u, magic);
    }

    [Fact]
    public void IsFatBinary_NotFatMachO_False() => Assert.False(FatBinaryReader.IsFatBinary(new byte[] { 0xca, 0xfe, 0xba, 0x0c }, out _));

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

    [Fact]
    public void ParseHeader_ReversedFatBinary_True()
    {
        Array.Reverse(_fatMachO, 0, 4);
        var header = FatBinaryReader.ParseHeader(_fatMachO);

        Assert.True(header.HasValue);
#nullable disable
        Assert.Equal(FatBinaryReader.FatObjectCigam, header.Value.Magic);
#nullable enable
        Assert.Equal(2u, header.Value.FatArchCount);
    }

    [Theory]
    [InlineData(new byte[] { })] // empty buffer
    [InlineData(new byte[] { 0xca, 0xfe, 0xba })] // invalid magic
    [InlineData(new byte[] { 0xca, 0xfe, 0xba, 0xbe })] // Valid magic but no arch count
    [InlineData(new byte[] { 0xca, 0xfe, 0xba, 0xbe, 0x00, 0x00, 0x00 })] // Valid magic, byte short
    public void ParseHeader_BufferTooSmall_ReturnsNull(byte[] input) => Assert.Null(FatBinaryReader.ParseHeader(input));

    [Fact]
    public void GetFatArches_ExpectedFatFilesArchitectures()
    {
        var arches = FatBinaryReader.GetFatArches(_fatMachO, 2).ToList();
        Assert.Equal(2, arches.Count);
        Assert.True(arches[0].Is64Bit());
        Assert.False(arches[1].Is64Bit());
    }

    [Fact]
    public void TryLoad_InvalidPath_ReturnsNull()
    {
        var target = new FatBinaryReader();
        Assert.False(target.TryLoad("invalid", out _));
    }
}
