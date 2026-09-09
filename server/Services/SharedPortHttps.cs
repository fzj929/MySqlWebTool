using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace DataPilot.Api.Services;

public static class SharedPortHttps
{
    public static void UseHttpOrHttps(this ListenOptions endpoint, X509Certificate2 certificate)
    {
        // Keep the HTTP parser shared, but route TLS records through UseHttps first.
        // The application redirects every plaintext request before any endpoint executes.
        endpoint.Protocols = HttpProtocols.Http1;
        ConnectionDelegate? plaintext = null;
        endpoint.Use(tls => async connection =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var input = connection.Transport.Input;
                var read = await input.ReadAsync(timeout.Token);
                var buffer = read.Buffer;
                if (read.IsCanceled || buffer.IsEmpty)
                {
                    input.AdvanceTo(buffer.Start, buffer.End);
                    return;
                }
                var isTls = buffer.Slice(0, 1).ToArray()[0] == 0x16;
                // Consume nothing: the TLS handler or HTTP parser must see the first byte.
                input.AdvanceTo(buffer.Start, buffer.Start);
                await (isTls ? tls : plaintext!)(connection);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                connection.Abort();
            }
        });
        endpoint.UseHttps(certificate);
        endpoint.Use(next => { plaintext = next; return next; });
    }
}
