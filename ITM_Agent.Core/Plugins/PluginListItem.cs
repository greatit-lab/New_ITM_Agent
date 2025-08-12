namespace ITM_Agent.Core.Plugins
{
    /// <summary>
    /// 동적으로 로드된 플러그인 하나의 메타데이터를 저장하는 모델 클래스입니다.
    /// </summary>
    public class PluginListItem
    {
        /// <summary>
        /// 플러그인의 이름 (보통 어셈블리 이름)
        /// </summary>
        public string PluginName { get; set; }

        /// <summary>
        /// 플러그인 DLL 파일의 전체 경로
        /// </summary>
        public string AssemblyPath { get; set; }

        /// <summary>
        /// 플러그인의 버전 정보 (예: "1.0.0.0")
        /// </summary>
        public string PluginVersion { get; set; }

        /// <summary>
        /// UI의 리스트 박스 등에 표시될 텍스트를 반환합니다.
        /// 예: "MyPlugin (v1.0.0.0)"
        /// </summary>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(PluginVersion))
            {
                return PluginName;
            }
            return $"{PluginName} (v{PluginVersion})";
        }
    }
}
