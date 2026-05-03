using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RekonOps.Rekon
{
    /// <summary>
    /// HTTP 요청 추상화 인터페이스.
    /// UnityWebRequest 의존성을 격리하여 테스트에서 MockHttpClient로 대체 가능하게 합니다.
    /// </summary>
    public interface IRekonHttpClient
    {
        /// <summary>
        /// HTTP GET 요청을 전송합니다.
        /// </summary>
        /// <param name="url">요청 URL</param>
        /// <param name="headers">추가 헤더 (null 가능)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>HTTP 응답 (상태 코드 + 본문)</returns>
        Task<HttpResponse> GetAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// HTTP POST 요청을 전송합니다 (JSON body).
        /// </summary>
        /// <param name="url">요청 URL</param>
        /// <param name="jsonBody">JSON 요청 본문 (null이면 "{}" 전송)</param>
        /// <param name="headers">추가 헤더 (null 가능)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>HTTP 응답 (상태 코드 + 본문)</returns>
        Task<HttpResponse> PostAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// HTTP PUT 요청을 전송합니다 (raw bytes).
        /// </summary>
        /// <param name="url">요청 URL</param>
        /// <param name="body">업로드 데이터 (byte[])</param>
        /// <param name="contentType">Content-Type 헤더 값</param>
        /// <param name="progress">진행률 콜백 (0~1, null 가능)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>HTTP 응답 (상태 코드 + 본문)</returns>
        Task<HttpResponse> PutAsync(
            string url,
            byte[] body,
            string contentType,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// HTTP 응답 값 타입.
    /// </summary>
    public struct HttpResponse
    {
        /// <summary>HTTP 상태 코드 (예: 200, 401, 404)</summary>
        public int StatusCode;

        /// <summary>응답 본문 문자열 (없으면 빈 문자열)</summary>
        public string Body;

        /// <summary>2xx 응답이면 true</summary>
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    }
}
