# MySQL Web 工具（Vue 3 + .NET 8）

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

一个轻量、开箱即用的 MySQL 网页版管理工具：连接管理、数据库/表浏览、表结构查看、SQL 执行、分页数据浏览、CSV 导出。

- 前端：Vue 3 + Vite + Element Plus
- 后端：ASP.NET Core 8 Minimal API + MySqlConnector
- 目标框架：**net8.0**

## 目录结构

```
MySqlWebTool/
├─ server/                    # .NET 8 Web API
│  ├─ Program.cs              # 启动、CORS、静态文件、SPA 回退
│  ├─ Endpoints/ApiEndpoints.cs
│  ├─ Services/MySqlService.cs # 连接/结构/查询/分页/导出
│  ├─ Services/SqlIdentifier.cs
│  ├─ Models/Contracts.cs
│  └─ appsettings.json
├─ client/                    # Vue 3 前端
│  └─ src/
│     ├─ App.vue
│     ├─ api/index.js
│     └─ components/          # ConnectionBar / ObjectTree / QueryPanel / TableDataPanel / StructurePanel / ResultTable
├─ db/init.sql                # 演示数据（配合 docker-compose 使用）
├─ docker-compose.yml         # 可选的一键测试数据库
└─ start-dev.bat              # 一键启动前后端
```

## 功能

| 功能 | 说明 |
| --- | --- |
| 连接管理 | 主机/端口/账号/密码/默认库/SSL；测试连接；**断开连接**；历史连接存浏览器本地（可勾选记住密码） |
| 对象浏览器 | 懒加载数据库与表（区分表与视图），支持名称过滤；连接成功后立即展示，无需二次点击 |
| SQL 查询 | Ctrl+Enter 执行，Tab 缩进，最大返回行数可调，显示耗时与截断提示，支持写操作（显示影响行数） |
| 数据浏览 | 选中表自动分页浏览，支持自定义 WHERE / ORDER BY、每页条数、总数统计 |
| 表结构 | 字段（类型/可空/默认值/键/注释）、索引 |
| DDL 生成 | 两种来源：按结构本地生成 / 服务端 SHOW CREATE TABLE 原文；可勾选 IF NOT EXISTS、前置 DROP TABLE、包含索引、包含注释、包含 AUTO_INCREMENT、带库名前缀；支持复制、下载 .sql、发送到 SQL 查询面板 |
| 导出 | SQL 结果或整表数据导出为 CSV（UTF-8 BOM，Excel 可直接打开） |

> 提示：所有提示信息统一显示在**右下角**，不会遮挡顶部的账号密码输入框；连接错误还会在连接栏内联展示，可手动关闭。

## 启动

### 方式一：一键脚本（Windows）

```
start-dev.bat
```

### 方式二：手动启动

```bash
# 后端（监听 5080）
cd server
dotnet run --urls http://localhost:5080

# 前端（监听 5173，已配置 /api 代理到 5080）
cd client
npm install
npm run dev
```

浏览器打开 <http://localhost:5173>。

### 生产模式（可选）

在安装了 Node.js 和 .NET 8 SDK 的 Windows 发布机器上运行：

```bat
publish.bat
```

脚本会构建前端与后端，并将可部署文件统一输出到 `publish/MySqlWebTool`。这是依赖框架的发布包，不包含 .NET 运行时；将整个目录复制到已安装 **ASP.NET Core 8 Runtime**（包含 .NET 8 Runtime）的目标主机即可。

```bat
:: Windows，端口默认为 5080
start.bat
start.bat 8080
```

```bash
# Linux，端口默认为 5080
chmod +x start.sh
./start.sh 8080
```

也可以直接调用 PowerShell 脚本指定输出目录或目标运行时标识：

```powershell
./publish.ps1 -OutputDirectory "D:/release/MySqlWebTool"
./publish.ps1 -RuntimeIdentifier "win-x64"
```

### 部署为系统服务

发布目录中会同时包含 Windows Service 和 Linux systemd 管理脚本。安装服务前，请先把整个发布目录复制到最终位置；服务配置会记录该绝对路径，安装后不要再移动目录。

#### Windows Service

使用“以管理员身份运行”的命令提示符进入发布目录：

```bat
:: 安装、设置开机启动并立即启动（默认服务名 MySqlWebTool、端口 5080）
install-service.bat

:: 自定义服务名和端口
install-service.bat -ServiceName MySqlWebTool -Port 8080

start-service.bat
stop-service.bat
restart-service.bat
uninstall-service.bat
```

管理自定义名称的服务时需要继续传入名称，例如 `restart-service.bat -ServiceName MySqlWebTool`。

#### Linux systemd

```bash
chmod +x *.sh

# 默认服务名 MySqlWebTool、端口 5080；默认以执行 sudo 的用户运行
sudo ./install-service.sh

# 自定义服务名、端口和运行用户
sudo ./install-service.sh MySqlWebTool 8080 www-data

sudo ./start-service.sh
sudo ./stop-service.sh
sudo ./restart-service.sh
sudo ./uninstall-service.sh
```

自定义服务名后，管理脚本需要将服务名作为第一个参数，例如 `sudo ./restart-service.sh MySqlWebTool`。可通过 `systemctl status MySqlWebTool` 查看状态，通过 `journalctl -u MySqlWebTool -f` 查看日志。

## API

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/health` | 健康检查 |
| POST | `/api/connection/test` | 测试连接，返回服务版本与耗时 |
| POST | `/api/schema/databases` | 数据库列表 |
| POST | `/api/schema/tables` | 指定库的表/视图列表（含行数与体积） |
| POST | `/api/schema/table` | 表结构：字段、索引、建表语句 |
| POST | `/api/query` | 执行 SQL（查询返回结果集，写操作返回影响行数） |
| POST | `/api/query/data` | 分页读取表数据（支持 WHERE / ORDER BY） |
| POST | `/api/query/export` | 导出 CSV |

开发环境下可访问 <http://localhost:5080/swagger> 查看接口文档。

## 配置

`server/appsettings.json`：

```json
"MySqlTool": {
  "MaxRows": 1000,          // 单次查询默认最大返回行数
  "MaxExportRows": 50000,   // 导出最大行数
  "CommandTimeoutSeconds": 60,
  "ConnectTimeoutSeconds": 10
}
```

## 安全提示

- 该工具可执行任意 SQL，**请勿部署到公网或不可信网络**，仅限本机/内网使用。
- 服务端不保存密码；连接信息由前端每次请求携带，历史连接保存在浏览器 localStorage。
- 数据浏览面板的 WHERE / ORDER BY 属于原始 SQL 片段，仅应由可信使用者填写。

## 演示数据库（可选）

需要 Docker Desktop 处于运行状态：

```bash
docker compose up -d
```

将创建 `demo` 库以及 `customers`、`orders` 两张示例表（root / root123456）。

## 开源协议

本项目基于 [MIT License](LICENSE) 开源，可自由使用、修改、分发与商用，无需授权，但请保留版权声明。
