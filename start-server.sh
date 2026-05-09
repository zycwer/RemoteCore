#!/bin/bash
# 启动服务端的脚本

echo "正在启动 RemoteCore 服务端..."

# 检查是否存在虚拟环境
if [ ! -d "server/venv" ]; then
    echo "创建虚拟环境..."
    python3 -m venv server/venv
fi

# 激活虚拟环境
source server/venv/bin/activate

# 安装依赖
echo "安装依赖..."
pip install -r server/requirements.txt

# 检查 .env 文件
if [ ! -f "server/.env" ]; then
    echo "复制示例配置文件..."
    cp server/.env.example server/.env
    echo "请修改 server/.env 文件中的配置，特别是 AUTH_TOKEN"
fi

# 启动服务
echo "启动服务端..."
cd server && python server.py
