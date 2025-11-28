namespace FileWatchRest.Tests.Configuration;

public class SecureConfigurationHelperTests {
    [Fact]
    public void IsTokenEncrypted_ReturnsFalse_ForPlainText() => SecureConfigurationHelper.IsTokenEncrypted("plain").Should().BeFalse();

    [Fact]
    public void IsTokenEncrypted_ReturnsTrue_ForEncryptedPrefix() => SecureConfigurationHelper.IsTokenEncrypted("enc:abcd").Should().BeTrue();

    [Fact]
    public void EnsureTokenIsEncrypted_Encrypts_WhenPlainText_OnWindows() {
        if (!OperatingSystem.IsWindows()) {
            // Running on non-windows in CI, just assert it returns input
            SecureConfigurationHelper.EnsureTokenIsEncrypted("plain").Should().Be("plain");
            return;
        }

        string input = "my-secret-token";
        string ensured = SecureConfigurationHelper.EnsureTokenIsEncrypted(input);
        ensured.Should().StartWith("enc:");
        string decrypted = SecureConfigurationHelper.DecryptBearerToken(ensured);
        decrypted.Should().Be(input);
    }

    [Fact]
    public void DecryptBearerToken_Throws_WhenMissingPrefix() {
        if (!OperatingSystem.IsWindows()) {
            return; // behavior differs on non-windows
        }

        System.Action act = () => SecureConfigurationHelper.DecryptBearerToken("not-enc");
        act.Should().Throw<InvalidOperationException>().WithMessage("Token does not have the expected encryption prefix");
    }
}
