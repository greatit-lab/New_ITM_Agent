using ITM_Agent.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

namespace ITM_Agent.Core
{
    public readonly struct Metric
    {
        public DateTime Timestamp { get; }
        public float Cpu { get; }
        public float Mem { get; }

        public Metric(float cpu, float mem)
        {
            Timestamp = DateTime.Now;
            Cpu = cpu;
            Mem = mem;
        }
    }

    /// <summary>
    /// CPU 및 메모리 사용률을 주기적으로 수집하는 싱글턴 서비스입니다.
    /// </summary>
    public sealed class PerformanceMonitor : IDisposable
    {
        private static readonly Lazy<PerformanceMonitor> _instance = new Lazy<PerformanceMonitor>(() => new PerformanceMonitor());
        public static PerformanceMonitor Instance => _instance.Value;
        
        private ILogger _logger;
        private PdhSampler _sampler;
        private Timer _flushTimer;
        private readonly ConcurrentQueue<Metric> _buffer = new ConcurrentQueue<Metric>();
        private const int FLUSH_INTERVAL_MS = 30_000;
        private const int BULK_COUNT = 60;
        private bool _isFileLoggingEnabled = false;
        private bool _isSampling = false;

        private PerformanceMonitor()
        {
            _sampler = new PdhSampler();
            _sampler.OnSample += OnSampleReceived;
            _sampler.OnThresholdExceeded += () => _sampler.IntervalMs = 1_000; // 과부하 시 1초 간격
            _sampler.OnBackToNormal += () => _sampler.IntervalMs = 5_000; // 평상 시 5초 간격
            
            _flushTimer = new Timer(_ => FlushToFile(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Initialize(ILogger logger)
        {
            _logger = logger;
            _sampler.Initialize(logger);
        }

        public void StartSampling()
        {
            if (_isSampling) return;
            _isSampling = true;
            _sampler.Start();
            _logger?.Event("[PerformanceMonitor] Sampling started.");
        }

        public void StopSampling()
        {
            if (!_isSampling) return;
            _sampler.Stop();
            SetFileLogging(false); // 파일 로깅도 중지
            _isSampling = false;
            _logger?.Event("[PerformanceMonitor] Sampling stopped.");
        }

        public void SetFileLogging(bool enable)
        {
            if (enable && !_isFileLoggingEnabled)
            {
                Directory.CreateDirectory(GetLogDir());
                _flushTimer.Change(FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
                _isFileLoggingEnabled = true;
                _logger?.Event("[PerformanceMonitor] File logging enabled.");
            }
            else if (!enable && _isFileLoggingEnabled)
            {
                _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
                FlushToFile(); // 남은 버퍼 기록 후 중지
                _isFileLoggingEnabled = false;
                _logger?.Event("[PerformanceMonitor] File logging disabled.");
            }
        }

        public void RegisterConsumer(Action<Metric> consumer) => _sampler.OnSample += consumer;
        public void UnregisterConsumer(Action<Metric> consumer) => _sampler.OnSample -= consumer;

        private void OnSampleReceived(Metric m)
        {
            _buffer.Enqueue(m);
            if (_isFileLoggingEnabled && _buffer.Count >= BULK_COUNT)
            {
                FlushToFile();
            }
        }

        private void FlushToFile()
        {
            if (!_isFileLoggingEnabled || _buffer.IsEmpty) return;

            string filePath = Path.Combine(GetLogDir(), $"{DateTime.Now:yyyyMMdd}_performance.log");
            
            var metricsToFlush = new List<Metric>();
            while (_buffer.TryDequeue(out var metric))
            {
                metricsToFlush.Add(metric);
            }

            if (metricsToFlush.Any())
            {
                try
                {
                    var logLines = metricsToFlush.Select(m => $"{m.Timestamp:yyyy-MM-dd HH:mm:ss.fff} C:{m.Cpu:F2} M:{m.Mem:F2}");
                    File.AppendAllLines(filePath, logLines);
                }
                catch(Exception ex)
                {
                    _logger?.Error($"[PerformanceMonitor] Failed to flush performance log: {ex.Message}");
                }
            }
        }
        
        private static string GetLogDir() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public void Dispose()
        {
            _sampler?.Dispose();
            _flushTimer?.Dispose();
        }
    }

    /// <summary>
    /// PerformanceCounter 기반의 경량 샘플러 클래스
    /// </summary>
    internal sealed class PdhSampler : IDisposable
    {
        public event Action<Metric> OnSample;
        public event Action OnThresholdExceeded;
        public event Action OnBackToNormal;

        private ILogger _logger;
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _memFreeMbCounter;
        private float _totalMemMb;
        private Timer _timer;
        private bool _isOverloaded;
        
        public int IntervalMs { get; set; } = 5000;

        public void Initialize(ILogger logger)
        {
            _logger = logger;
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memFreeMbCounter = new PerformanceCounter("Memory", "Available MBytes");
                _totalMemMb = GetTotalMemoryMb();
                _cpuCounter.NextValue(); 
            }
            catch (Exception ex)
            {
                _logger?.Error($"[PdhSampler] Failed to initialize performance counters: {ex.Message}. Performance monitoring will be disabled.");
                _cpuCounter = null;
            }
        }

        public void Start()
        {
            if (_cpuCounter == null) return;
            _timer?.Dispose();
            _timer = new Timer(_ => Sample(), null, 0, IntervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void Sample()
        {
            if (_cpuCounter == null) return;

            try
            {
                float cpuVal = _cpuCounter.NextValue();
                float freeMb = _memFreeMbCounter.NextValue();
                
                float usedRatio = (_totalMemMb > 0) ? ((_totalMemMb - freeMb) / _totalMemMb) * 100f : 0f;
                OnSample?.Invoke(new Metric(cpuVal, usedRatio));

                bool isCurrentlyOver = (cpuVal > 75f) || (usedRatio > 80f);
                if (isCurrentlyOver && !_isOverloaded)
                {
                    _isOverloaded = true;
                    OnThresholdExceeded?.Invoke();
                }
                else if (!isCurrentlyOver && _isOverloaded)
                {
                    _isOverloaded = false;
                    OnBackToNormal?.Invoke();
                }
            }
            catch(Exception ex)
            {
                _logger?.Error($"[PdhSampler] Failed during sampling: {ex.Message}");
                Stop();
            }
        }
        
        private float GetTotalMemoryMb()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    var mos = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if(mos != null)
                    {
                        return Convert.ToSingle(mos["TotalVisibleMemorySize"]) / 1024f;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"[PdhSampler] Failed to get total memory size: {ex.Message}");
            }
            return 0f;
        }
        
        public void Dispose()
        {
            _timer?.Dispose();
            _cpuCounter?.Dispose();
            _memFreeMbCounter?.Dispose();
        }
    }
}
