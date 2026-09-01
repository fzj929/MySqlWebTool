#!/usr/bin/env bash
# MySQL Web 工具启动脚本
# 用法： ./start.sh [端口]，默认 5080

cd "$(dirname "$0")" || exit 1

PORT=${1:-5080}

echo "============================================"
echo " MySQL Web 工具"
echo " 访问地址: http://localhost:${PORT}"
echo " 停止服务: Ctrl + C"
echo "============================================"
echo

exec dotnet MySqlTool.Api.dll --urls "http://0.0.0.0:${PORT}"
