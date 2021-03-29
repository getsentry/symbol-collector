using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SymbolCollector.Core;

namespace SymbolCollector.Server.Models
{
    public class SymbolMetadata
    {
        public string UnifiedId { get; set; }
        public string? Hash { get; set; }
        public string Path { get; set; }

        // Symsorter uses this to name the file
        public ObjectKind ObjectKind { get; set; }

        // /refs
        // name=
        public string Name { get; set; }

        // arch= arm, arm64, x86, x86_64
        public Architecture Arch { get; set; }

        // file_format= elf, macho
        public FileFormat FileFormat { get; set; }

        public ConcurrentDictionary<Guid, object?> BatchIds { get; }

        public SymbolMetadata(
            string unifiedId,
            string? hash,
            string path,
            ObjectKind objectKind,
            string name,
            Architecture arch,
            FileFormat fileFormat,
            ConcurrentDictionary<Guid, object?> batchIds)
        {
            UnifiedId = unifiedId;
            Hash = hash;
            Path = path;
            ObjectKind = objectKind;
            Name = name;
            Arch = arch;
            FileFormat = fileFormat;
            BatchIds = batchIds;
        }
    }
}
