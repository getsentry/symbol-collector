using System;
using System.IO;
using System.Net;

namespace ELFSharp.MachO
{
    // Min version targeted when compiling the object
    public abstract class MinVersion : Command
    {
        protected MinVersion(BinaryReader reader, Func<FileStream> streamProvider) : base(reader, streamProvider)
        {
            VersionUInt32 = (uint)IPAddress.HostToNetworkOrder((int)Reader.ReadUInt32());
            SdkUInt32 = (uint)IPAddress.HostToNetworkOrder((int)Reader.ReadUInt32());
        }

        public uint VersionUInt32 { get; }
        public uint SdkUInt32 { get; }

        public string Sdk => ParseNibbles(SdkUInt32);
        public string Version => ParseNibbles(VersionUInt32);

        private String ParseNibbles(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            return
                $"{(bytes[0] & 0x0F) + ((bytes[0] & 0xF0) >> 4) + (bytes[1] & 0x0F) + ((bytes[1] & 0xF0) >> 4)}" +
                $".{(bytes[2] & 0x0F) + ((bytes[2] & 0xF0) >> 4)}" +
                $".{(bytes[3] & 0x0F) + ((bytes[3] & 0xF0) >> 4)}";
        }
    }

    // LC_VERSION_MIN_MACOSX
    public class MacOsMinVersion : MinVersion
    {
        public MacOsMinVersion(BinaryReader reader, Func<FileStream> streamProvider) : base(reader, streamProvider)
        {
        }
    }

    // LC_VERSION_MIN_IPHONEOS
    public class IPhoneOsMinVersion : MinVersion
    {
        public IPhoneOsMinVersion(BinaryReader reader, Func<FileStream> streamProvider) : base(reader, streamProvider)
        {
        }
    }
}
