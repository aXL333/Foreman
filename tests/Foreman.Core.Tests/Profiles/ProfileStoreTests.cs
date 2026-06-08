using Foreman.Core.Models;
using Foreman.Core.Profiles;

namespace Foreman.Core.Tests.Profiles;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _profileDir = Path.Combine(Path.GetTempPath(), "foreman-profile-store-test-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        _store = new ProfileStore(_profileDir);
        _store.Initialize();
    }

    [Theory]
    [InlineData("t3-code", "t3-code-default")]
    [InlineData("opencode", "opencode-default")]
    public void BuiltInProfiles_AreLoadedForNewHarnesses(string harnessId, string profileName)
    {
        Assert.NotNull(KnownHarnesses.GetById(harnessId));
        Assert.Equal(profileName, HarnessIntegrationRegistry.GetDefaultProfileName(harnessId));
        Assert.NotNull(_store.Get(profileName));
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_profileDir))
            Directory.Delete(_profileDir, recursive: true);
    }
}
