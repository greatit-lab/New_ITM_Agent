namespace ITM_Agent.Common
{
    /// <summary>
    /// 솔루션의 모든 모듈에서 일관된 로깅을 위해 사용할 공통 인터페이스입니다.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// 일반적인 이벤트 정보(예: 서비스 시작, 파일 감지)를 기록합니다.
        /// </summary>
        /// <param name="message">기록할 메시지</param>
        void Event(string message);

        /// <summary>
        /// 예외 발생 등 오류 상황을 기록합니다.
        /// </summary>
        /// <param name="message">기록할 오류 메시지</param>
        void Error(string message);

        /// <summary>
        /// 디버그 모드가 활성화된 경우에만 개발자를 위한 상세 정보를 기록합니다.
        /// </summary>
        /// <param name="message">기록할 디버그 메시지</param>
        void Debug(string message);

        /// <summary>
        /// 디버그 로깅 활성화 여부를 동적으로 설정합니다.
        /// </summary>
        /// <param name="isEnabled">활성화 여부</param>
        void SetDebugMode(bool isEnabled);
    }
}
