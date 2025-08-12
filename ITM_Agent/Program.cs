using ITM_Agent.Common;
using ITM_Agent.Core;
using ITM_Agent.Core.Startup;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ITM_Agent
{
    internal static class Program
    {
        private static Mutex mutex = null;
        private const string appGuid = "c0a76b5a-12ab-45c5-b9d9-d693faa6e7b9"; // 프로그램 고유 ID

        [STAThread]
        static void Main()
        {
            // --- 1. 중복 실행 방지 ---
            mutex = new Mutex(true, appGuid, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("ITM Agent가 이미 실행 중입니다.", "실행 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --- 2. 파일 동기화 (타이밍 문제 해결) ---
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string solutionDir = Path.GetFullPath(Path.Combine(exeDir, @"..\..\")); 
            string sourceIniPath = Path.Combine(solutionDir, "Settings.ini");
            string destIniPath = Path.Combine(exeDir, "Settings.ini");

            try
            {
                if (File.Exists(sourceIniPath))
                {
                    if (!File.Exists(destIniPath) || File.GetLastWriteTime(sourceIniPath) > File.GetLastWriteTime(destIniPath))
                    {
                        File.Copy(sourceIniPath, destIniPath, true);
                    }
                }
            }
            catch { /* 복사 실패는 무시하고 진행 */ }

            // --- 3. 공통 및 핵심 서비스 생성 ---
            var logger = SharedLogManager.Instance;
            var dbRepository = new DatabaseRepository(logger);
            TimeSyncProvider.Instance.Initialize(logger);
            
            var settingsManager = new SettingsManager(destIniPath, logger); 
            
            bool isDebugMode = settingsManager.GetValue("Option", "EnableDebugLog") == "1";
            logger.SetDebugMode(isDebugMode);
            
            var fileProcessingService = new FileProcessingService(logger);

            // --- 4. 성능 최적화 실행 ---
            PerformanceWarmUp.Run(logger, dbRepository);

            // --- 5. MainForm에 모든 서비스 주입 후 실행 ---
            try
            {
                var mainForm = new MainForm(
                    logger,
                    settingsManager,
                    dbRepository,
                    fileProcessingService
                );
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                logger.Error($"An unhandled exception occurred at the application's entry point: {ex.ToString()}");
                MessageBox.Show("프로그램에 치명적인 오류가 발생하여 종료됩니다. 로그 파일을 확인해주세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 프로그램 종료 시 Mutex 해제
                mutex.ReleaseMutex();
            }
        }
    }
}
