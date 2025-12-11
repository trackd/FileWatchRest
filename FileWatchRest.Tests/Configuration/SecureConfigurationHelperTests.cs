namespace FileWatchRest.Tests.Configuration;

public class SecureConfigurationHelperTests {
    [Fact]
    public void IsTokenEncrypted_ReturnsFalse_ForPlainText() => Assert.False(SecureConfigurationHelper.IsTokenEncrypted("plain"));

    [Fact]
    public void IsTokenEncrypted_ReturnsTrue_ForEncryptedPrefix() => Assert.True(SecureConfigurationHelper.IsTokenEncrypted("enc:abcd"));

    [Fact]
    public void EnsureTokenIsEncrypted_Encrypts_WhenPlainText_OnWindows() {
        if (!OperatingSystem.IsWindows()) {
            // Running on non-windows in CI, just assert it returns input
            Assert.Equal("plain", SecureConfigurationHelper.EnsureTokenIsEncrypted("plain"));
            return;
        }

        string input = "my-secret-token";
        string ensured = SecureConfigurationHelper.EnsureTokenIsEncrypted(input);
        Assert.StartsWith("enc:", ensured);
        string decrypted = SecureConfigurationHelper.DecryptBearerToken(ensured);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void DecryptBearerToken_Throws_WhenMissingPrefix() {
        if (!OperatingSystem.IsWindows()) {
            return; // behavior differs on non-windows
        }

        System.Action act = () => SecureConfigurationHelper.DecryptBearerToken("not-enc");
        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Token does not have the expected encryption prefix", ex.Message);
    }
}
