using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Nuro;

public sealed class WebTools
{
    private static readonly HttpClient client = new HttpClient();

    public async Task<string> FetchUrlAsync(string url)
    {
        try
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }
    }
}