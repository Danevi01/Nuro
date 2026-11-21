using System.Net;
using System.Text.RegularExpressions;

namespace Nuro;

/// <summary>Lädt eine Webseite und gibt sie als lesbaren Text zurück.</summary>
public sealed class WebTools
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Nuro/" + LocalServer.Version);
        return h;
    }

    public async Task<string> FetchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Nur http- und https-Adressen sind erlaubt.");

        using var res = await Http.GetAsync(uri);
        var body = await res.Content.ReadAsStringAsync();
        var type = res.Content.Headers.ContentType?.MediaType ?? "";
        var head = $"{(int)res.StatusCode} {res.ReasonPhrase} – {uri}\n\n";
        if (!res.IsSuccessStatusCode) return head + Truncate(body, 2000);
        if (!type.Contains("html", StringComparison.OrdinalIgnoreCase)) return head + body;
        return head + HtmlToText(body);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + " …";

    public static string HtmlToText(string html)
    {
        var title = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var s = Regex.Replace(html, @"<(script|style|noscript|svg|head)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/li|/h[1-6]|/tr|/section|/article)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t ]+", " ");
        s = Regex.Replace(s, @"\s*\n\s*", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        return (title.Length > 0 ? "Titel: " + WebUtility.HtmlDecode(title) + "\n\n" : "") + s;
    }
}
