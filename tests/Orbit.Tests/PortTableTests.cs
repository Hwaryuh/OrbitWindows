using System.Buffers.Binary;
using Orbit.Core.Ports;

namespace Orbit.Tests;

public class PortTableTests
{
    // A table as Windows writes it: the count, then rows of 4-byte fields.
    private static byte[] Table(int rowSize, params Action<Span<byte>>[] rows)
    {
        var buffer = new byte[4 + rowSize * rows.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)rows.Length);
        for (int i = 0; i < rows.Length; i++) rows[i](buffer.AsSpan(4 + i * rowSize, rowSize));
        return buffer;
    }

    private static void Put(Span<byte> row, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(row[at..], value);

    // 3000 in network byte order, as the port field holds it.
    private const uint Port3000 = 0xB80B;

    [Fact]
    public void PortIsNetworkOrder()
    {
        Assert.Equal(3000, PortTable.PortOf(Port3000));
        Assert.Equal(80, PortTable.PortOf(0x5000));
        Assert.Equal(65535, PortTable.PortOf(0xFFFF));
    }

    [Fact]
    public void ReadsTcp4Rows()
    {
        var table = Table(24, row =>
        {
            Put(row, 0, 2); // LISTEN
            row[4] = 127; row[7] = 1;
            Put(row, 8, Port3000);
            Put(row, 20, 1234);
        });
        var socket = Assert.Single(PortTable.Parse(table, PortProtocol.Tcp, false)!);
        Assert.Equal(new PortTable.Socket(PortProtocol.Tcp, "127.0.0.1", 3000, 1234), socket);
    }

    [Fact]
    public void ReadsTcp6RowsWithScope()
    {
        var table = Table(56,
            row =>
            {
                Put(row, 20, Port3000);
                Put(row, 52, 77);
            },
            row =>
            {
                row[0] = 0xFE; row[1] = 0x80; row[15] = 1;
                Put(row, 16, 5);
                Put(row, 20, 0x5000);
                Put(row, 52, 78);
            });
        var rows = PortTable.Parse(table, PortProtocol.Tcp, true)!;
        Assert.Equal(new PortTable.Socket(PortProtocol.Tcp, "::", 3000, 77), rows[0]);
        Assert.Equal(new PortTable.Socket(PortProtocol.Tcp, "fe80::1%5", 80, 78), rows[1]);
    }

    [Fact]
    public void ReadsUdpRows()
    {
        var v4 = Table(12, row =>
        {
            Put(row, 4, 0x3500);
            Put(row, 8, 9);
        });
        Assert.Equal(new PortTable.Socket(PortProtocol.Udp, "0.0.0.0", 53, 9), Assert.Single(PortTable.Parse(v4, PortProtocol.Udp, false)!));
        var v6 = Table(28, row =>
        {
            row[15] = 1;
            Put(row, 20, 0x3500);
            Put(row, 24, 10);
        });
        Assert.Equal(new PortTable.Socket(PortProtocol.Udp, "::1", 53, 10), Assert.Single(PortTable.Parse(v6, PortProtocol.Udp, true)!));
    }

    [Fact]
    public void RejectsCountsTheBufferCannotHold()
    {
        Assert.Null(PortTable.Parse(new byte[3], PortProtocol.Tcp, false));
        var table = Table(24, _ => { });
        BinaryPrimitives.WriteUInt32LittleEndian(table, 2);
        Assert.Null(PortTable.Parse(table, PortProtocol.Tcp, false));
        Assert.Empty(PortTable.Parse(new byte[4], PortProtocol.Udp, true)!);
    }

    private static PortEntry Entry(PortProtocol protocol, string host, int port, int pid, string name) =>
        new(protocol, host, port, pid, name, 1, false, CloseBlock.None);

    [Fact]
    public void WildcardsAndEndpoints()
    {
        Assert.True(Entry(PortProtocol.Tcp, "0.0.0.0", 80, 1, "a").AllInterfaces);
        Assert.True(Entry(PortProtocol.Tcp, "::", 80, 1, "a").AllInterfaces);
        Assert.False(Entry(PortProtocol.Tcp, "127.0.0.1", 80, 1, "a").AllInterfaces);
        Assert.Equal("[::1]:80", Entry(PortProtocol.Tcp, "::1", 80, 1, "a").Endpoint);
        Assert.Equal("127.0.0.1:80", Entry(PortProtocol.Tcp, "127.0.0.1", 80, 1, "a").Endpoint);
    }

    [Fact]
    public void ArrangeDropsRepeatsAndSortsByPort()
    {
        var arranged = PortTable.Arrange(
        [
            Entry(PortProtocol.Tcp, "0.0.0.0", 8080, 3, "node"),
            Entry(PortProtocol.Udp, "0.0.0.0", 53, 2, "dns"),
            Entry(PortProtocol.Tcp, "0.0.0.0", 53, 2, "dns"),
            Entry(PortProtocol.Tcp, "0.0.0.0", 8080, 3, "node"),
        ]);
        Assert.Equal([(53, PortProtocol.Tcp), (53, PortProtocol.Udp), (8080, PortProtocol.Tcp)], arranged.Select(e => (e.Port, e.Protocol)));
    }

    [Fact]
    public void FilterMatchesEveryWordAndHidesUdpUnlessAsked()
    {
        PortEntry[] entries =
        [
            Entry(PortProtocol.Tcp, "127.0.0.1", 3000, 41, "node"),
            Entry(PortProtocol.Tcp, "::", 5432, 52, "postgres"),
            Entry(PortProtocol.Udp, "0.0.0.0", 5353, 60, "chrome"),
        ];
        Assert.Equal(2, PortTable.Filter(entries, "", udp: false).Count());
        Assert.Equal(3, PortTable.Filter(entries, " ", udp: true).Count());
        Assert.Equal("node", Assert.Single(PortTable.Filter(entries, "NODE 3000", udp: false)).Name);
        Assert.Equal("postgres", Assert.Single(PortTable.Filter(entries, "52", udp: false)).Name);
        Assert.Empty(PortTable.Filter(entries, "chrome", udp: false));
        Assert.Single(PortTable.Filter(entries, "chrome", udp: true));
        Assert.Single(PortTable.Filter(entries, "[::]", udp: false));
    }
}
