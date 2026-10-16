using Xunit;
using Xunit.Abstractions;

namespace HotelManagement.UnitTests;

public class HashGeneratorTest
{
    private readonly ITestOutputHelper _output;

    public HashGeneratorTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GenerateHash()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Admin123*!", 11);
        _output.WriteLine($"BCRYPT_HASH:{hash}");
        Assert.True(BCrypt.Net.BCrypt.Verify("Admin123*!", hash));
    }
}
