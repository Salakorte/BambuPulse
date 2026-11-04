using System.Buffers.Binary;
using System.Drawing;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

sealed class LanCameraStream
{
    const int HeaderSize = 16;
    const int MaxFrameSize = 8 * 1024 * 1024;
    readonly string host;
    readonly string accessCode;

    public LanCameraStream(string host, string accessCode)
    {
        this.host = host;
        this.accessCode = accessCode;
    }

    public async Task RunAsync(Action<Bitmap> onFrame, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await tcp.ConnectAsync(host, 6000, connectTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("La cámara LAN no respondió en 10 segundos.");
        }
        await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.None
        }, cancellationToken);

        await tls.WriteAsync(CreateLoginPacket(), cancellationToken);
        var header = new byte[HeaderSize];
        while (true)
        {
            using var frameTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            frameTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await ReadExactlyAsync(tls, header, frameTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("La cámara LAN no envió imágenes en 20 segundos.");
            }
            var frameSize = header[0] | header[1] << 8 | header[2] << 16;
            if (frameSize <= 0 || frameSize > MaxFrameSize)
                throw new InvalidDataException($"La cámara LAN envió un tamaño de imagen no válido ({frameSize} bytes).");

            var jpeg = new byte[frameSize];
            try
            {
                await ReadExactlyAsync(tls, jpeg, frameTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("La cámara LAN interrumpió la recepción de una imagen.");
            }
            if (jpeg.Length < 2 || jpeg[0] != 0xff || jpeg[1] != 0xd8)
                throw new InvalidDataException("La cámara LAN envió un fotograma que no es JPEG.");

            using var imageStream = new MemoryStream(jpeg, writable: false);
            using var decoded = Image.FromStream(imageStream);
            onFrame(new Bitmap(decoded));
        }
    }

    byte[] CreateLoginPacket()
    {
        var packet = new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), 0x3000);
        WriteField(packet.AsSpan(16, 32), "bblp");
        WriteField(packet.AsSpan(48, 32), accessCode);
        return packet;
    }

    static void WriteField(Span<byte> destination, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length)).CopyTo(destination);
    }

    static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("La impresora cerró la conexión de cámara LAN.");
            offset += read;
        }
    }
}
