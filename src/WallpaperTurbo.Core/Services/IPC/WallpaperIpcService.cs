using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.Core.Services.IPC;

/// <summary>
/// Provides asynchronous, binary length-prefixed named pipe IPC server and client services.
/// </summary>
public static class WallpaperIpcService
{
    private const string PipeName = "WallpaperTurbo_IPC_Pipe";

    /// <summary>
    /// Starts the IPC server background loop to listen for client command contracts.
    /// </summary>
    public static void StartServer(Func<IpcCommand, Task<IpcResponse>> onCommandReceived, CancellationToken token)
    {
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Create server instance for accepting incoming connections
                    var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    // Wait for connection
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    // Connection accepted. Spin off a background handler to serve this client
                    // and immediately loop back to wait for the next client on a fresh server stream instance.
                    _ = Task.Run(async () =>
                    {
                        using (server)
                        {
                            try
                            {
                                // Apply a defensive timeout on client requests
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                                cts.CancelAfter(5000); // 5-second maximum request timeout

                                IpcCommand? command = await ReadFramedMessageAsync<IpcCommand>(server, cts.Token).ConfigureAwait(false);
                                if (command != null)
                                {
                                    IpcResponse response = await onCommandReceived(command).ConfigureAwait(false);
                                    await WriteFramedMessageAsync(server, response, cts.Token).ConfigureAwait(false);
                                    
                                    try
                                    {
                                        server.WaitForPipeDrain();
                                    }
                                    catch
                                    {
                                        // Ignore issues draining when client closes aggressively
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                try { Console.Error.WriteLine($"[IPC Server Client Handler] Error: {ex.Message}"); } catch { }
                            }
                        }
                    }, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    try { Console.Error.WriteLine($"[IPC Server] Main loop exception: {ex.Message}"); } catch { }
                    // Prevent hot loop on server creation errors
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    /// <summary>
    /// Connects as a client to the IPC server, sends a command, and returns the response.
    /// </summary>
    public static async Task<IpcResponse?> SendCommandAsync(IpcCommand command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);
            
            await client.ConnectAsync(cts.Token).ConfigureAwait(false);

            await WriteFramedMessageAsync(client, command, cts.Token).ConfigureAwait(false);

            IpcResponse? response = await ReadFramedMessageAsync<IpcResponse>(client, cts.Token).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[IPC Client] Connection failed: {ex.Message}"); } catch { }
        }

        return null;
    }

    /// <summary>
    /// Serializes and writes a length-prefixed framed message to the stream.
    /// </summary>
    private static async Task WriteFramedMessageAsync(Stream stream, object payload, CancellationToken token)
    {
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] lengthPrefix = BitConverter.GetBytes(payloadBytes.Length);
        
        await stream.WriteAsync(lengthPrefix, 0, 4, token).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a length-prefixed framed message and deserializes it.
    /// </summary>
    private static async Task<T?> ReadFramedMessageAsync<T>(Stream stream, CancellationToken token) where T : class
    {
        byte[] lengthPrefix = new byte[4];
        int prefixRead = await ReadExactAsync(stream, lengthPrefix, 4, token).ConfigureAwait(false);
        if (prefixRead < 4)
        {
            return null; // EOF
        }

        int length = BitConverter.ToInt32(lengthPrefix, 0);
        if (length <= 0 || length > 10 * 1024 * 1024) // 10 MB sanity limit
        {
            throw new InvalidDataException($"Invalid IPC frame length: {length} bytes.");
        }

        byte[] buffer = new byte[length];
        int payloadRead = await ReadExactAsync(stream, buffer, length, token).ConfigureAwait(false);
        if (payloadRead < length)
        {
            return null; // Incomplete payload
        }

        return JsonSerializer.Deserialize<T>(buffer);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the stream.
    /// </summary>
    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, token).ConfigureAwait(false);
            if (read == 0)
            {
                break; // EOF
            }
            totalRead += read;
        }
        return totalRead;
    }
}
