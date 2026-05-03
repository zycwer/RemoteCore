# RemoteCore

RemoteCore 是一个用于授权场景的远程管理工具：Python 服务端提供 Web 控制台，Windows 客户端常驻连接并执行指令（摄像头/截图/文件/命令/远程桌面等）。

> 仅限学习、测试与明确授权的运维管理用途。禁止在未授权设备上部署使用，后果自负。

## 目录结构

- `server/`：Flask + SocketIO 服务端（含流式文件直通）
- `web/`：Web 控制台（静态页）
- `client/`：Windows 客户端（.NET 8）

服务端运行时文件默认落在 `server/storage/`（已加入 `.gitignore`）。

## 快速开始

### 1) 启动服务端

```bash
pip install -r server/requirements.txt
python server/server.py
```

浏览器访问 `http://<server>:5000`。

### 2) 构建并运行客户端（Windows）

```bash
dotnet publish client/RemoteCore.csproj -c Release -r win-x64
```

运行生成的 `RemoteCore.exe`（建议管理员权限运行）。

## 配置

### 服务端环境变量

- `AUTH_TOKEN`：鉴权 token（必配，默认 `admin123` 仅用于开发）
- `CORS_ORIGINS`：允许跨域来源（默认 `*`）
- `STREAMING_ENABLED`：是否启用文件直通流式传输（默认 `1`）

示例见 [server/.env.example](file:///workspace/server/.env.example)。

### 客户端环境变量

- `REMOTECORE_SERVER_URL`：服务端地址（默认 `http://127.0.0.1:5000`）
- `REMOTECORE_AUTH_TOKEN`：鉴权 token（需与服务端 `AUTH_TOKEN` 一致）

## 登录

Web 端使用 Basic Auth：用户名任意，密码为 `AUTH_TOKEN`。

## 文件传输说明

- 新版默认使用“直通流式传输”（边收边发），大文件耗时接近 `max(上行, 下行)`。
- 旧客户端未升级时会自动回退到兼容模式。

