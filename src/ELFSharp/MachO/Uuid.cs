using System;
using System.IO;

namespace ELFSharp.MachO
{
    // LC_UUID: 128 bit unique id of the object
    public class Uuid : Command
    {
        public Uuid(BinaryReader reader, Func<FileStream> streamProvider) : base(reader, streamProvider)
        {
            OriginalUuid = new byte[16];
            Reader.Read(OriginalUuid, 0, 16);

            // TODO: Make sure this needs to go LE
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(OriginalUuid, 0, 4);
                Array.Reverse(OriginalUuid, 4, 2);
                Array.Reverse(OriginalUuid, 6, 2);
            }
        }

        public byte[] OriginalUuid { get; }
        public Guid Id => new Guid(OriginalUuid);
    }
}
