using Npgsql;
using System;
using System.Threading;

namespace ITM_Agent.Common
{
    /// <summary>
    /// 서버-PC 간의 시간 오차를 보정하고, 모든 시간을 한국 표준시(KST)로 변환하는
    /// 중앙 집중형 시간 동기화 클래스입니다.
    /// </summary>
    public sealed class TimeSyncProvider : IDisposable
    {
        private static readonly Lazy<TimeSyncProvider> _inst =
            new Lazy<TimeSyncProvider>(() => new TimeSyncProvider());
        public static TimeSyncProvider Instance => _inst.Value;

        private readonly object syncLock = new object();
        private TimeSpan clockDiff = TimeSpan.Zero; // 서버와 PC의 순수한 시간 차이
        private readonly TimeZoneInfo kstZone;
        private readonly Timer syncTimer;
        private ILogger _logger;

        private TimeSyncProvider()
        {
            // KST 타임존 정보 로드
            try
            {
                kstZone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows가 아닌 환경(Linux 등)을 위해 IANA 타임존 ID 사용
                kstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
            }
            
            // 앱 시작 시 즉시 동기화 후, 10분마다 주기적으로 서버 시간과 동기화
            syncTimer = new Timer(
                _ => SynchronizeWithServer(),
                null,
                TimeSpan.Zero,                // 즉시 실행
                TimeSpan.FromMinutes(10)      // 10분 간격
            );
        }

        // 로거를 외부에서 주입받아 사용
        public void Initialize(ILogger logger)
        {
            _logger = logger;
        }

        private void SynchronizeWithServer()
        {
            try
            {
                string cs = DatabaseInfo.CreateDefault().GetConnectionString();
                using (var conn = new NpgsqlConnection(cs))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("SELECT NOW() AT TIME ZONE 'UTC'", conn))
                    {
                        DateTime serverUtcTime = Convert.ToDateTime(cmd.ExecuteScalar());
                        DateTime clientUtcTime = DateTime.UtcNow;

                        lock (syncLock)
                        {
                            clockDiff = serverUtcTime - clientUtcTime;
                        }
                        _logger?.Debug($"Time synchronized. Difference is {clockDiff.TotalMilliseconds}ms.");
                    }
                }
            }
            catch (Exception ex)
            {
                // DB 연결 실패 시 기존 시간 차이 값을 유지하며 에러 로그 기록
                _logger?.Error($"Time synchronization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 장비의 로컬 시간을 서버 시간과 동기화한 후, 한국 표준시(KST)로 변환합니다.
        /// </summary>
        /// <param name="agentLocalTime">장비에서 발생한 로컬 시간</param>
        /// <returns>오차가 보정된 KST 시간</returns>
        public DateTime ToSynchronizedKst(DateTime agentLocalTime)
        {
            // 1. 입력된 시간을 UTC 시간으로 변환합니다.
            //    (DateTimeKind가 지정되지 않았다면 Local로 간주)
            DateTime agentUtcTime = agentLocalTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(agentLocalTime, DateTimeKind.Local).ToUniversalTime()
                : agentLocalTime.ToUniversalTime();

            // 2. 계산된 서버-장비 시간 오차를 더하여 시간을 동기화합니다.
            DateTime synchronizedUtcTime;
            lock (syncLock)
            {
                synchronizedUtcTime = agentUtcTime.Add(clockDiff);
            }

            // 3. 동기화된 UTC 시간을 KST로 최종 변환합니다.
            return TimeZoneInfo.ConvertTimeFromUtc(synchronizedUtcTime, kstZone);
        }

        public void Dispose()
        {
            syncTimer?.Dispose();
        }
    }
}
