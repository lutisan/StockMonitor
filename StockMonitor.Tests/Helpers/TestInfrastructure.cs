using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace StockMonitor.Tests.Helpers;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> stub that routes outgoing HTTP requests
/// to an in-memory responder delegate so backend tests never depend on live network access.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>
    /// Gets the list of requested URLs recorded by this handler for assertion in tests.
    /// </summary>
    public List<string> RequestedUrls { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="responder">Delegate returning a mocked <see cref="HttpResponseMessage"/> for each request.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>
    /// Creates a 200 OK JSON <see cref="HttpResponseMessage"/> with UTF-8 encoding.
    /// </summary>
    public static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            RequestedUrls.Add(request.RequestUri.ToString());
        }

        return Task.FromResult(_responder(request));
    }
}

/// <summary>
/// Manages an isolated temporary directory for file-backed service tests (JSON storage, FX cache, Yahoo cache)
/// and deletes it automatically on disposal.
/// </summary>
internal sealed class TempTestDirectory : IDisposable
{
    /// <summary>
    /// Gets the full path to the isolated temporary directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Creates a uniquely named temporary directory under the system temp folder.
    /// </summary>
    public TempTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StockMonitorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Combines a relative filename with this temporary directory's path.
    /// </summary>
    public string FilePath(string fileName) => System.IO.Path.Combine(Path, fileName);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup race conditions on Windows file handles
        }
    }
}
