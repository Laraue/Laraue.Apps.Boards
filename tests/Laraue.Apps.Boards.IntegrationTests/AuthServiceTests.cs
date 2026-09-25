using Laraue.Apps.Boards.WebApiServices;

namespace Laraue.Apps.Boards.IntegrationTests;

public class AuthServiceTests
{
    [Fact]
    public void GetSymmetricSecurityKey_ShouldThrow_WhenKeyIsShorterThan256Bits()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthService.GetSymmetricSecurityKey(new string('k', AuthService.MinKeyBytes - 1)));

        Assert.Contains("Auth:Key", exception.Message);
    }

    [Fact]
    public void GetSymmetricSecurityKey_ShouldCreateKey_WhenKeyIs256BitsLong()
    {
        var key = AuthService.GetSymmetricSecurityKey(new string('k', AuthService.MinKeyBytes));

        Assert.Equal(AuthService.MinKeyBytes * 8, key.KeySize);
    }
}
