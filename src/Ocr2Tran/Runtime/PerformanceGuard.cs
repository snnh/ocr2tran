using System.Diagnostics;
using Ocr2Tran.App;

namespace Ocr2Tran.Runtime;

public sealed class PerformanceGuard
{
    private readonly PerformanceSettings _settings;

    public PerformanceGuard(PerformanceSettings settings)
    {
        _settings = settings;
    }

    public void Apply()
    {
        var threads = Math.Max(1, _settings.CpuThreads);
        ThreadPool.SetMinThreads(threads, threads);
        ThreadPool.SetMaxThreads(Math.Max(threads, Environment.ProcessorCount), Math.Max(threads, Environment.ProcessorCount));
        Environment.SetEnvironmentVariable("OMP_NUM_THREADS", threads.ToString());
        Environment.SetEnvironmentVariable("MKL_NUM_THREADS", threads.ToString());
        Environment.SetEnvironmentVariable("OPENBLAS_NUM_THREADS", threads.ToString());
        Environment.SetEnvironmentVariable("FLAGS_paddle_num_threads", threads.ToString());

        if (string.IsNullOrWhiteSpace(_settings.CpuAffinityMask))
        {
            return;
        }

        try
        {
            var mask = _settings.CpuAffinityMask.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(_settings.CpuAffinityMask[2..], 16)
                : Convert.ToInt64(_settings.CpuAffinityMask);
            Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(mask);
        }
        catch
        {
            // Invalid affinity should not prevent the tray tool from starting.
        }
    }

    public void TrimIfOverSoftLimit()
    {
        if (_settings.MemorySoftLimitMb <= 0)
        {
            return;
        }

        var process = Process.GetCurrentProcess();
        var currentMb = Math.Max(GC.GetTotalMemory(false), process.PrivateMemorySize64) / 1024 / 1024;
        if (currentMb >= _settings.MemorySoftLimitMb)
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: false, compacting: true);
        }
    }
}
