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
                string? rangeHeader = context.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(rangeHeader))
                {
                    request.Headers.TryAddWithoutValidation("Range", rangeHeader);
                }

                // Send request using bypassing HTTP client
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                context.Response.StatusCode = (int)response.StatusCode;

                // Copy headers safely
                foreach (var header in response.Headers)
                {
                    try
                    {
                        context.Response.Headers[header.Key] = string.Join(", ", header.Value);
                    }
                    catch
                    {
                        // Ignore restricted headers
                    }
                }
                foreach (var header in response.Content.Headers)
                {
                    try
                    {
                        if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.ContentType = string.Join(", ", header.Value);
                        }
                        else if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            if (long.TryParse(string.Join("", header.Value), out long len))
                            {
                                context.Response.ContentLength64 = len;
                            }
                        }
                        else
                        {
                            context.Response.Headers[header.Key] = string.Join(", ", header.Value);
                        }
                    }
                    catch
                    {
                        // Ignore restricted headers
                    }
                }

                // Copy body stream
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                {
                    await responseStream.CopyToAsync(context.Response.OutputStream);
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error handling proxy request: {ex.Message}\n{ex.StackTrace}");
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
