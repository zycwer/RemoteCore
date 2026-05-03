# 文件传输显著提速：直通流式转发（边收边发）

## 背景与问题

当前文件传输均为“两段式串行”：

- 客户端 → 网页下载：client 上传到 server（落盘）→ browser 再从 server 下载
- 网页 → 客户端下发：browser 上传到 server（落盘）→ client 再从 server 下载

总耗时约为两段耗时之和（A+B），且中间落盘/二次读取增加 I/O 与等待。

目标是将总耗时逼近两段中的较大者（max(A,B)），并保持鉴权、进度与错误链路完整。

## 目标与非目标

### 目标

- 显著缩短大文件传输总耗时：从 A+B 降到 max(A,B)
- 服务端支持“直通流式转发”，默认不落盘（可选边转发边落盘）
- 保持现有鉴权（Bearer / Basic）可用
- 维持现有 Socket.IO 控制面能力（client 触发、web 展示）
- 兼容现有进度事件 `file_transfer_progress`、完成/错误事件

### 非目标

- 断点续传（Range / chunk resume）
- 多路并发分片
- P2P（WebRTC）

## 总体方案（推荐）

引入“transfer_id + 有界队列”的双向直通流式转发：

- 直通含义：server 在接收一端数据的同时，将数据持续写给另一端响应流
- 有界队列：避免内存无限增长；下游慢时阻塞上游，实现 backpressure
- 生命周期管理：transfer 超时/异常清理，避免泄漏

## 传输方向 1：客户端下载到网页（client → web）

### 新增流式端点

- `GET /stream/download/<transfer_id>?filename=...`
  - 由 browser 触发下载
  - server 从队列取 bytes 写入 Response（chunked）
  - 响应头设置 `Content-Disposition: attachment; filename=...`
- `POST /stream/upload_from_client/<transfer_id>?filename=...`
  - 由 client 发起（上传文件流）
  - server 读取请求体流，将 bytes 写入 transfer 队列

### 控制流

1. Web 点击“下载”
2. server 生成 `transfer_id`，记录 `web_sid/client_id/filename`
3. server 向 web_sid emit `file_transfer_started`（包含 transfer_id）
4. server 向 client emit `start_stream_upload`（包含 upload_url、transfer_id、path、filename）
5. browser 立即开始下载：导航到 `GET /stream/download/<transfer_id>?filename=...`
6. client 将文件以流式 POST 到 `POST /stream/upload_from_client/<transfer_id>`
7. server 边收边发，browser 直接接收并完成下载

### 与现有逻辑的关系

- 保留原 `/client_upload_file` + `file_download_ready` 作为 fallback（可通过配置或 client 能力判断切换）

## 传输方向 2：网页上传到客户端（web → client）

### 新增流式端点

- `POST /stream/from_web/<transfer_id>?filename=...`
  - 由 browser 上传
  - server 读取请求体流，将 bytes 写入 transfer 队列
- `GET /stream/to_client/<transfer_id>?filename=...&target_dir=...`
  - 由 client 拉取
  - server 从队列取 bytes 写入 Response（chunked）

### 控制流

1. Web 选择文件并点击“上传到客户端”
2. server 生成 `transfer_id`，记录 `web_sid/client_id/filename/target_dir`
3. server 向 web_sid emit `file_transfer_started`
4. server 向 client emit `start_stream_download`（包含 download_url、transfer_id、target_dir、filename）
5. client 立即请求 `GET /stream/to_client/<transfer_id>...` 并开始落盘
6. browser 将文件以流式 POST 到 `POST /stream/from_web/<transfer_id>`
7. server 边收边发，client 直接接收并完成落盘

### 与现有逻辑的关系

- 保留原 `/upload_to_client` + `/download_from_server/uploads/...` 作为 fallback

## 服务端实现细节

### 数据结构（内存态）

`transfers[transfer_id]` 记录：

- `created_at`
- `direction`：`client_to_web` / `web_to_client`
- `web_sid`、`client_id`
- `filename`、`target_dir`（可选）
- `queue`: `queue.Queue(maxsize=N)`（元素为 bytes chunk，或 sentinel 表示结束）
- `closed`: bool
- `error`: str|None

### 关键行为

- backpressure：queue 满时 `put()` 阻塞，迫使上游读慢
- end-of-stream：上游结束时放入 sentinel，通知下游结束
- cancel：下游断开时标记 closed，唤醒上游并尽快停止读写
- cleanup：定时或按请求触发清理过期 transfer（例如 10~30 分钟）

## 客户端实现细节（C#）

- 新增 Socket 事件：
  - `start_stream_upload`: 将指定文件以流式 POST 到 server
  - `start_stream_download`: 从 server GET 流式下载并落盘
- 文件传输仍使用独立的 `_fileHttpClient`（无限 HttpClient.Timeout + 自定义 CancellationToken 超时）
- 复用现有 `file_transfer_progress` 事件上报

## 前端实现细节（index.html）

- 下载（client → web）：在拿到 `file_transfer_started.transfer_id` 后，直接触发 `<a href="/stream/download/...">` 开始下载，并用 Socket 事件更新进度条
- 上传（web → client）：使用 `XMLHttpRequest` 对 `/stream/from_web/<transfer_id>` 上传，利用 `xhr.upload.onprogress` 更新“第一段”进度，Socket 事件更新“第二段”进度

## 安全与权限

- 继续复用现有 `Authorization: Bearer` / Basic Auth 验证
- 所有 stream 端点仅允许：
  - 具备合法 token 的请求
  - 且 transfer_id 必须存在并与发起方（web_sid/client_id）匹配（防止他人猜测 transfer_id）

## 可观测性

- 复用 `file_transfer_progress`，要求 payload 带 `transfer_id/stage/transferred/total`
- server 在异常/超时时向 web_sid emit 错误（并清理 transfer）

## 回滚与兼容策略

- 默认启用流式传输；若 client 未升级或 stream 端点异常：
  - client → web 回退到 `/client_upload_file` 落盘模式
  - web → client 回退到 `/upload_to_client` 落盘模式

