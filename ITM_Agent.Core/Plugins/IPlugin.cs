namespace ITM_Agent.Core.Plugins
{
    /// <summary>
    /// 모든 동적 플러그인이 반드시 구현해야 하는 공통 인터페이스입니다.
    /// 이 인터페이스는 플러그인을 동적으로 로드하고 실행하기 위한 표준 계약 역할을 합니다.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// 플러그인의 고유한 이름을 반환합니다.
        /// </summary>
        string PluginName { get; }

        /// <summary>
        /// 플러그인의 핵심 처리 로직을 실행하는 메서드입니다.
        /// </summary>
        /// <param name="filePath">처리할 대상 파일의 전체 경로</param>
        /// <param name="arg1">첫 번째 추가 인자 (예: 설정 파일 경로)</param>
        /// <param name="arg2">두 번째 추가 인자 (예: EQPID)</param>
        void ProcessAndUpload(string filePath, object arg1 = null, object arg2 = null);
    }
}
