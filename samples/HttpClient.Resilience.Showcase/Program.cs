using System.Net.Http;

using var httpClient = new HttpClient();
using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

var response = await httpClient.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    CancellationToken.None);

Console.WriteLine(response.StatusCode);
