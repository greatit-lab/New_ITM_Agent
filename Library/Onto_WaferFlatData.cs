using ITM_Agent.Common;
using ITM_Agent.Core.Plugins; // IPlugin 인터페이스를 사용하기 위해 추가
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Onto_WaferFlatDataLib
{
    // .NET Core/5+ 환경에서 CP949 인코딩을 사용하기 위해 필요합니다.
    // 이 프로젝트에 System.Text.Encoding.CodePages NuGet 패키지를 설치해야 합니다.
    static class EncodingProvider
    {
        public static void Register()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
    
    // 자체 인터페이스 대신 IPlugin을 구현합니다.
    public class Onto_WaferFlatData : IPlugin
    {
        private readonly ILogger _logger;
        private readonly DatabaseRepository _dbRepository;
        
        public string PluginName => "Onto_WaferFlatData";

        static Onto_WaferFlatData()
        {
            EncodingProvider.Register();
        }

        public Onto_WaferFlatData()
        {
            // 생성자에서 중앙 서비스 인스턴스를 가져옵니다.
            _logger = SharedLogManager.Instance;
            _dbRepository = new DatabaseRepository(_logger);
        }
        
        public void ProcessAndUpload(string filePath, object arg1 = null, object arg2 = null)
        {
            _logger.Event($"[WaferFlat] ProcessAndUpload triggered for: {filePath}");

            if (!WaitForFileReady(filePath))
            {
                _logger.Error($"[WaferFlat] File was not ready for processing: {filePath}");
                return;
            }

            try
            {
                // arg2를 통해 메인 프로그램으로부터 EQPID를 전달받습니다.
                string eqpid = arg2 as string ?? "UNKNOWN_EQPID";
                ProcessFile(filePath, eqpid);
            }
            catch (Exception ex)
            {
                _logger.Error($"[WaferFlat] Unhandled exception during processing {filePath}: {ex.ToString()}");
            }
        }

        private void ProcessFile(string filePath, string eqpid)
        {
            _logger.Debug($"[WaferFlat] Parsing file: {Path.GetFileName(filePath)}");

            string fileContent = ReadAllTextSafe(filePath, Encoding.GetEncoding(949));
            var lines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ln in lines)
            {
                int idx = ln.IndexOf(':');
                if (idx > 0)
                {
                    string key = ln.Substring(0, idx).Trim();
                    if (!meta.ContainsKey(key)) // .NET Framework 호환
                    {
                        meta.Add(key, ln.Substring(idx + 1).Trim());
                    }
                }
            }

            int? waferNo = null;
            if (meta.TryGetValue("Wafer ID", out string waferId))
            {
                var m = Regex.Match(waferId, @"W(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int w)) waferNo = w;
            }

            DateTime dtVal = DateTime.MinValue;
            if (meta.TryGetValue("Date and Time", out string dtStr))
                DateTime.TryParse(dtStr, out dtVal);

            int hdrIdx = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Point#", StringComparison.OrdinalIgnoreCase));
            if (hdrIdx == -1)
            {
                _logger.Debug("[WaferFlat] Header 'Point#' not found. Skipping file.");
                return;
            }

            var headers = lines[hdrIdx].Split(',').Select(NormalizeHeader).ToList();
            var headerIndexMap = headers.Select((h, idx) => new { h, idx })
                                        .GroupBy(x => x.h)
                                        .ToDictionary(g => g.Key, g => g.First().idx);

            var dataTable = BuildDataTable(headers, meta, eqpid, dtVal, waferNo, lines, hdrIdx, headerIndexMap);

            if (dataTable.Rows.Count > 0)
            {
                UploadData(dataTable, Path.GetFileName(filePath));
            }

            try 
            { 
                File.Delete(filePath);
                _logger.Debug($"[WaferFlat] Source file deleted: {filePath}");
            } 
            catch (Exception ex) 
            { 
                _logger.Error($"[WaferFlat] Failed to delete file {filePath}: {ex.Message}"); 
            }
        }

        private DataTable BuildDataTable(List<string> headers, Dictionary<string, string> meta, string eqpid, DateTime dtVal, int? waferNo, string[] lines, int hdrIdx, Dictionary<string, int> headerIndexMap)
        {
            var dataTable = new DataTable();
            foreach (var header in headerIndexMap.Keys)
            {
                dataTable.Columns.Add(header, typeof(object));
            }
            dataTable.Columns.Add("cassettercp", typeof(string));
            dataTable.Columns.Add("stagercp", typeof(string));
            dataTable.Columns.Add("stagegroup", typeof(string));
            dataTable.Columns.Add("lotid", typeof(string));
            dataTable.Columns.Add("waferid", typeof(int));
            dataTable.Columns.Add("datetime", typeof(DateTime));
            dataTable.Columns.Add("film", typeof(string));
            dataTable.Columns.Add("eqpid", typeof(string));
            dataTable.Columns.Add("serv_ts", typeof(DateTime));

            for (int i = hdrIdx + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var vals = lines[i].Split(',').Select(v => v.Trim()).ToArray();
                if (vals.Length < headers.Count) continue;

                var row = dataTable.NewRow();
                row["cassettercp"] = meta.GetValueOrDefault("Cassette Recipe Name", "");
                row["stagercp"] = meta.GetValueOrDefault("Stage Recipe Name", "");
                row["stagegroup"] = meta.GetValueOrDefault("Stage Group Name", "");
                row["lotid"] = meta.GetValueOrDefault("Lot ID", "");
                row["waferid"] = waferNo.HasValue ? (object)waferNo.Value : DBNull.Value;
                row["datetime"] = dtVal != DateTime.MinValue ? (object)dtVal : DBNull.Value;
                row["film"] = meta.GetValueOrDefault("Film Name", "");
                row["eqpid"] = eqpid;
                
                if (dtVal != DateTime.MinValue) row["serv_ts"] = TimeSyncProvider.Instance.ToSynchronizedKst(dtVal);
                else row["serv_ts"] = DBNull.Value;
                
                foreach (var kvp in headerIndexMap)
                {
                    string colName = kvp.Key;
                    int colIdx = kvp.Value;
                    string rawVal = (colIdx < vals.Length) ? vals[colIdx] : "";
                    
                    if (double.TryParse(rawVal, out double numVal)) row[colName] = numVal;
                    else row[colName] = string.IsNullOrEmpty(rawVal) ? (object)DBNull.Value : rawVal;
                }
                dataTable.Rows.Add(row);
            }
            return dataTable;
        }

        private void UploadData(DataTable dataTable, string sourceFileName)
        {
            try
            {
                const string sqlTemplate = "INSERT INTO public.plg_wf_flat ({0}) VALUES ({1}) ON CONFLICT DO NOTHING;";
                foreach (DataRow row in dataTable.Rows)
                {
                    var cols = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));
                    var pars = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}"));
                    string sql = string.Format(sqlTemplate, cols, pars);
                    
                    var sqlParams = dataTable.Columns.Cast<DataColumn>()
                        .Select(c => new NpgsqlParameter($"@{c.ColumnName}", row[c] ?? DBNull.Value))
                        .ToArray();
                        
                    _dbRepository.ExecuteNonQuery(sql, sqlParams);
                }
                _logger.Event($"[WaferFlat] Successfully uploaded {dataTable.Rows.Count} rows from {sourceFileName}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[WaferFlat] Database upload failed for {sourceFileName}: {ex.Message}");
            }
        }
        
        private string NormalizeHeader(string h)
        {
            h = h.ToLowerInvariant();
            h = Regex.Replace(h, @"\(\s*no\s*cal\.?\s*\)", " nocal ");
            h = Regex.Replace(h, @"\bno[\s_]*cal\b", "nocal");
            h = Regex.Replace(h, @"\(\s*cal\.?\s*\)", " cal ");
            h = h.Replace("(mm)", "").Replace("(탆)", "").Replace("die x", "diex").Replace("die y", "diey").Trim();
            h = Regex.Replace(h, @"\s+", "_");
            h = Regex.Replace(h, @"[#/:\-]", "");
            return h;
        }

        private bool WaitForFileReady(string path, int maxRetries = 10, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return true;
                }
                catch (IOException) { Thread.Sleep(delayMs); }
            }
            return false;
        }

        private string ReadAllTextSafe(string path, Encoding enc, int timeoutMs = 30000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return File.ReadAllText(path, enc);
                }
                catch (IOException)
                {
                    if (sw.ElapsedMilliseconds > timeoutMs) throw;
                    Thread.Sleep(500);
                }
            }
        }
    }

    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
        {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }
    }
}
