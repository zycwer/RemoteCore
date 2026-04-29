# RemoteCore (远程控制与管理系统)

**RemoteCore** 是一个功能强大的远程控制与管理系统，支持通过 Web 界面实现对 Windows 客户端的安全远程管理。本项目包含一个 Python Flask 服务器，以及 C# 语言实现的 Windows 客户端。

> **⚠️ 警告与免责声明**：本项目仅供学习、测试和合法授权的系统管理用途。未经明确许可，禁止在任何他人的设备上部署和使用此工具。使用者需对滥用此工具产生的一切后果自行承担责任。

## ✨ 主要功能

- 📷 **远程摄像头控制**：后台静默调用摄像头拍照，并将结果自动回传至服务器。
- 🖥️ **远程屏幕抓取**：实时截取客户端桌面并上传。
- 🎮 **远程桌面 (VNC)**：基于 WebSocket 的简易 VNC 方案，支持鼠标和键盘事件的同步控制。
- 💻 **远程命令执行 (CMD)**：在客户端静默执行系统命令并返回执行结果。
- 📁 **远程文件管理**：支持浏览客户端任意目录，并进行文件的双向上传和下载。
- 🛡️ **安全认证**：全链路采用 Token 认证机制 (Bearer / Basic Auth)，防止未授权访问。
- 👻 **隐蔽运行机制**：支持 Windows 开机自启、隐藏控制台窗口运行、以及自我复制到 Program Files (需要管理员权限)。
- 💥 **客户端自毁与管理**：支持从 Web 端对客户端下发重启、停止、卸载并自毁的指令。

## 🏗️ 架构与组件

- **服务端 (`server.py`)**：基于 `Flask` 和 `Flask-SocketIO` 构建，负责接收客户端连接并提供 Web 控制面板 (`index.html`)。
- **C# 客户端 (`CSharpClient/`)**：使用 .NET Core 编写的轻量级客户端，支持 Socket.IO 协议，具有很好的执行性能和隐蔽性。

## 🚀 部署指南

### 1. 服务端部署

**环境要求**：Python 3.8+

```bash
# 1. 克隆代码仓库
git clone https://github.com/zycwer/RemoteCore.git
cd RemoteCore

# 2. 安装依赖
pip install -r requirements.txt

# 3. 配置服务器（可选）
# 服务端默认使用硬编码配置：
# - 默认认证令牌：admin123
# - 默认端口：5000
# - 默认监听地址：0.0.0.0

# 如需自定义配置，可设置环境变量：
# Linux / macOS
export AUTH_TOKEN="your_secure_token_here"
export SECRET_KEY="your_flask_secret_key"

# Windows (CMD)
set AUTH_TOKEN=your_secure_token_here
set SECRET_KEY=your_flask_secret_key

# 4. 运行服务器
python server.py
```

### 依赖项说明

项目依赖项已在 `requirements.txt` 文件中定义，包括：
- Flask 3.0+：Web 框架
- Flask-SocketIO 5.3+：实时通信
- Werkzeug 3.0+：文件处理
- requests 2.31+：HTTP 客户端
- simple-websocket 1.0+：WebSocket 传输支持

服务端默认监听 `0.0.0.0:5000`。可通过浏览器访问 `http://<服务器IP>:5000`，使用配置的 `AUTH_TOKEN` 作为密码（用户名任意）登录 Web 控制台。

> **⚠️ 安全警告**：默认令牌 `admin123` 仅用于测试，生产环境必须设置强密码！

### 2. 客户端部署前配置

在编译或运行客户端之前，**必须** 修改代码中的服务器地址和认证 Token：

- **C# 客户端 (`CSharpClient/RemoteClient.cs`)**：
  ```csharp
  private readonly string _serverUrl = "http://你的服务器IP:5000";  // 替换为你的服务器IP
  private readonly string _authToken = "admin123";                // 替换为与服务器一致的认证 token
  ```

也可以通过环境变量覆盖默认值：

- `REMOTECORE_SERVER_URL`：服务器地址（默认 `http://127.0.0.1:5000`）
- `REMOTECORE_AUTH_TOKEN`：认证 Token（默认 `admin123`）

### 3. C# 客户端编译

**环境要求**：.NET 6.0 SDK 或以上

```bash
cd CSharpClient

# 发布为单文件并去除控制台窗口
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:OutputType=WinExe
```

生成的 `.exe` 位于 `bin/Release/net6.0/win-x64/publish/` 目录下。

## 🔒 安全配置说明

系统默认开启基于 Token 的安全验证机制，支持三种认证方式：
1. **Bearer Token**：在请求头中使用 `Authorization: Bearer <AUTH_TOKEN>`（客户端默认使用此方式）
2. **Basic Auth**：在浏览器中使用任意用户名和 `AUTH_TOKEN` 作为密码（Web 控制台默认使用此方式）
3. **URL 参数**：在 URL 中添加 `?token=<AUTH_TOKEN>` 参数

所有接口（包括 WebSocket 连接、文件上传/下载、命令执行等）均受到 Token 保护，未授权的请求将被拒绝。

> **⚠️ 安全提示**：
> - 默认令牌 `admin123` 仅用于测试，生产环境必须设置强密码
> - 建议在生产环境中通过环境变量设置 `AUTH_TOKEN`
> - 确保服务器只在受信任的网络环境中运行

## 📝 客户端功能使用说明

### 客户端启动流程

客户端执行时，会自动尝试：
1. **隐藏自身窗口**（如果尚未隐藏）。
2. **尝试获取管理员权限**（如果需要，会弹出 UAC）。
3. **自我复制到 Program Files**：如果是以管理员身份运行，会自动将自身复制到 `C:\Program Files\RemoteCore\RemoteCore.exe`，并添加开机启动注册表项。
4. **初始化摄像头**：尝试初始化默认摄像头，为后续的拍照功能做准备。
5. **连接服务器**：建立与服务器的 WebSocket 连接，并保持心跳。

### 命令执行说明

- **支持的命令**：可以执行任意 Windows 命令，包括系统管理命令
- **权限要求**：部分命令需要管理员权限才能执行
- **执行环境**：命令在后台静默执行，不会显示命令窗口
- **超时设置**：命令执行超时时间为 30 秒，超过时间会被强制终止
- **输出编码**：支持自动识别和转换不同编码的命令输出（GBK、UTF-8、CP437 等）

> **⚠️ 注意**：由于命令执行权限较高，请谨慎使用此功能，避免执行危险操作。

## 📊 技术实现

### 服务端
- **Web 框架**：Flask 3.0+
- **实时通信**：Flask-SocketIO 5.3+
- **文件处理**：Werkzeug
- **认证机制**：Token + Basic Auth（支持三种认证方式）
- **安全特性**：
  - 文件路径安全检查（防止路径遍历攻击）
  - 上传文件类型验证
  - DoS 防护（限制文件数量和大小）
  - 输入验证和参数过滤
- **配置方式**：默认硬编码配置，支持环境变量覆盖

### C# 客户端
- **网络通信**：SocketIOClient
- **多媒体处理**：OpenCvSharp (摄像头) + System.Drawing (屏幕截图)
- **系统操作**：P/Invoke (Windows API) + Process (命令执行)
- **异步编程**：async/await 模式
- **性能优化**：资源池化和异步I/O
- **配置方式**：硬编码配置

## 🎯 使用场景

- **远程技术支持**：快速连接并解决用户电脑问题
- **企业设备管理**：统一管理公司内部设备
- **家庭远程协助**：帮助家人解决电脑问题
- **监控与安全**：监控重要设备的使用状态
- **教育教学**：远程指导学生操作

## 🔧 常见问题与解决方案

### 1. 客户端无法连接服务器
- **检查网络**：确保客户端和服务器在同一网络或可互相访问
- **检查防火墙**：确保防火墙允许 5000 端口的流量
- **检查 Token**：确保客户端和服务器的 AUTH_TOKEN 一致
- **检查服务器地址**：确保客户端配置的服务器地址正确

### 2. 摄像头拍照失败
- **检查权限**：确保客户端有摄像头访问权限
- **检查摄像头**：确保摄像头已正确安装并可使用
- **检查驱动**：确保摄像头驱动已更新

### 3. 远程命令执行失败
- **检查权限**：确保客户端以管理员身份运行
- **检查命令**：确保命令语法正确且可在目标系统执行
- **检查超时**：命令执行时间不应超过 30 秒

### 4. 文件上传/下载失败
- **检查网络**：确保网络连接稳定
- **检查权限**：确保客户端有文件操作权限
- **检查路径**：确保目标路径存在且可访问

## 📜 License

MIT License. 详见 LICENSE 文件。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目！

## 📞 联系

如果您有任何问题或建议，欢迎联系项目维护者：
- GitHub: [zycwer](https://github.com/zycwer)

---

**RemoteCore** - 让远程管理更简单、更安全！
