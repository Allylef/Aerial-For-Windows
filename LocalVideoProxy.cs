using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AerialWindows
{
    public class LocalVideoProxy : IDisposable
    {
        private HttpListener? _listener;
        private readonly HttpClient _httpClient;
        private int _port;
        private bool _isListening;

        public string BaseUrl => $"http://localhost:{_port}/";

        public LocalVideoProxy(HttpClient httpClient, int port = 49200)
        {
            _httpClient = httpClient;
            _port = port;
        }

        public void Start()
        {
            int attempts = 0;
            while (attempts < 10)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                    _isListening = true;
                    Task.Run(() => ListenLoop());
                    App.Log($"Local video proxy started successfully on port {_port}");
                    return;
                }
                catch (Exception ex)
                {
                    App.Log($"Failed to bind to port {_port}: {ex.Message}. Trying next port.");
                    _port++;
                    attempts++;
                }
            }
            App.Log("Failed to start local video proxy after 10 attempts.");
        }

        private async Task ListenLoop()
        {
            while (_isListening && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    App.Log($"Error in proxy listen loop: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.QueryString["url"] ?? "";
                if (string.IsNullOrEmpty(rawUrl))
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    return;
                }

                string url = Uri.UnescapeDataString(rawUrl);

                // Create request to remote URL
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Forward Range header if present
                if (context.Request.Headers["Range"] != null)
                {
                    request.Headers.Add("Range", context.Request.Headers["Range"]);
                }

                // Send request using bypassing HTTP client
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                context.Response.StatusCode = (int)response.StatusCode;

                // Copy headers
                foreach (var header in response.Headers)
                {
                    context.Response.Headers[header.Key] = string.Join(", ", header.Value);
                }
                foreach (var header in response.Content.Headers)
                {
                    context.Response.Headers[header.Key] = string.Join(", ", header.Value);
                }

                // Copy body stream
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                {
                    await responseStream.CopyToAsync(context.Response.OutputStream);
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error handling proxy request: {ex.Message}");
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            _isListening = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }
        }
    }
}
