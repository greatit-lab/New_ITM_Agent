using Npgsql;

namespace ITM_Agent.Common
{
    /// <summary>
    /// PostgreSQL 데이터베이스 연결 정보를 생성합니다.
    /// </summary>
    public sealed class DatabaseInfo
    {
        // TODO: 실제 운영 환경에 맞는 DB 접속 정보로 변경해야 합니다.
        private const string _server   = "127.0.0.1"; // 예시: 로컬호스트
        private const string _database = "itm";
        private const string _userId   = "postgres";  // 예시 사용자
        private const string _password = "password";  // 예시 비밀번호
        private const int    _port     = 5432;

        private DatabaseInfo() { }

        public static DatabaseInfo CreateDefault() => new DatabaseInfo();

        /// <summary>
        /// PostgreSQL 전용 연결 문자열을 생성하여 반환합니다.
        /// </summary>
        public string GetConnectionString()
        {
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host     = _server,
                Database = _database,
                Username = _userId,
                Password = _password,
                Port     = _port,
                Encoding = "UTF8",
                SslMode  = SslMode.Disable,   // 필요 시 Enable로 변경
                SearchPath = "public"      // 기본 스키마를 public으로 지정
            };
            return csb.ConnectionString;
        }
    }
}
