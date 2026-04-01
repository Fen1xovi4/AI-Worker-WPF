using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WpfBrowserWorker.Helpers;

/// <summary>
/// Local HTTP CONNECT proxy that tunnels all connections through an
/// authenticated remote SOCKS5 proxy. Bind to 127.0.0.1:auto-port, then
/// point Chrome to --proxy-server=http://127.0.0.1:{LocalPort}.
/// Chrome's HTTP CONNECT support is rock-solid; this avoids all SOCKS5
/// command-line auth limitations.  Dispose to shut down the relay.
/// </summary>
internal sealed class HttpConnectRelay : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _remoteHost;
    private readonly int _remotePort;
    private readonly string? _username;
    private readonly string? _password;
    private readonly CancellationTokenSource _cts = new();

    public int LocalPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private int _connectionCount;
    /// <summary>Number of connections Chrome (or any client) has made to this relay.</summary>
    public int ConnectionCount => _connectionCount;

    public HttpConnectRelay(string remoteHost, int remotePort, string? username, string? password)
    {
        _remoteHost = remoteHost;
        _remotePort = remotePort;
        _username   = username;
        _password   = password;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client);
            }
            catch { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        Interlocked.Increment(ref _connectionCount);
        try
        {
            using var _ = client;
            var cs = client.GetStream();

            // ── Step 1: read HTTP request headers until \r\n\r\n ──────────
            var headerBytes = await ReadUntilDoubleNewlineAsync(cs);
            var headerText  = Encoding.ASCII.GetString(headerBytes);

            // First line: "CONNECT host:port HTTP/1.x"
            var firstLine = headerText.Split('\n')[0].Trim();
            if (!firstLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            {
                // Non-CONNECT (plain HTTP) — not supported; send 405
                var reject = Encoding.ASCII.GetBytes("HTTP/1.1 405 Method Not Allowed\r\n\r\n");
                await cs.WriteAsync(reject);
                return;
            }

            var parts = firstLine.Split(' ');   // ["CONNECT", "host:port", "HTTP/1.x"]
            var hostPort = parts[1];
            var colonIdx = hostPort.LastIndexOf(':');
            var targetHost = hostPort[..colonIdx];
            var targetPort = int.Parse(hostPort[(colonIdx + 1)..]);

            // ── Step 2: connect to remote SOCKS5 proxy ────────────────────
            var remote = new TcpClient();
            await remote.ConnectAsync(_remoteHost, _remotePort);
            using var __ = remote;
            var rs = remote.GetStream();

            var buf = new byte[512];

            // Offer username/password auth (method 2) if we have creds
            bool hasAuth = _username is not null;
            await rs.WriteAsync(hasAuth ? new byte[] { 5, 1, 2 } : new byte[] { 5, 1, 0 });
            await ReadExactAsync(rs, buf, 2);   // server choice

            if (buf[1] == 2 && hasAuth)
            {
                // ── username/password sub-negotiation (RFC 1929) ──────────
                var user = Encoding.ASCII.GetBytes(_username!);
                var pass = Encoding.ASCII.GetBytes(_password ?? "");
                var authReq = new byte[3 + user.Length + pass.Length];
                authReq[0] = 1;
                authReq[1] = (byte)user.Length;
                user.CopyTo(authReq, 2);
                authReq[2 + user.Length] = (byte)pass.Length;
                pass.CopyTo(authReq, 3 + user.Length);
                await rs.WriteAsync(authReq);
                await ReadExactAsync(rs, buf, 2);   // auth response
                if (buf[1] != 0)
                {
                    var fail = Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");
                    await cs.WriteAsync(fail);
                    return;
                }
            }

            // ── Step 3: SOCKS5 CONNECT for target ─────────────────────────
            byte[] connectReq;
            var domainBytes = Encoding.ASCII.GetBytes(targetHost);
            if (IPAddress.TryParse(targetHost, out var ip))
            {
                var ipBytes = ip.GetAddressBytes();
                connectReq = new byte[10];
                connectReq[0] = 5; connectReq[1] = 1; connectReq[2] = 0; connectReq[3] = 1;
                ipBytes.CopyTo(connectReq, 4);
                connectReq[8] = (byte)(targetPort >> 8);
                connectReq[9] = (byte)(targetPort & 0xFF);
            }
            else
            {
                connectReq = new byte[7 + domainBytes.Length];
                connectReq[0] = 5; connectReq[1] = 1; connectReq[2] = 0; connectReq[3] = 3;
                connectReq[4] = (byte)domainBytes.Length;
                domainBytes.CopyTo(connectReq, 5);
                connectReq[5 + domainBytes.Length] = (byte)(targetPort >> 8);
                connectReq[6 + domainBytes.Length] = (byte)(targetPort & 0xFF);
            }
            await rs.WriteAsync(connectReq);

            // Read SOCKS5 reply
            await ReadExactAsync(rs, buf, 4);
            var rep     = buf[1];
            var repAtyp = buf[3];
            // Skip bound address returned by remote proxy
            if      (repAtyp == 1) await ReadExactAsync(rs, buf, 6);
            else if (repAtyp == 3) { await ReadExactAsync(rs, buf, 1); await ReadExactAsync(rs, buf, buf[0] + 2); }
            else                   await ReadExactAsync(rs, buf, 18); // IPv6

            if (rep != 0)
            {
                var fail = Encoding.ASCII.GetBytes($"HTTP/1.1 502 Bad Gateway\r\n\r\n");
                await cs.WriteAsync(fail);
                return;
            }

            // ── Step 4: tell Chrome the tunnel is ready ────────────────────
            var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await cs.WriteAsync(ok);

            // ── Step 5: bidirectional pipe ─────────────────────────────────
            var t1 = cs.CopyToAsync(rs, _cts.Token);
            var t2 = rs.CopyToAsync(cs, _cts.Token);
            await Task.WhenAny(t1, t2);
        }
        catch { /* connection closed or proxy error */ }
    }

    /// <summary>Reads bytes until the \r\n\r\n header terminator is found.</summary>
    private static async Task<byte[]> ReadUntilDoubleNewlineAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(512);
        var oneByte = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(oneByte);
            if (n == 0) break;
            buffer.Add(oneByte[0]);
            if (buffer.Count >= 4)
            {
                var t = buffer.Count;
                if (buffer[t-4] == '\r' && buffer[t-3] == '\n' &&
                    buffer[t-2] == '\r' && buffer[t-1] == '\n')
                    break;
            }
        }
        return buffer.ToArray();
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buf, int count)
    {
        var read = 0;
        while (read < count)
            read += await stream.ReadAsync(buf.AsMemory(read, count - read));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
