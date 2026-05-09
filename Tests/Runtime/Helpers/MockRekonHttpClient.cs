using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// 테스트용 IRekonHttpClient mock.
    /// 호출 기록 (Calls) + 설정 가능한 응답 (ResponseToReturn) + URL 패턴별 응답 매핑 (SetResponseFor).
    /// </summary>
    public class MockRekonHttpClient : IRekonHttpClient
    {
        /// <summary>기록된 요청 목록</summary>
        public List<RequestCall> Calls { get; } = new List<RequestCall>();

        /// <summary>기본 응답 (URL 매핑이 없으면 사용)</summary>
        public HttpResponse ResponseToReturn { get; set; } = new HttpResponse { StatusCode = 200, Body = "{}" };

        /// <summary>예외 throw (null 이면 ResponseToReturn 반환)</summary>
        public Exception ExceptionToThrow { get; set; }

        // URL 패턴(Contains 매칭) → 응답 매핑
        private readonly List<UrlResponseRule> _urlRules = new List<UrlResponseRule>();

        /// <summary>특정 URL substring 매칭 시 응답 설정</summary>
        public void SetResponseFor(string urlContains, HttpResponse response)
        {
            _urlRules.Add(new UrlResponseRule { UrlContains = urlContains, Response = response });
        }

        /// <summary>URL 매핑 초기화</summary>
        public void ClearUrlRules()
        {
            _urlRules.Clear();
        }

        public Task<HttpResponse> GetAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RequestCall { Method = "GET", Url = url, Headers = headers });
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult(ResolveResponse(url));
        }

        public Task<HttpResponse> PostAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RequestCall { Method = "POST", Url = url, Body = jsonBody, Headers = headers });
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult(ResolveResponse(url));
        }

        public Task<HttpResponse> PutAsync(
            string url,
            byte[] body,
            string contentType,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RequestCall { Method = "PUT", Url = url, ContentType = contentType });
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult(ResolveResponse(url));
        }

        private HttpResponse ResolveResponse(string url)
        {
            foreach (var rule in _urlRules)
            {
                if (url != null && url.Contains(rule.UrlContains))
                    return rule.Response;
            }
            return ResponseToReturn;
        }

        /// <summary>단일 요청 기록</summary>
        public class RequestCall
        {
            public string Method;
            public string Url;
            public string Body;
            public string ContentType;
            public Dictionary<string, string> Headers;
        }

        private class UrlResponseRule
        {
            public string UrlContains;
            public HttpResponse Response;
        }
    }
}
