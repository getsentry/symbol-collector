using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ELFSharp.MachO;

namespace SymbolCollector.Core
{
    public class FatMachO : IDisposable
    {
        private readonly bool _deleteFilesOnDispose;

        public FatMachO(bool deleteFilesOnDispose = false) => _deleteFilesOnDispose = deleteFilesOnDispose;

        public FatHeader Header { get; set; }
        public IEnumerable<string> MachOFiles { get; set; } = Enumerable.Empty<string>();

        public void Dispose()
        {
            if (_deleteFilesOnDispose)
            {
                foreach (var file in MachOFiles)
                {
                    File.Delete(file);
                }
            }
        }
    }

    // AKA multi-arch file/universal binary
    public static class FatBinaryReader
    {
        public const uint FatObjectMagic = 0x_cafe_babe;
        private const int FatArchSize = 20;
        private const int HeaderSize = 8;

        public static bool TryLoad(string path, out FatMachO? fatMachO)
        {
            var file = File.ReadAllBytes(path);
            return TryLoad(file, out fatMachO);
        }

        public static bool TryLoad(byte[] bytes, out FatMachO? fatMachO)
        {
            fatMachO = null;
            var header = ParseHeader(bytes);
            if (header == null || bytes.Length < header.Value.FatArchCount * FatArchSize + HeaderSize)
            {
                return false;
            }

            fatMachO = new FatMachO(true)
            {
                Header = header.Value,
                MachOFiles = GetFatArches(bytes, (int) header.Value.FatArchCount)
                    .Select(arch =>
                    {
                        // TODO: This needs to change. Current Mach-O lib only reads from disk
                        // Without System.Range (.NET Standard 2.1) we can't make a slice so we need
                        // to make a copy of the range into a new byte array, and write to disk for ELFSharp
                        // lib to pick it up.
                        var buffer = new byte[arch.Size];
                        Buffer.BlockCopy(bytes, (int)arch.StartOffset, buffer, 0, (int)arch.Size);

                        // blocking I/O
                        var file = Path.GetTempFileName();
                        File.WriteAllBytes(file, buffer);
                        return file;
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
                    CpuType  = Get(),
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
            if (bytes == null || bytes.Length < HeaderSize || !IsFatBinary(bytes))
            {
                return null;
            }

            var fatArchCount = new byte[4];

            return new FatHeader
            {
                Magic = FatObjectMagic, // Checked by IsFatBinary
                FatArchCount = GetFatBinaryUint32(bytes, 4, fatArchCount, 0)
            };
        }

        public static bool IsFatBinary(byte[] bytes)
        {
            if (bytes.Length < 4)
            {
                return false;
            }
            var magic = new byte[4];
            Buffer.BlockCopy(bytes, 0, magic, 0, 4);
            // Fat files are BigEndian
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(magic);
            }
            return BitConverter.ToUInt32(magic, 0) == FatObjectMagic;
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
        public uint Magic { get; set; } // cafebabe
        public uint FatArchCount { get; set; }
    }
}
