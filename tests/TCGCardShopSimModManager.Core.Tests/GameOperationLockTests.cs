using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class GameOperationLockTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "cardshop-lock-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Acquire_RefusesASecondOperationForTheSameGame()
    {
        Directory.CreateDirectory(_root);
        using var first = GameOperationLock.Acquire(_root);

        var error = Assert.Throws<IOException>(() =>
            GameOperationLock.Acquire(Path.Combine(_root, "."), TimeSpan.Zero));

        Assert.Contains("Another mod manager operation", error.Message);
    }

    [Fact]
    public void Acquire_AllowsAnotherOperationAfterRelease()
    {
        Directory.CreateDirectory(_root);
        using (GameOperationLock.Acquire(_root))
        {
        }

        using var next = GameOperationLock.Acquire(_root, TimeSpan.Zero);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
