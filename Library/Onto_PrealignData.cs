using ITM_Agent.Common;
using ITM_Agent.Core.Plugins; // IPlugin 인터페이스를 사용하기 위해 추가
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Onto_PrealignDataLib
{
    // .NET Core/5+ 환경에서 CP949 인코딩을 사용하기 위해 필요
    static class EncodingProvider
    {
        public static void Register()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
    
    // 자체 인터페이스 대신 IPlugin을 구현합니다.
    public class Onto_PrealignData : IPlugin
    {
        private readonly ILogger _logger;
        private readonly DatabaseRepository _dbRepository;
        private FileSystemWatcher _fw; // 기능 유지를 위해 남겨둠
        
        public string PluginName => "Onto_PrealignData";

        static Onto_PrealignData()
        {
            EncodingProvider.Register();
        }

        public Onto_PrealignData()
        {
            // 중앙 서비스 인스턴스를 가져옵니다.
            _logger = SharedLogManager.Instance;
            _dbRepository = new DatabaseRepository(_logger);
        }
        
        // IPlugin 인터페이스의 필수 구현 멤버입니다. (수동 처리용)
        public void ProcessAndUpload(string filePath, object arg1 = null, object arg2 = null)
        {
            _logger.Event($"[Prealign] Manual ProcessAndUpload triggered for: {filePath}");
            if (!File.Exists(filePath))
            {
                _logger.Error($"[Prealign] File does not exist: {filePath}");
                return;
            }

            // 메인 프로그램으로부터 EQPID를 전달받습니다.
            string eqpid = arg2 as string ?? "UNKNOWN_EQPID";
            
            try
            {
                ProcessCore(filePath, eqpid); // 전체 파일을 처리하는 메서드 호출
            }
            catch (Exception ex)
            {
                _logger.Error($"[Prealign] Unhandled exception during manual processing of {filePath}: {ex.ToString()}");
            }
        }

        // --- Watcher Methods (기존 기능 유지를 위해 남겨둠) ---
        public void StartWatch(string folderPath)
        {
            _logger.Event("[Prealign] Legacy StartWatch called. It's recommended to use the central FileProcessingService.");
            // 이 기능은 이제 메인 프로그램의 FileProcessingService가 담당하므로,
            // 이 플러그인 내에서의 자동 감시 기능은 사용되지 않습니다.
        }

        public void StopWatch()
        {
            _fw?.Dispose();
            _fw = null;
        }

        /// <summary>
        /// 전체 파일을 읽어 처리하는 핵심 로직입니다.
        /// </summary>
        private void ProcessCore(string filePath, string eqpid)
        {
            var lines = File.ReadAllLines(filePath, Encoding.GetEncoding(949));
            var rex = new Regex(@"Xmm\s*([-\d.]+)\s*Ymm\s*([-\d.]+)\s*Notch\s*([-\d.]+)\s*Time\s*([\d\-:\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var dataTable = new DataTable();
            dataTable.Columns.Add("eqpid", typeof(string));
            dataTable.Columns.Add("datetime", typeof(DateTime));
            dataTable.Columns.Add("xmm", typeof(decimal));
            dataTable.Columns.Add("ymm", typeof(decimal));
            dataTable.Columns.Add("notch", typeof(decimal));
            dataTable.Columns.Add("serv_ts", typeof(DateTime));

            foreach (string line in lines)
            {
                Match m = rex.Match(line);
                if (!m.Success) continue;
                
                if (DateTime.TryParse(m.Groups[4].Value.Trim(), out DateTime ts) &&
                    decimal.TryParse(m.Groups[1].Value, out decimal x) &&
                    decimal.TryParse(m.Groups[2].Value, out decimal y) &&
                    decimal.TryParse(m.Groups[3].Value, out decimal n))
                {
                    var serv_ts = TimeSyncProvider.Instance.ToSynchronizedKst(ts);
                    serv_ts = new DateTime(serv_ts.Year, serv_ts.Month, serv_ts.Day, serv_ts.Hour, serv_ts.Minute, serv_ts.Second);
                    dataTable.Rows.Add(eqpid, ts, x, y, n, serv_ts);
                }
            }

            if (dataTable.Rows.Count > 0)
            {
                UploadData(dataTable, Path.GetFileName(filePath));
            }
        }

        private void UploadData(DataTable dataTable, string sourceFileName)
        {
            try
            {
                const string sql = @"
                    INSERT INTO public.plg_prealign (eqpid, datetime, xmm, ymm, notch, serv_ts)
                    VALUES (@eqpid, @datetime, @xmm, @ymm, @notch, @serv_ts)
                    ON CONFLICT (eqpid, datetime) DO NOTHING;";
                    
                foreach (DataRow row in dataTable.Rows)
                {
                    var parameters = dataTable.Columns.Cast<DataColumn>()
                        .Select(c => new NpgsqlParameter($"@{c.ColumnName}", row[c] ?? DBNull.Value))
                        .ToArray();
                    _dbRepository.ExecuteNonQuery(sql, parameters);
                }
                _logger.Event($"[Prealign] Successfully uploaded {dataTable.Rows.Count} rows from {sourceFileName}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Prealign] Database upload failed for {sourceFileName}: {ex.Message}");
            }
        }
    }
}
