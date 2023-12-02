using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SymbolCollector.Core;

public class FatMachO : IDisposable
{
    private readonly bool _deleteFilesOnDispose;

    public FatMachO(bool deleteFilesOnDispose = false) => _deleteFilesOnDispose = deleteFilesOnDispose;

    public FatHeader Header { get; set; }
    public IEnumerable<string> MachOFiles { get; set; } = Enumerable.Empty<string>();

    internal IEnumerable<string> FilesToDelete { get; set; } = Enumerable.Empty<string>();

    public void Dispose()
    {
        if (_deleteFilesOnDispose)
        {
            foreach (var file in FilesToDelete)
            {
                File.Delete(file);
            }
        }
    }
}

// AKA multi-arch file/universal binary
public class FatBinaryReader
{
    private readonly ILogger<FatBinaryReader> _logger;
    public const uint FatObjectMagic = 0x_cafe_babe;
    public const uint FatObjectCigam = 0x_beba_feca;
    private const int FatArchSize = 20;
    private const int HeaderSize = 8;

    public FatBinaryReader(ILogger<FatBinaryReader>? logger = null)
        => _logger = logger ?? NullLogger<FatBinaryReader>.Instance;

    public bool TryLoad(string path, out FatMachO? fatMachO)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(path);
            return TryLoad(Path.GetFileName(path), fileBytes, out fatMachO);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Couldn't open file.");
            fatMachO = null;
            return false;
        }
    }

    private static bool TryLoad(string fileName, byte[] bytes, out FatMachO? fatMachO)
    {
        fatMachO = null;
        var header = ParseHeader(bytes);
        if (header == null || bytes.Length < header.Value.FatArchCount * FatArchSize + HeaderSize)
        {
            return false;
        }

        var filesToDelete = new List<string>();
        fatMachO = new FatMachO(true)
        {
            Header = header.Value,
            FilesToDelete = filesToDelete,
            MachOFiles = GetFatArches(bytes, (int)header.Value.FatArchCount)
                .Select(arch =>
                {
                    // TODO: This needs to change. Current Mach-O lib only reads from disk
                    // Without System.Range (.NET Standard 2.1) we can't make a slice so we need
                    // to make a copy of the range into a new byte array, and write to disk for ELFSharp
                    // lib to pick it up.
                    var buffer = new byte[arch.Size];
                    Buffer.BlockCopy(bytes, (int)arch.StartOffset, buffer, 0, (int)arch.Size);

                    // blocking I/O
                    var newTempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    Directory.CreateDirectory(newTempDir);
                    var originalNameTempPath = Path.Combine(newTempDir, fileName);
                    filesToDelete.Add(originalNameTempPath);
                    File.WriteAllBytes(originalNameTempPath, buffer);
                    return originalNameTempPath;
                })
        };
        return true;
    }

    internal static IEnumerable<FatArch> GetFatArches(byte[] bytes, int count)
    {
        var buffer = new byte[4];

        for (var i = HeaderSize; i < count * FatArchSize; i += FatArchSize)
        {
            var itemOffset = i;
            yield return new FatArch
            {
                CpuType = Get(),
                CpuSubType = Get(),
                StartOffset = Get(),
                Size = Get(),
                Align = Get(),
            };

            uint Get()
            {
                var value = GetFatBinaryUint32(bytes, itemOffset, buffer, 0);
                itemOffset += 4;
                return value;
            }
        }
    }

    private static uint GetFatBinaryUint32(byte[] src, int srcOffset, byte[] dst, int dstOffset)
    {
        Buffer.BlockCopy(src, srcOffset, dst, dstOffset, 4);
        // Fat files are BigEndian
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(dst);
        }

        return BitConverter.ToUInt32(dst, 0);
    }

    internal static FatHeader? ParseHeader(byte[] bytes)
    {
        if (bytes == null || bytes.Length < HeaderSize || !IsFatBinary(bytes, out var magic))
        {
            return null;
        }

        var fatArchCount = GetFatBinaryUint32(bytes, 4, new byte[4], 0);
        // https://github.com/file/file/blob/c81d1ccbf4c224af50e6d556419961dba72666c7/magic/Magdir/cafebabe#L12
        if (fatArchCount > 43)
        {
            return null;
        }

        return new FatHeader
        {
            Magic = magic,
            FatArchCount = fatArchCount
        };
    }

    public static bool IsFatBinary(byte[] bytes, out uint magic)
    {
        if (bytes.Length < 4)
        {
            magic = 0;
            return false;
        }
        var buffer = new byte[4];
        magic = GetFatBinaryUint32(bytes, 0, buffer, 0);
        return magic == FatObjectMagic || magic == FatObjectCigam;
    }
}

public struct FatArch
{
    private const uint CpuArchAbi64 = 0x0100_0000;
    public uint CpuType { get; set; }
    public uint CpuSubType { get; set; }
    public uint StartOffset { get; set; }
    public uint Size { get; set; }
    public uint Align { get; set; }

    public bool Is64Bit() => (CpuType & CpuArchAbi64) == CpuArchAbi64;
}

public struct FatHeader
{
    public uint Magic { get; set; } // cafebabe or beba_feca
    public uint FatArchCount { get; set; }
}