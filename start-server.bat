@echo off
REM Windows 启动服务端的脚本

echo 正在启动 RemoteCore 服务端...

REM 检查是否存在虚拟环境
if not exist "server\venv" (
    echo 创建虚拟环境...
    python -m venv server\venv
)

REM 激活虚拟环境
call server\venv\Scripts\activate.bat

REM 安装依赖
echo 安装依赖...
pip install -r server\requirements.txt

REM 检查 .env 文件
if not exist "server\.env" (
    echo 复制示例配置文件...
    copy server\.env.example server\.env
    echo 请修改 server\.env 文件中的配置，特别是 AUTH_TOKEN
)

REM 启动服务
echo 启动服务端...
cd server
python server.py
