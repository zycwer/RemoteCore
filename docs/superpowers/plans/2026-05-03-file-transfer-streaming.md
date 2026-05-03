# File Transfer Streaming Acceleration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current two-hop file transfers with server-side streaming proxy so end-to-end time approaches `max(upstream, downstream)` instead of `sum(upstream, downstream)`.

**Architecture:** Add streaming endpoints backed by a bounded in-memory queue keyed by `transfer_id`. Web/client open read endpoints while the opposite side uploads; the server forwards bytes as they arrive and applies backpressure.

**Tech Stack:** Flask 3, Flask-SocketIO 5, Werkzeug 3, C# .NET 8 HttpClient, SocketIOClient, plain HTML/JS.

---

## File Map

**Modify**
- [server.py](file:///workspace/server.py): add transfer registry, streaming routes, new Socket.IO events, cleanup
- [RemoteClient.cs](file:///workspace/CSharpClient/RemoteClient.cs): add `start_stream_upload` / `start_stream_download` handlers and streaming transfer logic
- [index.html](file:///workspace/index.html): switch file transfer UI to use streaming endpoints (download and upload)

**Create**
- `docs/superpowers/plans/2026-05-03-file-transfer-streaming.md` (this file)

---

### Task 1: Implement server-side streaming transfer registry + endpoints

**Files:**
- Modify: [server.py](file:///workspace/server.py)

- [ ] **Step 1: Add transfer registry structures**

Add:
- `Transfer` record fields: `transfer_id`, `web_sid`, `client_id`, `filename`, `target_dir`, `direction`, `created_at`, `queue`, `closed`, `error`
- Bounded queue: `queue.Queue(maxsize=64)` and a sentinel `None` for EOF

- [ ] **Step 2: Add cleanup for expired transfers**

Add:
- A helper `cleanup_transfers()` that deletes transfers older than e.g. 30 minutes or marked closed
- Call it opportunistically at the start of streaming routes and when creating new transfers

- [ ] **Step 3: Add streaming routes**

Add these Flask routes (all must validate auth + validate that caller matches transfer ownership):
- `GET /stream/download/<transfer_id>`: web reads; returns `Response(generator(), mimetype="application/octet-stream")` and sets `Content-Disposition`
- `POST /stream/upload_from_client/<transfer_id>`: client writes; reads `request.stream` chunk-by-chunk, `queue.put(chunk)`, then `queue.put(None)` on EOF
- `GET /stream/to_client/<transfer_id>`: client reads; `Response(generator())` writes chunks from queue to client
- `POST /stream/from_web/<transfer_id>`: web writes; reads `request.stream` chunk-by-chunk into queue; `queue.put(None)` on EOF

Chunk size: `128 * 1024`.

- [ ] **Step 4: Add Socket.IO control events**

Modify:
- `request_download` handler to create `transfer_id`, store mapping, emit:
  - `file_transfer_started` to `web_sid`
  - `start_stream_upload` to `client_id` with `{transfer_id, path, filename, upload_url}`
- Add server handling for push-to-client (replace `/upload_to_client` path in UI):
  - Create a lightweight API endpoint (or reuse existing POST with a new flag) that allocates `transfer_id`, emits:
    - `file_transfer_started` to `web_sid`
    - `start_stream_download` to `client_id` with `{transfer_id, filename, target_dir, download_url}`

- [ ] **Step 5: Keep fallback routes**

Keep existing endpoints and behavior intact:
- `/client_upload_file` → `file_download_ready`
- `/upload_to_client` → `download_from_server/uploads/...`

But update the web UI to prefer streaming; fallback can be manual via a toggle if needed.

- [ ] **Step 6: Manual verification (server-only)**

Run:
```bash
python -m py_compile server.py
python server.py
```

In another shell, do a local self-check of queue streaming (no browser/client required):
```bash
# Terminal A: start a "reader"
curl -u any:admin123 -o /tmp/out.bin "http://127.0.0.1:5000/stream/download/test123?filename=test.bin" &

# Terminal B: write some bytes
dd if=/dev/urandom bs=1024 count=256 | curl -u any:admin123 -X POST --data-binary @- "http://127.0.0.1:5000/stream/upload_from_client/test123?filename=test.bin"

# Check size (should be 262144)
wc -c /tmp/out.bin
```

- [ ] **Step 7: Commit**

```bash
git add server.py
git commit -m "feat(server): add streaming file transfer endpoints"
```

---

### Task 2: Implement client streaming upload/download handlers (C#)

**Files:**
- Modify: [RemoteClient.cs](file:///workspace/CSharpClient/RemoteClient.cs)

- [ ] **Step 1: Add socket event handlers**

Register:
- `start_stream_upload`: POST file stream to `upload_url` with `transfer_id`
- `start_stream_download`: GET `download_url` and stream to disk to `target_dir/filename`

- [ ] **Step 2: Implement streaming POST for upload**

Requirements:
- Use `_fileHttpClient` + `HttpRequestMessage` with `StreamContent(File.OpenRead(path))`
- Use `HttpCompletionOption.ResponseHeadersRead` where relevant
- Emit `file_transfer_progress` (`stage=upload_to_server`) periodically
- Ensure cancellation via `CancellationTokenSource(FileTransferTimeout)`

- [ ] **Step 3: Implement streaming GET for download**

Requirements:
- Use `_fileHttpClient.GetAsync(..., ResponseHeadersRead)`
- Stream to file with a 128KB buffer
- Emit `file_transfer_progress` (`stage=download_from_server`) periodically
- Emit `client_download_complete` with `{transfer_id, success/path}` or `{transfer_id, error}`

- [ ] **Step 4: Build verification (in a .NET 8 environment)**

Run:
```bash
dotnet build CSharpClient/RemoteCore.csproj -c Release
```

- [ ] **Step 5: Commit**

```bash
git add CSharpClient/RemoteClient.cs
git commit -m "feat(client): support streaming file transfers"
```

---

### Task 3: Update web UI to use streaming endpoints + show progress

**Files:**
- Modify: [index.html](file:///workspace/index.html)

- [ ] **Step 1: Download flow (client → web)**

Change:
- On `file_transfer_started` (direction `client_to_web`), immediately trigger browser download:
  - `href = /stream/download/<transfer_id>?filename=<encoded>`
  - Keep progress UI bound to `file_transfer_progress` with the same `transfer_id`

- [ ] **Step 2: Upload flow (web → client)**

Change:
- Replace POST `/upload_to_client` with POST `/stream/from_web/<transfer_id>?filename=...&client_id=...&target_dir=...&web_sid=...` (or use a small allocator endpoint first)
- Use `XMLHttpRequest.upload.onprogress` for browser→server progress
- Use Socket `file_transfer_progress` for server→client progress

- [ ] **Step 3: Manual verification**

Run server and open:
- `http://127.0.0.1:5000/`

Validate:
- Downloading a file shows a progress bar immediately and completes without waiting for server-side “ready” message.
- Uploading a file shows upload progress, then switches to client receiving progress.

- [ ] **Step 4: Commit**

```bash
git add index.html
git commit -m "feat(web): switch file transfers to streaming"
```

---

### Task 4: Push to GitHub main (no force)

- [ ] **Step 1: Ensure branch is up-to-date**

```bash
git fetch origin
git status -sb
```

- [ ] **Step 2: Merge or rebase as needed (no force push)**

```bash
git checkout main
git merge <feature-branch>
```

- [ ] **Step 3: Push**

```bash
git push origin main
```

