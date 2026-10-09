using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Xml;

namespace QuickRoute.BusinessEntities.ExternalLapDataSources
{
  /// <summary>
  /// Shared HTTP access for external lap data sources. Calls are synchronous; callers should run them off the UI thread.
  /// </summary>
  internal static class ExternalLapDataHttp
  {
    private static readonly HttpClient client = CreateClient();

    public static XmlDocument GetXmlDocument(string url)
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
      response.EnsureSuccessStatusCode();
      using Stream stream = response.Content.ReadAsStream();
      using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
      var document = new XmlDocument { XmlResolver = null };
      document.Load(reader);
      return document;
    }

    private static HttpClient CreateClient()
    {
      var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
      var version = typeof(ExternalLapDataHttp).Assembly.GetName().Version;
      httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("QuickRoute2/" + (version?.ToString(3) ?? "1.0.0"));
      return httpClient;
    }
  }
}
