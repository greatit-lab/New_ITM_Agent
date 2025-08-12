using ITM_Agent.Common;
using ITM_Agent.Core.Plugins;
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

namespace Onto_ErrorDataLib
{
    static class EncodingProvider
    {
        public static void Register()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }

    public class Onto_ErrorData : IPlugin
    {
        private readonly ILogger _logger;
        private readonly DatabaseRepository _dbRepository;

        public string PluginName => "Onto_ErrorData";

        static Onto_ErrorData()
        {
            EncodingProvider.Register();
        }

        public Onto_ErrorData()
        {
            _logger = SharedLogManager.Instance;
            _dbRepository = new DatabaseRepository(_logger);
        }

        // 생성자는 비워둡니다.
        public Onto_ErrorData() { }
        
        // 외부에서 로거와 DB 저장소를 주입받습니다.
        public void Initialize(ILogger logger, DatabaseRepository dbRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbRepository = dbRepository ?? throw new ArgumentNullException(nameof(dbRepository));
        }

        public void ProcessAndUpload(string filePath, object arg1 = null, object arg2 = null)
        {
            _logger.Event($"[ErrorData] ProcessAndUpload triggered for: {filePath}");

            if (!WaitForFileReady(filePath, 10, 500))
            {
                _logger.Error($"[ErrorData] File not ready: {filePath}");
                return;
            }

            string eqpid = arg2 as string ?? "UNKNOWN_EQPID";

            try
            {
                ProcessFile(filePath, eqpid);
            }
            catch (Exception ex)
            {
                _logger.Error($"[ErrorData] Unhandled exception processing {filePath}: {ex.ToString()}");
            }
        }

        private void ProcessFile(string filePath, string eqpid)
        {
            var lines = File.ReadAllLines(filePath, Encoding.GetEncoding(949));
            var meta = ParseMeta(lines);
            if (!meta.ContainsKey("EqpId")) meta["EqpId"] = eqpid;

            UploadItmInfo(meta);
            
            var errorTable = BuildErrorDataTable(lines, eqpid);
            var allowedErrorIds = LoadAllowedErrorIds();
            
            var filteredTable = FilterErrorDataTable(errorTable, allowedErrorIds);
            _logger.Event($"[ErrorData] Filtering complete for {Path.GetFileName(filePath)}. Total: {errorTable.Rows.Count}, Filtered: {filteredTable.Rows.Count}");

            if (filteredTable.Rows.Count > 0)
            {
                UploadErrorData(filteredTable);
            }
        }

        private Dictionary<string, string> ParseMeta(string[] lines)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Where(l => l.Contains(":,")))
            {
                var parts = line.Split(new[] { ":," }, 2, StringSplitOptions.None);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !parts[0].Trim().Equals("EXPORT_TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    string key = parts[0].Trim();
                    if (!dict.ContainsKey(key))
                    {
                        dict.Add(key, parts[1].Trim());
                    }
                }
            }
            if (dict.TryGetValue("DATE", out string dateStr) && 
                DateTime.TryParseExact(dateStr, "M/d/yyyy H:m:s", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
            {
                dict["DATE"] = dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return dict;
        }

        private void UploadItmInfo(Dictionary<string, string> meta)
        {
             const string sql = @"
                INSERT INTO public.itm_info (eqpid, system_name, system_model, serial_num, application, version, db_version, ""date"", serv_ts)
                VALUES (@eqpid, @system_name, @system_model, @serial_num, @application, @version, @db_version, @date, @serv_ts)
                ON CONFLICT (eqpid) DO UPDATE SET
                    system_name = EXCLUDED.system_name, system_model = EXCLUDED.system_model,
                    serial_num = EXCLUDED.serial_num, application = EXCLUDED.application,
                    version = EXCLUDED.version, db_version = EXCLUDED.db_version,
                    ""date"" = EXCLUDED.""date"", serv_ts = EXCLUDED.serv_ts;";
            try
            {
                DateTime date = meta.ContainsKey("DATE") && DateTime.TryParse(meta["DATE"], out var dt) ? dt : DateTime.Now;
                
                var parameters = new[] {
                    new NpgsqlParameter("@eqpid", meta.GetValueOrDefault("EqpId", (object)DBNull.Value)),
                    new NpgsqlParameter("@system_name", meta.GetValueOrDefault("SYSTEM_NAME", (object)DBNull.Value)),
                    new NpgsqlParameter("@system_model", meta.GetValueOrDefault("SYSTEM_MODEL", (object)DBNull.Value)),
                    new NpgsqlParameter("@serial_num", meta.GetValueOrDefault("SERIAL_NUM", (object)DBNull.Value)),
                    new NpgsqlParameter("@application", meta.GetValueOrDefault("APPLICATION", (object)DBNull.Value)),
                    new NpgsqlParameter("@version", meta.GetValueOrDefault("VERSION", (object)DBNull.Value)),
                    new NpgsqlParameter("@db_version", meta.GetValueOrDefault("DB_VERSION", (object)DBNull.Value)),
                    new NpgsqlParameter("@date", date),
                    new NpgsqlParameter("@serv_ts", TimeSyncProvider.Instance.ToSynchronizedKst(date))
                };
                _dbRepository.ExecuteNonQuery(sql, parameters);
                _logger.Debug($"[ErrorData] itm_info upserted for EQPID: {meta.GetValueOrDefault("EqpId")}");
            }
            catch(Exception ex)
            {
                _logger.Error($"[ErrorData] Failed to upsert itm_info: {ex.Message}");
            }
        }
        
        private DataTable BuildErrorDataTable(string[] lines, string eqpid)
        {
            var dt = new DataTable();
            dt.Columns.AddRange(new[] {
                new DataColumn("eqpid", typeof(string)), new DataColumn("error_id", typeof(string)),
                new DataColumn("time_stamp", typeof(DateTime)), new DataColumn("error_label", typeof(string)),
                new DataColumn("error_desc", typeof(string)), new DataColumn("millisecond", typeof(int)),
                new DataColumn("extra_message_1", typeof(string)), new DataColumn("extra_message_2", typeof(string)),
                new DataColumn("serv_ts", typeof(DateTime))
            });

            var rg = new Regex(@"^(?<id>\w+),\s*(?<ts>[^,]+),\s*(?<lbl>[^,]+),\s*(?<desc>[^,]+),\s*(?<ms>\d+)(?:,\s*(?<extra>.*))?", RegexOptions.Compiled);

            foreach (var ln in lines)
            {
                var m = rg.Match(ln);
                if (!m.Success) continue;
                
                DateTime.TryParseExact(m.Groups["ts"].Value.Trim(), "dd-MMM-yy h:mm:ss tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts);
                int.TryParse(m.Groups["ms"].Value, out var ms);
                
                var serv_ts = TimeSyncProvider.Instance.ToSynchronizedKst(ts);
                serv_ts = new DateTime(serv_ts.Year, serv_ts.Month, serv_ts.Day, serv_ts.Hour, serv_ts.Minute, serv_ts.Second);
                
                dt.Rows.Add(eqpid, m.Groups["id"].Value.Trim(), ts, m.Groups["lbl"].Value.Trim(), m.Groups["desc"].Value.Trim(), 
                            ms, m.Groups["extra"].Value.Trim(), "", serv_ts);
            }
            return dt;
        }

        private HashSet<string> LoadAllowedErrorIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var conn = new NpgsqlConnection(DatabaseInfo.CreateDefault().GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("SELECT error_id FROM public.err_severity_map", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if(!reader.IsDBNull(0)) set.Add(reader.GetString(0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[ErrorData] Could not load allowed error IDs: {ex.Message}");
            }
            return set;
        }

        private DataTable FilterErrorDataTable(DataTable source, HashSet<string> allowedIds)
        {
            if (allowedIds == null || allowedIds.Count == 0) return source.Clone();
            var destination = source.Clone();
            foreach (DataRow row in source.Rows)
            {
                if (allowedIds.Contains(row["error_id"].ToString()))
                {
                    destination.ImportRow(row);
                }
            }
            return destination;
        }

        private void UploadErrorData(DataTable table)
        {
            try
            {
                const string sql = @"
                    INSERT INTO public.plg_error (eqpid, error_id, time_stamp, error_label, error_desc, millisecond, extra_message_1, extra_message_2, serv_ts)
                    VALUES (@eqpid, @error_id, @time_stamp, @error_label, @error_desc, @millisecond, @extra_message_1, @extra_message_2, @serv_ts)
                    ON CONFLICT DO NOTHING;";

                foreach (DataRow row in table.Rows)
                {
                    var parameters = table.Columns.Cast<DataColumn>()
                        .Select(c => new NpgsqlParameter($"@{c.ColumnName}", row[c] ?? DBNull.Value))
                        .ToArray();
                    _dbRepository.ExecuteNonQuery(sql, parameters);
                }
                _logger.Event($"[ErrorData] Uploaded {table.Rows.Count} rows to plg_error table.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[ErrorData] Failed to upload to plg_error table: {ex.Message}");
            }
        }
        
        private bool WaitForFileReady(string path, int maxRetries, int delayMs)
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
