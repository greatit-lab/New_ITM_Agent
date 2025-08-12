using Npgsql;
using System;
using System.Data;
using System.Linq;

namespace ITM_Agent.Common
{
    /// <summary>
    /// 데이터베이스 관련 모든 작업을 캡슐화하는 리포지토리 클래스입니다.
    /// </summary>
    public class DatabaseRepository
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;

        public DatabaseRepository(ILogger logger)
        {
            _connectionString = DatabaseInfo.CreateDefault().GetConnectionString();
            _logger = logger;
        }

        /// <summary>
        /// 단일 INSERT, UPDATE, DELETE SQL 문을 실행합니다.
        /// </summary>
        /// <param name="sql">실행할 SQL 쿼리</param>
        /// <param name="parameters">SQL에 전달할 파라미터</param>
        public void ExecuteNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        if (parameters != null)
                        {
                            cmd.Parameters.AddRange(parameters);
                        }
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Database ExecuteNonQuery failed. SQL: {sql}. Error: {ex.Message}");
                throw; // 오류를 상위 호출자로 다시 전달하여 처리하도록 함
            }
        }
        
        /// <summary>
        /// 스칼라 값을 반환하는 쿼리를 실행합니다. (예: SELECT COUNT(*))
        /// </summary>
        /// <param name="sql">실행할 SQL 쿼리</param>
        /// <param name="parameters">SQL에 전달할 파라미터</param>
        /// <returns>쿼리 결과의 첫 번째 행, 첫 번째 열의 값</returns>
        public object ExecuteScalar(string sql, params NpgsqlParameter[] parameters)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        if (parameters != null)
                        {
                            cmd.Parameters.AddRange(parameters);
                        }
                        return cmd.ExecuteScalar();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Database ExecuteScalar failed. SQL: {sql}. Error: {ex.Message}");
                return null;
            }
        }
    }
}
