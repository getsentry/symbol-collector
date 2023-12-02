using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class FatMachOTests
    {
        [Fact]
        public void Dispose_DefaultArg_FileNotDeleted()
        {
            var file = Path.GetTempFileName();
            var f = new FatMachO {FilesToDelete = new[] { file }};
            f.Dispose();
            Assert.True(File.Exists(file));
            File.Delete(file);
        }
        [Fact]
        public void Dispose_DeleteOnDispose_FileDeleted()
        {
            var file = Path.GetTempFileName();
            var f = new FatMachO(deleteFilesOnDispose: true) { FilesToDelete = new[] { file } };
            f.Dispose();
            Assert.False(File.Exists(file));
        }
    }
}
