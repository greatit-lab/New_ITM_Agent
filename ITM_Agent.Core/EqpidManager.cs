using ITM_Agent.Common;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Management; // NuGet: System.Management 필요

namespace ITM_Agent.Core
{
    /// <summary>
    /// Eqpid 및 관련 장비 정보를 관리하고 DB와 동기화하는 클래스입니다. UI 로직이 분리되었습니다.
    /// </summary>
    public class EqpidManager
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly DatabaseRepository _dbRepository;
        private readonly string _appVersion;

        private static readonly ConcurrentDictionary<string, TimeZoneInfo> _timezoneCache = new ConcurrentDictionary<string, TimeZoneInfo>();

        public EqpidManager(SettingsManager settings, ILogger logger, DatabaseRepository dbRepository, string appVersion)
        {
            _settingsManager = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbRepository = dbRepository ?? throw new ArgumentNullException(nameof(dbRepository));
            _appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
        }

        /// <summary>
        /// 설정 파일에서 EQPID를 확인하고, 없으면 null을 반환하여 UI 레이어의 입력을 요청합니다.
        /// </summary>
        /// <returns>저장된 EQPID 또는 null</returns>
        public string CheckAndGetEqpid()
        {
            _logger.Event("[EqpidManager] Checking for existing Eqpid.");
            string eqpid = _settingsManager.GetEqpid();
            if (string.IsNullOrEmpty(eqpid))
            {
                _logger.Event("[EqpidManager] Eqpid not found in settings. UI input required.");
                return null;
            }
            
            _logger.Event($"[EqpidManager] Eqpid found in settings: {eqpid}");
            return eqpid;
        }
        
        /// <summary>
        /// UI 레이어로부터 새로 입력받은 EQPID를 저장하고 DB에 업로드합니다.
        /// </summary>
        /// <param name="eqpid">사용자가 입력한 EQPID</param>
        /// <param name="type">사용자가 선택한 장비 타입</param>
        public void RegisterNewEqpid(string eqpid, string type)
        {
            if(string.IsNullOrWhiteSpace(eqpid) || string.IsNullOrWhiteSpace(type))
            {
                _logger.Error("[EqpidManager] Registration failed. EQPID or Type is empty.");
                return;
            }
            
            string upperEqpid = eqpid.ToUpper();
            _logger.Event($"[EqpidManager] Registering new Eqpid: {upperEqpid}, Type: {type}");
            _settingsManager.SetEqpid(upperEqpid);
            _settingsManager.SetEqpidType(type);
            
            UploadAgentInfoToDatabase(upperEqpid, type);
        }

        private void UploadAgentInfoToDatabase(string eqpid, string type)
        {
            try
            {
                const string sql = @"
                    INSERT INTO public.agent_info (eqpid, type, os, system_type, pc_name, locale, timezone, app_ver, reg_date, servtime)
                    VALUES (@eqpid, @type, @os, @arch, @pc_name, @loc, @tz, @app_ver, @pc_now, NOW()::timestamp(0))
                    ON CONFLICT (eqpid, pc_name) DO UPDATE SET
                        type = EXCLUDED.type,
                        os = EXCLUDED.os,
                        system_type = EXCLUDED.system_type,
                        locale = EXCLUDED.locale,
                        timezone = EXCLUDED.timezone,
                        app_ver = EXCLUDED.app_ver,
                        reg_date = EXCLUDED.reg_date,
                        servtime = NOW()::timestamp(0);";

                var parameters = new[]
                {
                    new NpgsqlParameter("@eqpid", eqpid),
                    new NpgsqlParameter("@type", type),
                    new NpgsqlParameter("@os", SystemInfoCollector.GetOSVersion()),
                    new NpgsqlParameter("@arch", SystemInfoCollector.GetArchitecture()),
                    new NpgsqlParameter("@pc_name", SystemInfoCollector.GetMachineName()),
                    new NpgsqlParameter("@loc", SystemInfoCollector.GetLocale()),
                    new NpgsqlParameter("@tz", SystemInfoCollector.GetTimeZoneId()),
                    new NpgsqlParameter("@app_ver", _appVersion),
                    new NpgsqlParameter("@pc_now", DateTime.Now)
                };

                _dbRepository.ExecuteNonQuery(sql, parameters);
                _logger.Event($"[EqpidManager] Agent info for '{eqpid}' uploaded to database.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[EqpidManager] Database upload failed: {ex.Message}");
            }
        }

        public TimeZoneInfo GetTimezoneForEqpid(string eqpid)
        {
            if (_timezoneCache.TryGetValue(eqpid, out var cachedZone))
            {
                return cachedZone;
            }
            try
            {
                string sql = "SELECT timezone FROM public.agent_info WHERE eqpid = @eqpid LIMIT 1";
                var result = _dbRepository.ExecuteScalar(sql, new NpgsqlParameter("@eqpid", eqpid));

                if (result != null && result != DBNull.Value)
                {
                    var timezoneId = result.ToString();
                    var fetchedZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                    _timezoneCache.TryAdd(eqpid, fetchedZone);
                    return fetchedZone;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[EqpidManager] Failed to fetch timezone for {eqpid}: {ex.Message}");
            }
            return TimeZoneInfo.Local;
        }
    }

    public static class SystemInfoCollector
    {
        public static string GetOSVersion()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                {
                    return searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["Caption"]?.ToString() ?? "Unknown OS";
                }
            }
            catch { return "Unknown OS"; }
        }
        public static string GetArchitecture() => Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        public static string GetMachineName() => Environment.MachineName;
        public static string GetLocale() => CultureInfo.CurrentUICulture.Name;
        public static string GetTimeZoneId() => TimeZoneInfo.Local.Id;
    }
}
