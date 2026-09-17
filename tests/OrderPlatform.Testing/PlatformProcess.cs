using System.Diagnostics;
using System.Text;

namespace OrderPlatform.Testing;

/// <summary>
/// Runs a built platform executable (<c>OrderPlatform.Migrator.dll</c>, <c>OrderPlatform.Api.dll</c>) as a separate process,
/// exactly as a container or deployment job would: configuration through environment variables, exit code as the result.
/// </summary>
public sealed class PlatformProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StringBuilder output = new();

    private PlatformProcess(string assemblyPath, IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet", [assemblyPath])
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Do not inherit test-runner settings such as ASPNETCORE_ENVIRONMENT.
        startInfo.Environment.Remove("ASPNETCORE_ENVIRONMENT");
        startInfo.Environment.Remove("DOTNET_ENVIRONMENT");
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, line) => Append(line.Data);
        process.ErrorDataReceived += (_, line) => Append(line.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string Output
    {
        get
        {
            lock (output)
            {
                return output.ToString();
            }
        }
    }

    public bool HasExited => process.HasExited;

    public int ExitCode => process.ExitCode;

    public static PlatformProcess Start(string assemblyPath, IReadOnlyDictionary<string, string?> environment) =>
        File.Exists(assemblyPath)
            ? new PlatformProcess(assemblyPath, environment)
            : throw new FileNotFoundException("Build the solution before running these tests.", assemblyPath);

    /// <summary>Runs the process to completion and returns it (exit code and output).</summary>
    public static async Task<PlatformProcess> RunAsync(string assemblyPath, IReadOnlyDictionary<string, string?> environment, TimeSpan timeout)
    {
        var platformProcess = Start(assemblyPath, environment);
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await platformProcess.process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            platformProcess.process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(assemblyPath)} did not exit within {timeout}. Output:\n{platformProcess.Output}");
        }

        // Flush the asynchronous output readers.
        await platformProcess.process.WaitForExitAsync(CancellationToken.None);
        return platformProcess;
    }

    /// <summary>Waits until the process exits or the URL answers 200, whichever happens first.</summary>
    public async Task<bool> WaitUntilHealthyAsync(Uri url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            try
            {
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            await Task.Delay(500);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        process.Dispose();
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }
}
