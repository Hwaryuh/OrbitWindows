using Orbit.Core.Ports;

namespace Orbit.Tests;

public sealed class PortSearchTests
{
    private static PortEntry Tcp(int port, string name, int pid = 100, bool window = false, string host = "127.0.0.1") =>
        new(PortProtocol.Tcp, host, port, pid, name, null, window, CloseBlock.None);

    private static readonly PortEntry[] Table =
    [
        Tcp(135, "svchost", 1),
        Tcp(3000, "node", 200),
        Tcp(3000, "node", 200, host: "::1"),
        Tcp(3001, "node", 201),
        Tcp(5173, "bun", 300),
        Tcp(8080, "java", 400),
        Tcp(1159, "Orca", 500, window: true),
        new(PortProtocol.Udp, "0.0.0.0", 3002, 600, "node", null, false, CloseBlock.None),
    ];

    private static int[] Ports(string query) => [.. PortSearch.Match(query, Table).Select(e => e.Port)];

    [Theory]
    [InlineData("3000")]
    [InlineData(":3000")]
    [InlineData("localhost:3000")]
    [InlineData("127.0.0.1:3000")]
    public void APortIsFoundByItsNumber(string query) => Assert.Equal([3000], Ports(query));

    [Fact]
    public void ANumberMatchesPortsStartingWithIt() => Assert.Equal([3000, 3001], Ports("300"));

    [Fact]
    public void OneDigitIsTooFew() => Assert.Empty(Ports("3"));

    [Fact]
    public void BothAddressesOfOneProgramAreOneRow() => Assert.Single(PortSearch.Match("3000", Table));

    [Fact]
    public void UdpIsLeftOut() => Assert.Empty(Ports("3002"));

    [Theory]
    [InlineData("포트")]
    [InlineData("ports")]
    [InlineData("por")]
    [InlineData("localhost")]
    public void AskingForPortsListsDevServersThenPrograms(string query) => Assert.Equal([3000, 3001, 5173, 8080, 1159], Ports(query));

    [Fact]
    public void WindowsServicesAreNotListed() => Assert.DoesNotContain(135, Ports("포트"));

    [Fact]
    public void ADevServerIsFoundByItsProgram() => Assert.Equal([3000, 3001], Ports("node"));

    [Fact]
    public void OtherProgramsAreNotFoundByName()
    {
        Assert.Empty(Ports("svchost"));
        Assert.Empty(Ports("orca"));
    }

    [Fact]
    public void AtMostEight()
    {
        var many = Enumerable.Range(4000, 20).Select(p => Tcp(p, "node", p));
        Assert.Equal(8, PortSearch.Match("40", many).Count);
    }

    [Theory]
    [InlineData("3000", true)]
    [InlineData("포트", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("3", false)]
    public void OnlyQueriesThatCouldBeAboutPortsAreLookedUp(string query, bool asks) => Assert.Equal(asks, PortSearch.Asks(query));
}
