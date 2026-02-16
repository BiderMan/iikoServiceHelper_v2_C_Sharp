using FluentAssertions;
using iikoServiceHelper.Models;
using Xunit;

namespace iikoServiceHelper.Tests.Models
{
    public class AppSettingsTests
    {
        [Fact]
        public void CrmPassword_ShouldEncryptAndDecrypt()
        {
            // Arrange
            var settings = new AppSettings();
            var originalPassword = "MySecretPassword123!";

            // Act
            settings.CrmPassword = originalPassword;
            var decrypted = settings.CrmPassword;

            // Assert
            decrypted.Should().Be(originalPassword);
            settings.CrmPasswordEncrypted.Should().NotBe(originalPassword);
            settings.CrmPasswordEncrypted.Should().NotBeEmpty();
        }

        [Fact]
        public void CrmPassword_ShouldHandleEmptyPassword()
        {
            // Arrange
            var settings = new AppSettings();

            // Act
            settings.CrmPassword = "";
            var decrypted = settings.CrmPassword;

            // Assert
            decrypted.Should().BeEmpty();
            settings.CrmPasswordEncrypted.Should().BeEmpty();
        }

        [Fact]
        public void DelaySettings_ShouldHaveDefaultValues()
        {
            // Arrange
            var settings = new AppSettings();

            // Assert
            settings.Delays.Should().NotBeNull();
            settings.Delays.KeyPress.Should().Be(50);
            settings.Delays.ActionPause.Should().Be(100);
            settings.Delays.FocusWait.Should().Be(500);
        }
    }
}
