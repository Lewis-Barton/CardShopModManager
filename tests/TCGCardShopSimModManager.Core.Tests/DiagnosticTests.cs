using System.Text;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void ReadRecentLines_ReturnsRequestedTailFromLargeFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diagnostic-{Guid.NewGuid():N}.log");
        try
        {
            using (var writer = new StreamWriter(path, append: false, Encoding.UTF8))
            {
                for (var i = 0; i < 150_000; i++)
                    writer.WriteLine($"old-{i:000000}");
                writer.WriteLine("wanted-one");
                writer.WriteLine("wanted-two");
            }

            var lines = Diagnostic.ReadRecentLines(path, 2);

            Assert.Equal(["wanted-one", "wanted-two"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRecentLines_HandlesEmptyAndZeroLengthRequests()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diagnostic-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, string.Empty);

            Assert.Empty(Diagnostic.ReadRecentLines(path, 10));
            Assert.Empty(Diagnostic.ReadRecentLines(path, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
