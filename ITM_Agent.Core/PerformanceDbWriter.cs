using ITM_Agent.Common;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;

namespace ITM_Agent.Core
{
    public sealed class PerformanceDbWriter : IDisposable
    {
        private static PerformanceDbWriter _currentInstance;
        private static readonly object _lock = new object();

        private readonly string _eqpid;
        private readonly ConcurrentQueue<Metric> _buffer = new ConcurrentQueue<Metric>();
        private readonly Timer _timer;
        private readonly ILogger _logger;
        private readonly DatabaseRepository _dbRepository;
        private const int BULK_COUNT = 60;
        private const int FLUSH_INTERVAL_MS = 30_000;

        private PerformanceDbWriter(string eqpid, ILogger logger, DatabaseRepository dbRepository)
        {
            _eqpid = eqpid.StartsWith("Eqpid:", StringComparison.OrdinalIgnoreCase) ? eqpid.Substring(6).Trim() : eqpid.Trim();
            _logger = logger;
            _dbRepository = dbRepository;
            
            PerformanceMonitor.Instance.RegisterConsumer(OnSampleReceived);
            _timer = new Timer(_ => FlushToDb(), null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
        }

        public static void Start(string eqpid, ILogger logger, DatabaseRepository dbRepository)
        {
            lock (_lock)
            {
                if (_currentInstance != null) return;
                
                PerformanceMonitor.Instance.Initialize(logger);
                PerformanceMonitor.Instance.StartSampling();
                
                _currentInstance = new PerformanceDbWriter(eqpid, logger, dbRepository);
                logger.Event("[PerformanceDbWriter] Service started.");
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                if (_currentInstance == null) return;
                
                _currentInstance.Dispose();
                _currentInstance = null;
            }
        }

        private void OnSampleReceived(Metric m)
        {
            _buffer.Enqueue(m);
            if (_buffer.Count >= BULK_COUNT)
            {
                FlushToDb();
            }
        }

        private void FlushToDb()
        {
            var batch = new List<Metric>();
            while (_buffer.TryDequeue(out var metric))
            {
                batch.Add(metric);
            }

            if (!batch.Any()) return;

            try
            {
                var dt = new DataTable();
                dt.Columns.Add("eqpid", typeof(string));
                dt.Columns.Add("ts", typeof(DateTime));
                dt.Columns.Add("serv_ts", typeof(DateTime));
                dt.Columns.Add("cpu_usage", typeof(float));
                dt.Columns.Add("mem_usage", typeof(float));
                
                foreach (var m in batch)
                {
                    var ts = new DateTime(m.Timestamp.Year, m.Timestamp.Month, m.Timestamp.Day, m.Timestamp.Hour, m.Timestamp.Minute, m.Timestamp.Second);
                    var srv_ts = TimeSyncProvider.Instance.ToSynchronizedKst(ts);
                    
                    float cpuUsage = (float)Math.Round(m.Cpu, 2);
                    if (cpuUsage == 0.0f && m.Cpu > 0.0f) cpuUsage = 0.01f;

                    dt.Rows.Add(_eqpid, ts, srv_ts, cpuUsage, (float)Math.Round(m.Mem, 2));
                }
                
                 const string sql = @"
                    INSERT INTO public.eqp_perf (eqpid, ts, serv_ts, cpu_usage, mem_usage)
                    VALUES (@eqpid, @ts, @serv_ts, @cpu, @mem)
                    ON CONFLICT (eqpid, ts) DO NOTHING;";

                foreach(DataRow row in dt.Rows)
                {
                    var parameters = new[]
                    {
                        new NpgsqlParameter("@eqpid", row["eqpid"]),
                        new NpgsqlParameter("@ts", row["ts"]),
                        new NpgsqlParameter("@serv_ts", row["serv_ts"]),
                        new NpgsqlParameter("@cpu", row["cpu_usage"]),
                        new NpgsqlParameter("@mem", row["mem_usage"])
                    };
                    _dbRepository.ExecuteNonQuery(sql, parameters);
                }
                 _logger.Debug($"[PerformanceDbWriter] Flushed {batch.Count} performance metrics to the database.");

            }
            catch (Exception ex)
            {
                _logger.Error($"[PerformanceDbWriter] Failed to flush batch to database: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _logger.Event("[PerformanceDbWriter] Service stopping.");
            PerformanceMonitor.Instance.UnregisterConsumer(OnSampleReceived);
            PerformanceMonitor.Instance.StopSampling();
            _timer?.Dispose();
            FlushToDb(); // 마지막 남은 데이터 처리
        }
    }
}
