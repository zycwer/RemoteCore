# RemoteCore 快速入门指南

## 项目结构
- **server/**: Python Flask + SocketIO 服务端
- **client/**: C# .NET 8 客户端 (Windows)
- **web/**: Web 管理前端

## 1. 启动服务端

### Linux/macOS
```bash
chmod +x start-server.sh
./start-server.sh
```

### Windows
```cmd
start-server.bat
```

或者手动操作：
1. 创建虚拟环境: `python -m venv server/venv`
2. 激活虚拟环境
3. 安装依赖: `pip install -r server/requirements.txt`
4. 复制配置: `cp server/.env.example server/.env` (Windows: `copy`)
5. 启动服务: `cd server && python server.py`

服务端将在 `http://0.0.0.0:5000` 启动

## 2. 配置

编辑 `server/.env` 文件：
```
AUTH_TOKEN=your_secure_token_here
CORS_ORIGINS=*
STREAMING_ENABLED=1
SERVER_PORT=5000
```

## 3. 构建并运行客户端

### Windows
```cmd
build-client.bat
```
或者手动：
```cmd
cd client
dotnet publish -c Release -r win-x64 -o publish
```

运行客户端：
1. 以管理员身份运行 `publish/RemoteCore.exe`
2. 客户端会自动连接到 `http://127.0.0.1:5000`
3. 如需修改服务端地址，设置环境变量 `REMOTECORE_SERVER_URL`
4. 确保 `REMOTECORE_AUTH_TOKEN` 与服务端一致

## 4. 访问 Web 控制台

在浏览器中打开 `http://<server-ip>:5000`

使用 Basic Auth 登录：
- 用户名：任意
- 密码：`AUTH_TOKEN` 的值

## 功能说明

- 📷 拍摄照片：远程捕获摄像头
- 🖥️ 抓取屏幕：获取屏幕截图
- 🖱️ 远程桌面：实时控制远程机器
- 📁 文件管理：上传/下载文件
- 💻 命令执行：执行远程命令
- 🔄 客户端管理：重启/停止/卸载

## 安全说明

⚠️ 此工具仅用于授权的运维和学习目的。
- 请务必修改默认的 `AUTH_TOKEN`
- 不要在未授权的设备上使用
- 在公共网络中请使用加密传输
