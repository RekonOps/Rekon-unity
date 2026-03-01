using System.Collections.Generic;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 커스텀 K/V 컨텍스트 데이터를 제공하는 인터페이스.
    /// 게임 코드에서 ContextProviderRegistry에 등록하면
    /// 상태 스냅샷 수집 시 자동으로 포함됩니다.
    ///
    /// 사용 예:
    ///   public class MyGameContextProvider : IContextProvider
    ///   {
    ///       public Dictionary&lt;string, string&gt; GetContext()
    ///       {
    ///           return new Dictionary&lt;string, string&gt;
    ///           {
    ///               { "level", GameManager.CurrentLevel.ToString() },
    ///               { "score", GameManager.Score.ToString() },
    ///           };
    ///       }
    ///   }
    /// </summary>
    public interface IContextProvider
    {
        /// <summary>
        /// 현재 컨텍스트 데이터를 Dictionary 형태로 반환합니다.
        /// null을 반환하면 해당 프로바이더의 데이터는 무시됩니다.
        /// Key 충돌 시 나중에 등록된 프로바이더가 우선합니다.
        /// </summary>
        Dictionary<string, string> GetContext();
    }
}
