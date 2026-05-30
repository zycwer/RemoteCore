# RemoteCore

远程管理工具 —— Python 服务端提供 Web 控制台，Windows 客户端常驻连接并执行指令。

> ⚠️ 仅限学习、测试与明确授权的运维管理用途。禁止在未授权设备上部署使用，后果自负。

## 功能

| 类别 | 功能 | 说明 |
|------|------|------|
| 📷 摄像头 | 拍摄照片 | 远程调用摄像头拍照并回传 |
| 🖥️ 屏幕 | 抓取屏幕 / 远程桌面 | 截图或实时画面 + 键鼠控制 |
| 📁 文件 | 文件管理 | 浏览目录、上传/下载文件（流式直通） |
| 💻 命令 | 远程执行 | 以管理员权限执行 CMD 命令 |
| ⏰ 定时 | 定时任务 | Cron 表达式定时拍照/截图/执行命令 |
| 👥 管理 | 客户端管理 | 重启 / 停止 / 卸载自毁 |

## 架构

```
┌─────────────┐     Socket.IO      ┌──────────────┐
│  Web 控制台  │◄──────────────────►│   服务端      │
│  (浏览器)    │    HTTP + WS       │  Flask+SocketIO│
└─────────────┘                     └──────┬───────┘
                                           │ Socket.IO
                                    ┌──────▼───────┐
                                    │  Windows 客户端│
                                    │  .NET 8       │
                                    └──────────────┘
```

- **服务端** (`server/`)：Flask + Flask-SocketIO，提供 REST API 和 WebSocket 双向通信
- **Web 控制台** (`web/`)：单页应用，通过 Socket.IO 实时交互
- **客户端** (`client/`)：C# .NET 8 WinForms，自动安装到 Program Files 并设置开机自启

## 快速开始

### 1. 启动服务端

**Linux / macOS：**
```bash
chmod +x start-server.sh && ./start-server.sh
```

**Windows：**
```cmd
start-server.bat
```

**手动启动：**
```bash
pip install -r server/requirements.txt
cp server/.env.example server/.env   # 编辑 AUTH_TOKEN
cd server && python server.py
```

服务默认监听 `http://0.0.0.0:5000`。

### 2. 构建客户端

```cmd
build-client.bat
```

或手动：
```bash
dotnet publish client/RemoteCore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o client/publish
```

### 3. 运行客户端

以**管理员身份**运行 `RemoteCore.exe`。客户端会：
1. 将自身复制到 `C:\Program Files\RemoteCore\`（设置隐藏属性）
2. 创建计划任务实现开机自启
3. 连接服务端并等待指令

### 4. 访问控制台

浏览器打开 `http://<server-ip>:5000`，使用 Basic Auth 登录：
- 用户名：任意
- 密码：`.env` 中配置的 `AUTH_TOKEN`

## 配置

### 服务端 (`server/.env`)

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `AUTH_TOKEN` | `admin123` | 鉴权密钥，**生产环境务必修改** |
| `SERVER_PORT` | `5000` | 监听端口 |
| `CORS_ORIGINS` | `*` | 允许跨域来源 |
| `STREAMING_ENABLED` | `1` | 启用流式文件直通 |

### 客户端（环境变量）

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `REMOTECORE_SERVER_URL` | `http://127.0.0.1:5000` | 服务端地址 |
| `REMOTECORE_AUTH_TOKEN` | `admin123` | 需与服务端 AUTH_TOKEN 一致 |

## 文件传输

支持两种模式，自动选择：

- **流式直通**（默认）：服务端内存中转发，不落盘，耗时 ≈ max(上行, 下行)
- **兼容模式**（回退）：服务端先落盘再转发，适用于旧版客户端

## 定时任务

在 Web 控制台的「⏰ 定时任务」Tab 中创建，支持标准 5 字段 Cron 表达式：

```
┌──────── 分钟 (0-59)
│ ┌────── 小时 (0-23)
│ │ ┌──── 日 (1-31)
│ │ │ ┌── 月 (1-12)
│ │ │ │ ┌ 周 (0-6, 0=周日)
│ │ │ │ │
* * * * *
```

常用示例：
- `*/5 * * * *` — 每 5 分钟
- `0 8 * * *` — 每天 8:00
- `0 9 * * 1-5` — 工作日 9:00

## 客户端行为

| 操作 | 效果 |
|------|------|
| **重启** | 启动新进程后退出当前进程 |
| **停止** | 直接退出进程，需手动重启 |
| **卸载** | 删除计划任务 → 清除文件属性 → 删除安装目录 → 退出进程 |

## 目录结构

```
RemoteCore/
├── server/
│   ├── server.py            # 服务端主程序
│   ├── requirements.txt     # Python 依赖
│   ├── .env.example         # 配置模板
│   └── test_content_disposition.py
├── client/
│   ├── Program.cs           # 入口（自安装、自启动、互斥锁）
│   ├── RemoteClient.cs      # 客户端核心逻辑
│   └── RemoteCore.csproj    # .NET 项目文件
├── web/
│   └── index.html           # Web 控制台
├── start-server.sh          # Linux/macOS 启动脚本
├── start-server.bat         # Windows 启动脚本
├── build-client.bat         # 客户端构建脚本
├── .gitignore
└── LICENSE                  # MIT
```

## 安全提醒

- 生产环境**必须**修改默认 `AUTH_TOKEN`
- 建议在服务端前部署 HTTPS 反向代理（Nginx/Caddy）
- 客户端以管理员权限运行，可执行任意命令，请确保部署环境受控

## License

[MIT](LICENSE)
