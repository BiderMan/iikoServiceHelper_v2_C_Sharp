using FluentAssertions;
using iikoServiceHelper.Utils;
using Xunit;

namespace iikoServiceHelper.Tests.Utils
{
    public class StringUtilsTests
    {
        [Theory]
        [InlineData("ghbdtn", "привет")]
        [InlineData("rfr ltkf?", "как дела?")]
        [InlineData("", "")]
        public void ConvertLayout_ShouldConvertEnglishToRussian(
            string input,
            string expected)
        {
            // Act
            var result = StringUtils.ConvertLayout(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("привет", "ghbdtn")]
        [InlineData("как дела?", "rfr ltkf?")]
        public void ConvertLayout_ShouldConvertRussianToEnglish(
            string input,
            string expected)
        {
            // Act
            var result = StringUtils.ConvertLayout(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ConvertLayout_ShouldHandleNullInput()
        {
            // Act
            var result = StringUtils.ConvertLayout(null);

            // Assert
            result.Should().BeNullOrEmpty();
        }
    }
}
