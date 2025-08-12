using ITM_Agent.Common;
using System;
using System.Diagnostics;
using System.Threading;

namespace ITM_Agent.Core.Startup
{
    /// <summary>
    /// 애플리케이션 시작 시 주요 구성 요소의 성능을 미리 최적화(Warm-Up)합니다.
    /// </summary>
    public static class PerformanceWarmUp
    {
        /// <summary>
        /// 성능 최적화 작업을 실행합니다. 이 메서드는 MainForm이 로드되기 전에 호출되어야 합니다.
        /// </summary>
        /// <param name="logger">로깅을 위한 ILogger 인스턴스</param>
        /// <param name="dbRepository">데이터베이스 연결 테스트를 위한 DatabaseRepository 인스턴스</param>
        public static void Run(ILogger logger, DatabaseRepository dbRepository)
        {
            logger.Event("[WarmUp] Starting performance warm-up tasks.");

            // 1) PDH(Performance Data Helper) 카운터 초기 호출
            // .NET의 PerformanceCounter는 첫 번째 값을 가져오는 데 시간이 걸릴 수 있으므로,
            // 미리 한 번 호출하여 실제 사용 시 지연이 없도록 합니다.
            try
            {
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue(); // 첫 호출은 0을 반환할 수 있음
                    Thread.Sleep(100);      // 약간의 딜레이 후
                    cpuCounter.NextValue(); // 유효한 값을 얻기 위한 두 번째 호출
                }
                logger.Debug("[WarmUp] Performance counters initialized.");
            }
            catch (Exception ex)
            {
                logger.Error($"[WarmUp] Failed to initialize performance counters: {ex.Message}. This may affect performance monitoring.");
            }

            // 2) 데이터베이스 연결 풀(Connection Pool) 예열
            // 프로그램에서 첫 DB 연결은 약간의 시간이 소요될 수 있습니다.
            // 미리 한 번 연결을 열었다 닫음으로써 연결 풀을 활성화하고, 실제 DB 작업 시의 응답 속도를 높입니다.
            try
            {
                // 간단히 스칼라 쿼리를 실행하여 연결을 테스트합니다.
                dbRepository.ExecuteScalar("SELECT 1");
                logger.Debug("[WarmUp] Database connection pool is ready.");
            }
            catch (Exception ex)
            {
                logger.Error($"[WarmUp] Database connection test failed: {ex.Message}. Please check connection info and network status.");
            }

            logger.Event("[WarmUp] Performance warm-up completed.");
        }
    }
}
