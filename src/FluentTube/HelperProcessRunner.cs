using System.Diagnostics;
namespace FluentTube;

internal sealed record HelperResult(int ExitCode, string Output, string Error);
internal sealed class HelperProcessRunner : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<Process> running = [];
    private readonly CancellationTokenSource shutdown = new();
    private bool stopped;
    private bool cancellationFinished;
    internal async Task<HelperResult> Run(ProcessStartInfo start, CancellationToken cancellation)
    {
        using var process = new Process { StartInfo = start };
        CancellationToken lifetime;
        lock (gate)
        {
            if (stopped) throw new OperationCanceledException("Playback helpers have stopped.");
            cancellation.ThrowIfCancellationRequested();
            lifetime = shutdown.Token;
            process.Start();
            running.Add(process);
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                Kill(process);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(3));
                if (cancellation.IsCancellationRequested || lifetime.IsCancellationRequested) throw;
                throw new InvalidOperationException("YouTube took too long to respond. Check your connection and try again.");
            }
            return new(process.ExitCode, await output, await errors);
        }
        finally
        {
            lock (gate)
            {
                running.Remove(process);
                if (cancellationFinished && running.Count == 0) shutdown.Dispose();
            }
        }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { System.Diagnostics.Debug.WriteLine(error); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
        }
        shutdown.Cancel();
        lock (gate)
        {
            cancellationFinished = true;
            foreach (var process in running) Kill(process);
            if (running.Count == 0) shutdown.Dispose();
        }
    }
}
