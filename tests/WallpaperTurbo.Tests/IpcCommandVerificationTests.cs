using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WallpaperTurbo.Tests;

public class IpcCommandVerificationTests
{
    [Fact]
    public async Task IpcPipe_ShouldCommunicateBidirectionally()
    {
        string pipeName = "WallpaperTurbo_Test_IPC_" + Guid.NewGuid().ToString();
        using var cts = new CancellationTokenSource(5000);

        // Start local Named Pipe Server simulating AppRunner
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cts.Token);

                var reader = new StreamReader(server);
                var writer = new StreamWriter(server) { AutoFlush = true };

                string? cmd = await reader.ReadLineAsync(cts.Token);
                if (cmd == "swap 2")
                {
                    await writer.WriteLineAsync("success");
                }
                else
                {
                    await writer.WriteLineAsync("error: unknown command");
                }
            }
            catch
            {
                // Silently finish if token cancelled or connection failed
            }
        });

        // Simulating the UI Client side SendIpcCommandAsync
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, cts.Token);
        var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("swap 2");

        var reader = new StreamReader(client);
        string? response = await reader.ReadLineAsync(cts.Token);

        Assert.Equal("success", response);

        await serverTask;
    }
}
