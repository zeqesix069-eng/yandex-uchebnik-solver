using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace YandexSolver
{
    public sealed class LessonTarget
    {
        public string Slug { get; private set; }
        public string SessionId { get; private set; }

        public LessonTarget(string slug, string sessionId)
        {
            Slug = slug;
            SessionId = sessionId;
        }
    }

    public sealed class CertificateResult
    {
        public string Url { get; private set; }
        public string Slug { get; private set; }
        public string SessionId { get; private set; }
        public bool Verified { get; private set; }

        public CertificateResult(string url, string slug, string sessionId, bool verified)
        {
            Url = url;
            Slug = slug;
            SessionId = sessionId;
            Verified = verified;
        }
    }

    public static class CertificateResolver
    {
        private static readonly string[] PortalHosts =
        {
            "https://xn--h1adlhdnlo2c.xn--p1ai",
            "https://datalesson.ru"
        };

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            return client;
        }

        public static LessonTarget ParseLessonUrl(string rawUrl, string slugOverride = "")
        {
            string text = (rawUrl ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Введите ссылку на урок.");
            }

            if (!text.Contains("://"))
            {
                text = "https://" + text;
            }

            Uri parsed;
            if (!Uri.TryCreate(text, UriKind.Absolute, out parsed) || string.IsNullOrEmpty(parsed.Host))
            {
                throw new ArgumentException("Некорректная ссылка.");
            }

            Dictionary<string, string> query = ParseQuery(parsed.Query);
            string sessionId;
            if (!query.TryGetValue("session_id", out sessionId) || string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("В ссылке отсутствует параметр session_id.");
            }

            string slug = string.IsNullOrWhiteSpace(slugOverride)
                ? ExtractSlug(parsed.Host, parsed.AbsolutePath)
                : slugOverride.Trim();

            if (string.IsNullOrEmpty(slug))
            {
                throw new ArgumentException("Не удалось определить код урока из ссылки. Укажите его вручную.");
            }

            return new LessonTarget(slug, sessionId.Trim());
        }

        public static string ExtractSlug(string host, string path)
        {
            string normalizedHost = (host ?? string.Empty).ToLowerInvariant();
            string[] segments = (path ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 2 && string.Equals(segments[0], "lessons", StringComparison.Ordinal))
            {
                return segments[1];
            }

            const string suffix = ".datalesson.ru";
            if (normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
            {
                string subdomain = normalizedHost.Substring(0, normalizedHost.Length - suffix.Length);
                if (!string.IsNullOrEmpty(subdomain) && subdomain != "www")
                {
                    return subdomain;
                }
            }

            return string.Empty;
        }

        public static async Task<CertificateResult> VerifyCertificateUrlAsync(LessonTarget target)
        {
            string lastError = string.Empty;

            foreach (string baseUrl in PortalHosts)
            {
                string certUrl = string.Format("{0}/lessons/{1}/certificate?session_id={2}", baseUrl, target.Slug, target.SessionId);
                try
                {
                    using (HttpResponseMessage response = await HttpClient.GetAsync(certUrl).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = string.Format("Сервер вернул HTTP {0} для {1}.", (int)response.StatusCode, certUrl);
                            continue;
                        }

                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (body.Contains("certificate-page") || body.Contains("Поздравляем"))
                        {
                            return new CertificateResult(certUrl, target.Slug, target.SessionId, true);
                        }

                        lastError = string.Format("Страница {0} не похожа на страницу сертификата.", certUrl);
                    }
                }
                catch (TaskCanceledException)
                {
                    lastError = string.Format("Превышено время ожидания при проверке {0}.", certUrl);
                }
                catch (HttpRequestException exc)
                {
                    lastError = string.Format("Ошибка сети при проверке {0}: {1}", certUrl, exc.Message);
                }
            }

            throw new InvalidOperationException(string.IsNullOrEmpty(lastError)
                ? "Не удалось проверить страницу сертификата."
                : lastError);
        }

        private static Dictionary<string, string> ParseQuery(string rawQuery)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string trimmed = (rawQuery ?? string.Empty).TrimStart('?').Replace('?', '&');
            if (string.IsNullOrEmpty(trimmed))
            {
                return result;
            }

            foreach (string pair in trimmed.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eqIndex = pair.IndexOf('=');
                if (eqIndex <= 0)
                {
                    continue;
                }

                string key = Uri.UnescapeDataString(pair.Substring(0, eqIndex));
                string value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));
                if (!result.ContainsKey(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }
    }
}
