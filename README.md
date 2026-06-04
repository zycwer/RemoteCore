# RemoteCore

远程管理工具 —— Python 服务端提供 Web 控制台，Windows 客户端常驻连接并执行指令。

> ⚠️ 仅限学习、测试与明确授权的运维管理用途。禁止在未授权设备上部署使用，后果自负。

---

## 功能

| 类别 | 功能 | 说明 |
|------|------|------|
| 📷 摄像头 | 拍摄照片 | 远程调用摄像头拍照并回传 |
| 🖥️ 屏幕 | 抓取屏幕 / 远程桌面 | 截图或实时画面 + 键鼠控制 |
| 📁 文件 | 文件管理 | 浏览目录、上传/下载文件（流式直通） |
| 💻 命令 | 远程执行 | 以管理员权限执行 CMD 命令 |
| ⏰ 定时 | 定时任务 | Cron 表达式定时拍照/截图/执行命令 |
| 👥 管理 | 客户端管理 | 重启 / 停止 / 卸载自毁 |

---

## 系统架构

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

- **服务端** (`server/`)：Flask + Flask-SocketIO（`async_mode='threading'`），提供 REST API 和 WebSocket 双向通信
- **Web 控制台** (`web/`)：单页应用，jQuery + Socket.IO Client，通过 WebSocket 实时交互
- **客户端** (`client/`)：C# .NET 8 WinForms，自动安装到 Program Files 并设置开机自启

---

## 快速开始

### 1. 启动服务端

```bash
pip install -r server/requirements.txt
cp server/.env.example server/.env   # 编辑 AUTH_TOKEN
cd server && python server.py
```

服务默认监听 `http://0.0.0.0:5000`。

### 2. 构建客户端

```bash
dotnet publish client/RemoteCore.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o client/publish
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

---

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

---

## 技术细节

### 通信协议

所有实时通信基于 Socket.IO（WebSocket 长连接 + HTTP 长轮询回退）。服务端作为中继，Web 控制台和 Windows 客户端之间不直接通信。

**核心事件流：**

```
Web ──emit──► Server ──emit──► Client
Web ◄──emit── Server ◄──emit── Client
```

| 事件名 | 方向 | 用途 |
|--------|------|------|
| `shoot` | Web→Server→Client | 触发摄像头拍照 |
| `take_screenshot` | Web→Server→Client | 触发屏幕截图 |
| `execute_command` | Web→Server→Client | 执行 CMD 命令 |
| `command_result` | Client→Server→Web | 返回命令执行结果 |
| `list_dir` / `dir_list_result` | 双向 | 目录浏览 |
| `start_vnc` / `vnc_frame` / `vnc_mouse_event` / `vnc_key_event` | 双向 | 远程桌面 |
| `request_download` | Web→Server | 请求下载客户端文件 |
| `file_transfer_started` | Server→Web | 传输开始通知 |
| `file_transfer_progress` | Client→Server→Web | 传输进度更新 |
| `client_capabilities` | Client→Server | 上报设备信息 |
| `client_control` | Web→Server→Client | 重启/停止/卸载指令 |

### 文件传输

支持两种模式，根据客户端能力自动选择：

**流式直通（默认）：**

```
上传: Web ──XHR POST──► Server ──queue──► XHR GET ──► Client
下载: Client ──XHR POST──► Server ──queue──► <a download> ──► Web
```

- 服务端在内存中通过 `queue.Queue` 转发数据块，不落盘
- 每个 chunk 大小 128KB（`STREAM_CHUNK_SIZE`），队列最大 64 块（`STREAM_QUEUE_MAX`）
- 传输记录 TTL 30 分钟（`STREAM_TRANSFER_TTL_SECONDS`）
- 每个传输有唯一 `transfer_id`（12 位 hex）和 `access_key` 防止未授权访问
- 下载端空闲 120 秒自动断开，上传端 `queue.put` 超时 30 秒防止死锁

**兼容模式（回退）：**

```
上传: Web ──multipart──► Server(落盘) ──URL──► Client下载
下载: Client ──multipart──► Server(落盘) ──Socket通知──► Web下载
```

- 服务端先保存到磁盘（`storage/uploads/` 或 `storage/downloads/`），再通知对方下载
- 适用于不支持流式传输的旧版客户端

### 定时任务系统

**后端调度引擎：**

- 自实现 5 字段 cron 表达式解析器，支持 `*`、`*/N`、`N-M`、`N` 语法
- 调度线程每秒检查当前分钟是否匹配 cron 表达式，匹配则在新线程中执行任务
- `_cron_next_run()` 计算下次执行时间（逐分钟遍历，上限 366 天）
- 任务存储：内存字典 + JSON 文件持久化（`storage/scheduled_tasks.json`）
- 线程安全：`threading.Lock` 保护所有读写操作
- 服务重启后自动加载已有任务

**REST API：**

| 端点 | 方法 | 功能 |
|------|------|------|
| `/scheduled_tasks` | GET | 获取所有任务列表 |
| `/scheduled_tasks` | POST | 创建新任务 |
| `/scheduled_tasks/<id>` | PUT | 更新任务（含启用/禁用） |
| `/scheduled_tasks/<id>` | DELETE | 删除任务 |
| `/scheduled_tasks/<id>/trigger` | POST | 立即手动触发执行 |

**任务执行流程：**

```
调度线程匹配 cron → 新线程执行 → SocketIO emit 指令到客户端 → 记录执行结果和时间
```

- 客户端离线时记录"客户端离线"状态，不会中断调度循环
- 支持三种操作：`shoot`（拍照）、`screenshot`（截图）、`execute`（执行命令）

**Cron 表达式格式：**

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

### 远程桌面（VNC）

客户端使用 `System.Drawing` 定期截取屏幕画面，通过 Socket.IO 以 base64 编码传输：

- 帧率：约 5-10 FPS（根据网络延迟自适应）
- 编码：JPEG，质量可配置
- 输入：鼠标点击/移动/滚轮 + 键盘事件，通过 `vnc_mouse_event` / `vnc_key_event` 发送
- 服务端纯转发，不做图像处理

### 客户端自安装机制

`Program.cs` 中的 `InstallToProgramFiles()` 实现了完整的自安装流程：

1. 检测当前是否已在安装目录（`C:\Program Files\RemoteCore\`）
2. 若不在，复制自身到安装目录，设置 `FileAttributes.Hidden | FileAttributes.System`
3. 创建 Windows 计划任务（`TaskScheduler`）实现开机自启
4. 启动新进程后退出当前进程
5. 使用 `Mutex` 确保单实例运行

### 客户端卸载机制

| 步骤 | 操作 |
|------|------|
| 1 | 删除计划任务（取消开机自启） |
| 2 | 释放 Mutex |
| 3 | 判断是否安装在 `Program Files\RemoteCore` |
| 3a | 是 → `attrib -h -s` 清除目录及文件属性 → `rmdir /s /q` 删除整个安装目录 |
| 3b | 否 → `attrib -h -s` 清除 exe 属性 → `del /f /q` 删除文件 |
| 4 | `ping 127.0.0.1 -n 3` 等待进程退出 → 执行删除命令 |

### 认证机制

- 服务端使用 HTTP Basic Auth，密码为 `AUTH_TOKEN`
- Web 控制台通过浏览器原生 Basic Auth 对话框登录
- 客户端在 Socket.IO 连接时通过 `auth` 参数传递 token
- 流式传输端点通过 URL 参数 `key`（`access_key`）验证，每个传输有独立密钥
- 所有 API 端点均受 Basic Auth 保护

### 进度条实现

Web 控制台中的文件传输进度条采用以下策略确保流畅：

- **精度**：百分比保留一位小数（如 49.3%），避免整数跳变
- **单调递增**：新进度值不低于上次显示值，防止视觉回退
- **节流**：进度有增长时立即更新 DOM，无增长时 200ms 节流兜底
- **CSS 过渡**：`transition: width 0.3s ease-out`，平滑过渡到目标宽度

---

## 目录结构

```
RemoteCore/
├── server/
│   ├── server.py            # 服务端主程序（Flask + SocketIO + 定时任务调度）
│   ├── requirements.txt     # Python 依赖
│   ├── .env.example         # 配置模板
│   └── test_content_disposition.py  # Content-Disposition 头测试
├── client/
│   ├── Program.cs           # 入口（自安装、自启动、互斥锁）
│   ├── RemoteClient.cs      # 客户端核心逻辑（SocketIO通信、文件传输、VNC）
│   └── RemoteCore.csproj    # .NET 8 项目文件
├── web/
│   └── index.html           # Web 控制台（单页应用）
├── .gitignore
└── LICENSE                  # MIT
```

**运行时生成的目录：**

```
storage/                     # 服务端数据目录（自动创建）
├── photos/                  # 摄像头照片
├── uploads/                 # 上传文件暂存
├── downloads/               # 下载文件暂存
└── scheduled_tasks.json     # 定时任务持久化
```

---

## 安全提醒

- 生产环境**必须**修改默认 `AUTH_TOKEN`
- 建议在服务端前部署 HTTPS 反向代理（Nginx/Caddy）
- 客户端以管理员权限运行，可执行任意命令，请确保部署环境受控
- 流式传输的 `access_key` 为一次性使用，传输完成后即失效

## License

[MIT](LICENSE)
