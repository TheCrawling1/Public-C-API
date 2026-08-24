using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ApiRouter.Actions;

/// <summary>
/// Guards outbound fetches of client-supplied URLs against SSRF. It classifies an address
/// as disallowed if it falls in a loopback, link-local, private, unique-local,
/// carrier-grade-NAT, multicast, or other non-public range (IPv4 and IPv6, including
/// IPv4-embedded IPv6 forms) — so a caller cannot point the router at cloud metadata
/// (169.254.169.254), localhost, or internal hosts.
/// </summary>
/// <remarks>
/// <see cref="ValidatePublicHostAsync"/> is a fast pre-flight reject; the real guarantee is
/// <see cref="SafeConnectAsync"/>, used as the download client's <see cref="SocketsHttpHandler.ConnectCallback"/>.
/// It resolves and connects to the exact validated address, so every connection — the initial
/// host and each redirect hop — is checked at connect time, closing the DNS-rebinding window.
/// </remarks>
public static class NetworkGuard
{
    public static async Task<(bool Ok, string? Reason)> ValidatePublicHostAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            }
            catch (SocketException)
            {
                return (false, "host could not be resolved");
            }
        }

        if (addresses.Length == 0)
        {
            return (false, "host did not resolve to any address");
        }

        foreach (var address in addresses)
        {
            if (IsDisallowed(address))
            {
                return (false, "host resolves to a disallowed (private, loopback, or link-local) address");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host and connects
    /// only to a validated public address — closing the DNS-rebinding window that a pre-flight
    /// check leaves open, because the address actually connected to is the one that was checked.
    /// Invoked for every connection the handler opens, so redirect hops are validated too.
    /// </summary>
    public static async ValueTask<Stream> SafeConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endPoint.Host, ct);

        foreach (var address in addresses)
        {
            if (IsDisallowed(address))
            {
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                // A real cancel/timeout must propagate, not be retried against another address.
                socket.Dispose();
                throw;
            }
            catch
            {
                socket.Dispose();
            }
        }

        throw new HttpRequestException($"No allowed address to connect to for host '{endPoint.Host}'.");
    }

    internal static bool IsDisallowed(IPAddress address)
    {
        // Normalize IPv4-mapped IPv6 (e.g. ::ffff:169.254.169.254) to its IPv4 form.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsDisallowedV4(address.GetAddressBytes());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 forms that embed an IPv4 address (6to4, NAT64, IPv4-compatible) must be
            // decoded and re-checked, or an internal IPv4 could hide inside an IPv6 literal.
            if (TryExtractEmbeddedV4(address.GetAddressBytes(), out var embedded))
            {
                return IsDisallowedV4(embedded);
            }

            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || address.IsIPv6UniqueLocal; // fc00::/7
        }

        // Unknown address family: fail closed.
        return true;
    }

    private static bool IsDisallowedV4(byte[] b)
    {
        return b[0] == 0                                   // 0.0.0.0/8 "this network"
            || b[0] == 10                                  // 10.0.0.0/8 private
            || b[0] == 127                                 // 127.0.0.0/8 loopback
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64.0.0/10 CGNAT (+ 100.100.x metadata)
            || (b[0] == 169 && b[1] == 254)                // 169.254.0.0/16 link-local / metadata
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16.0.0/12 private
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24 IETF protocol assignments
            || (b[0] == 192 && b[1] == 0 && b[2] == 2)     // 192.0.2.0/24 TEST-NET-1
            || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16 private
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18.0.0/15 benchmarking
            || (b[0] == 198 && b[1] == 51 && b[2] == 100)  // 198.51.100.0/24 TEST-NET-2
            || (b[0] == 203 && b[1] == 0 && b[2] == 113)   // 203.0.113.0/24 TEST-NET-3
            || b[0] >= 224;                                // 224.0.0.0/4 multicast + 240/4 reserved
    }

    private static bool TryExtractEmbeddedV4(byte[] b, out byte[] v4)
    {
        // 6to4: 2002:V4::/16 — the IPv4 is bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            v4 = new[] { b[2], b[3], b[4], b[5] };
            return true;
        }

        // NAT64 well-known prefix: 64:ff9b::/96 — the IPv4 is the last 4 bytes.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b &&
            b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0 &&
            b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
        {
            v4 = new[] { b[12], b[13], b[14], b[15] };
            return true;
        }

        // IPv4-compatible (deprecated) ::a.b.c.d — first 12 bytes zero, IPv4 in the last 4.
        for (var i = 0; i < 12; i++)
        {
            if (b[i] != 0)
            {
                v4 = Array.Empty<byte>();
                return false;
            }
        }

        v4 = new[] { b[12], b[13], b[14], b[15] };
        return true;
    }
}
