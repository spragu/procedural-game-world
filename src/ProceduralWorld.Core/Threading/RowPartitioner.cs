namespace ProceduralWorld.Core.Threading;

/// <summary>
/// Splits row ranges across dedicated threads.
///
/// This exists because <see cref="Parallel.For(int, int, Action{int})"/> is a poor
/// fit for this workload on WebAssembly. The .NET thread pool injects new workers
/// on a timer (roughly one per second) rather than all at once, so a burst of work
/// lasting a few seconds finishes almost entirely on the starting thread. Measured
/// on a 4096x4096 world in Blazor: Parallel.For took 179 s, which is essentially
/// the serial time.
///
/// Creating threads explicitly sidesteps the injection heuristic. The cost of
/// spawning a handful of threads is irrelevant next to passes that run for seconds,
/// and the partitioning is contiguous so each thread gets a cache-friendly block.
/// </summary>
public static class RowPartitioner
{
    /// <summary>Below this many rows the fork/join overhead outweighs the work.</summary>
    private const int MinRowsToParallelise = 64;

    /// <summary>
    /// Invokes <paramref name="body"/> for every row in [0, <paramref name="rowCount"/>),
    /// splitting the range across up to <paramref name="maxDegree"/> threads.
    /// </summary>
    /// <remarks>
    /// Must not be called from the WebAssembly main thread: it blocks while joining,
    /// which that thread is not allowed to do. Callers should already be inside a
    /// <see cref="Task.Run(Action)"/>.
    /// </remarks>
    public static void For(int rowCount, int maxDegree, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (rowCount <= 0) return;

        int threads = Math.Clamp(maxDegree, 1, Environment.ProcessorCount);
        if (threads == 1 || rowCount < MinRowsToParallelise)
        {
            for (int y = 0; y < rowCount; y++) body(y);
            return;
        }

        threads = Math.Min(threads, rowCount);

        int chunk = rowCount / threads;
        int remainder = rowCount % threads;

        var workers = new Thread[threads - 1];
        Exception? failure = null;
        int start = 0;

        for (int t = 0; t < threads; t++)
        {
            // Spread the remainder over the first few chunks so no thread is a row
            // behind everyone else at the join.
            int size = chunk + (t < remainder ? 1 : 0);
            int from = start;
            int to = start + size;
            start = to;

            void Run()
            {
                try
                {
                    for (int y = from; y < to; y++) body(y);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }
            }

            if (t == threads - 1)
            {
                // Run the final chunk on the calling thread rather than idling it.
                Run();
            }
            else
            {
                var thread = new Thread(Run, 512 * 1024) { IsBackground = true };
                workers[t] = thread;
                thread.Start();
            }
        }

        foreach (var thread in workers) thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("A parallel row pass failed.", failure);
    }
}
