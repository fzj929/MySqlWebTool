# DataPilot · 数据库工作台

DataPilot（原 MySQL Web 工具）是 Vue 3 + ASP.NET Core 8 构建的轻量数据库管理工具。支持 MySQL、SQL Server、PostgreSQL、SQLite、达梦 DM8 和 Oracle，统一浏览对象、执行 SQL、查看结构与 DDL、分页查询和导出 CSV。生产模式默认仅提供 HTTPS。

## 数据库支持

| 数据库 | 连接方式 | 对象结构 | DDL |
| --- | --- | --- | --- |
| MySQL | 主机、端口、账号密码、SSL | 数据库 → 表/视图 | SHOW CREATE 定义及可配置的结构生成 |
| SQL Server | TCP 主机、端口、SQL 账号密码、加密及证书选项 | 数据库 → Schema → 表/视图 | 系统目录重建表、键、约束和常用索引；视图读取定义 |
| PostgreSQL | 主机、端口、账号密码、数据库、SSL Mode | 数据库 → Schema → 表/视图 | 系统目录重建，保留类型、表达式、约束及索引定义 |
| SQLite 3 | 上传 .db / .sqlite / .sqlite3 文件 | main → 表/视图 | sqlite_schema 中的表、索引、视图及触发器定义 |
| 达梦 DM8 | 主机、端口（默认 5236）、账号密码、可选模式 | 当前实例 → Schema → 表/视图 | DBMS_METADATA.GET_DDL 原生对象定义 |
| Oracle 19c+ | 主机、端口（默认 1521）、Service Name / SID、账号密码、可选 Schema | 当前服务 → Schema → 表/视图 | DBMS_METADATA.GET_DDL 原生对象定义 |

推荐 SQL Server 2012+、PostgreSQL 12+；具体服务器版本需要在部署环境验证。SQL Server 使用 SQL 身份认证，暂不支持 Windows 集成登录。SQLite 支持标准未加密数据库。

- SQL 编辑器支持 Ctrl+Enter、Tab 缩进、查询结果、影响行数和执行耗时。
- 写操作和 DDL 直接作用于目标数据库；SQL 使用各数据库自己的语法，不进行跨方言翻译。
- 批次执行全部语句，展示第一个返回结果集；支持 INSERT RETURNING、OUTPUT、PRAGMA 等返回结果的语句。SQL Server 的 GO 是客户端批次分隔符，编辑器中请分次执行，不要提交 GO。
- 表数据支持 WHERE、ORDER BY、分页与 CSV 导出；有主键时默认按主键排序，无唯一排序条件的表/视图应手动指定 ORDER BY，以获得稳定分页。
- 64 位整数和高精度 decimal 按字符串返回，避免浏览器丢失精度。
- “记住连接”默认勾选：点击连接且验证成功后，在当前浏览器保存完整连接参数和密码。下次从“连接管理”选择即可回填，也会自动回填最近使用的连接；兼容旧 MySQL 历史记录。密码保存在本机浏览器 localStorage 中，不在服务器保存。
- 取消“记住连接”仅表示本次不新增或更新记录；已有记录可通过“删除连接”移除。测试连接或连接失败不会保存。连接列表显示数据库类型、账号、主机、端口及数据库，最多保留最近 20 条。
- 数据库列表和结构受登录用户的数据库权限限制。
- SQL Server/PostgreSQL DDL 用于常见对象重建，界面注明来源和限制；分区、权限、触发器、序列依赖等完整迁移应使用数据库原生备份工具。DDL 不包含表数据。

## 达梦 DM8

选择“达梦 DM8”，填写主机、端口、用户和密码。模式可留空使用账号默认模式；指定时按服务器实际大小写填写，不加双引号。对象树列出有可见表/视图的模式以及当前模式。通过模式节点切换后，后续查询使用所选模式；每次请求独立连接，不跨请求保留事务或 SET SCHEMA 状态。

使用 NuGet `DM.DmProvider 8.3.1.47463` 的 net8.0 驱动，随 `dotnet publish` 自动打包。普通连接无需手工注册 GAC。特殊 SSL/第三方加密配置不在本版界面范围内。驱动来源：[NuGet](https://www.nuget.org/packages/DM.DmProvider/8.3.1.47463)，参考：[达梦 .NET 编程指南](https://eco.dameng.com/document/dm/zh-cn/pm/net-rogramming-guide.html)。

支持字段、索引、主键、数据分页、SQL 执行及 CSV 导出；GET_DDL 不可用时显示提示，不阻止数据浏览。原生 DDL 不等于完整备份，独立索引、触发器、权限及数据迁移请使用达梦原生工具。建议使用具有所需权限的专用账号，不要日常使用 SYSDBA 管理员账号。

已在 Windows 应用主机连接 DM8 8.1.4.6 完成只读联调：连接、模式与表列表、字段/索引/主键、GET_DDL、分页 SQL、CSV 表头、BIGINT、38 位 DECIMAL、中文 CLOB 和 NULL。未执行写入或 Linux 实机测试，其他目标版本/系统仍需验收。`tests/smoke_dm8_readonly.py` 从进程环境 DM8_TEST_HOST、DM8_TEST_PORT、DM8_TEST_USER、DM8_TEST_PASSWORD 读取凭据，不保存到文件；分页与导出使用空条件，不读取业务行。

如需写入回归，可在忽略提交的 `tests/connections.local.json` 中配置 `databaseType: "dm8"`、host、port、user、password、schema，然后运行 `tests/smoke_remote.py --connections tests/connections.local.json`。该测试会在指定测试模式内创建随机名称的表和索引、写入测试数据，结束后删除测试表；勿指向生产模式。

## 连接字符串页面

“表结构”右侧的“连接字符串”页由服务器按当前顶部参数生成实际驱动连接字符串。默认隐藏，勾选“显示明文（含密码）”后可多行编辑。测试按钮仅验证编辑后的字符串能否连接并执行简单探测，不切换对象浏览器使用的连接，也不保存草稿；“恢复默认连接字符串”按顶部最新参数重新生成。切换数据库或修改顶部参数会清除旧草稿和测试状态。

测试仅允许默认字符串中已开放的字段，连接池关闭并限制超时；不允许服务器文件/插件相关选项。SQLite 路径、访问模式由顶部已上传文件及只读选项决定，不可通过此页绕过限制。Oracle CURRENT_SCHEMA 是连接后的会话设置，不包含在连接字符串中。含密码的响应禁止缓存，测试错误不回显原始驱动异常。测试页面覆盖六种驱动的默认串、SQLite 实际连接与路径保护，其他驱动使用同一连接探测流程。

## Oracle 使用说明

使用官方 `Oracle.ManagedDataAccess.Core 23.26.300`，发布时自动携带，无需另装 Oracle 客户端。选择 Service Name（例如 XE 21c 的 XEPDB1）或 SID（例如 XE），填写普通数据库账号和密码，不支持 SYSDBA 特权登录/Wallet/TCPS 配置。模式留空使用账号默认模式，指定时保留实际大小写、不加双引号。当前服务不可通过对象树切换为其他 PDB；要切换服务请修改连接参数重新连接。

每次执行一条 SQL 或一个完整 PL/SQL 块；普通 SQL 末尾分号会去除，PL/SQL 的 END; 保留。不支持 SQL*Plus 的 `/`、CONNECT、SET 或多个独立语句拼接执行。NUMBER 使用驱动高精度表示后返回字符串；分页使用 OFFSET/FETCH FIRST。对象、字段、注释、索引、主键和 DDL 受当前账号权限限制，GET_DDL 失败不阻止查看字段。完整迁移请使用 Data Pump 等原生工具。

当前已通过 Oracle 驱动参数/方言自检和 HTTPS 下的模拟界面测试；实际 Oracle 服务器端口尚未就绪，实机查询、元数据与写入暂未验收。只读联调脚本 `tests/smoke_oracle_readonly.py` 从进程环境 ORACLE_TEST_HOST、ORACLE_TEST_USER、ORACLE_TEST_PASSWORD、ORACLE_TEST_SERVICE 读取参数，并通过 `--cafile` 严格验证 HTTPS。

## SQLite 文件管理

在顶部选择 SQLite，上传文件，选择数据库后点击连接。可切换只读访问、下载包含修改的数据库快照、删除数据库文件。同名上传生成不同 ID，不覆盖已有文件；替换时上传新文件并确认内容后删除旧文件。

默认每文件最大 100 MB，总上传存储配额 1 GB；上传时检查 SQLite 文件头和完整性。上传正在运行的 WAL 数据库时，单独复制主 .db 文件可能缺少尚未合并的数据：请先关闭源数据库或使用备份 API 生成独立快照，再上传。

文件默认存储于应用目录的同级 `DataPilot-data` 目录，位于 wwwroot 和发布包之外。此目录需要单独备份，升级程序不会删除它。部署为服务时运行账户必须有读写权限；Linux 安装脚本会创建目录并授予运行用户权限。

SQLite 查询禁用 ATTACH、VACUUM INTO、加载扩展及危险的文件相关 PRAGMA；只能访问选中的托管数据库。SQLite 操作在本服务中串行协调，适合轻量管理，不适合高并发写入。上传配额在上传时检查，SQL 写入后的文件增长仍需由主机磁盘配额管理。

## 开发

构建机需要 .NET 8 SDK（或支持 net8.0 的更新 SDK）与 Node.js 20+。

```powershell
# 后端
dotnet run --project server/DataPilot.Api.csproj --urls http://localhost:8088

# 另一个终端启动前端
cd client
npm install
npm run dev
```

浏览器打开 <http://localhost:5173>。Windows 也可以运行 `start-dev.bat`。

## 发布与部署

```bat
publish.bat
```

生成 `publish/DataPilot`，包含前后端、Windows/Linux 启动和服务管理脚本及本说明。将完整目录复制到最终部署位置。目标主机需要 **ASP.NET Core 8 Runtime**，无需 Node.js 或 SDK。

### HTTPS 与证书

生产启动和服务脚本默认使用端口 8088。访问 `https://data.mybips.com:8088`；访问 `http://data.mybips.com:8088` 会在同一端口返回 307，跳转到 HTTPS，并保留路径和查询参数。HTTP 不执行页面或 API 操作。若使用 443 端口则 HTTPS 地址可省略端口号。未指定端口的 HTTP 地址默认连接 80，本程序不会额外监听 80。DNS 必须指向部署主机，必要时自行配置端口映射/防火墙；脚本不会改动 DNS、防火墙或信任证书库。

请直接使用 HTTPS 地址输入凭据：跳转无法保护已经通过 HTTP 发出的密码。自签名证书仍需在客户端信任。已有服务若固定了旧端口，需按新脚本重新安装并指定 8088；更新配置文件不会覆盖服务命令行中的端口。

Windows 服务可用 `install-service.bat -Port 443`，Linux 服务可用 `sudo ./install-service.sh DataPilot 443 www-data`。Linux 服务脚本对低于 1024 的端口授予绑定低位端口所需的 capability；普通命令行启动 443 仍需相应权限。

首次启动自动生成 RSA 3072 / SHA-256 自签名证书，组织机构为“深圳市纷享科技有限公司”，有效期精确为签发起点起 10 年。SAN 包含 `data.mybips.com`、localhost、部署主机名称和活动网卡 IP；不会在发布时预置共享私钥。也可以部署后手动执行：

```bat
create-certificate.bat data.mybips.com
```

```bash
bash create-certificate.sh data.mybips.com
```

多个地址使用逗号分隔。证书存放在程序目录同级的 `DataPilot-data/certificates`，不在 wwwroot 中。`server.pfx` 包含私钥，受 Windows ACL 或 Linux 0700 目录/0600 文件权限保护，无硬编码密码；**不要把 PFX 发给客户端或提交 Git**。`server.crt` 仅包含公钥证书，可通过可信渠道分发给客户端，核对指纹后安装到客户端受信任证书库。自签名不会自动获得浏览器信任，不能把“忽略警告”当成完成可信部署；10 年证书若泄露应立即更换。

Windows 可双击公钥 `server.crt`，按组织策略导入当前用户的“受信任的根证书颁发机构”；Linux 使用发行版的系统 CA 管理方式，Firefox 等浏览器可能需要单独导入。仅信任核对过指纹的证书。HTTPS 的 localStorage 与原 HTTP 地址隔离，旧地址保存的连接不会自动迁移，需要在 HTTPS 下重新保存一次。

已有证书绝不自动覆盖。更换域名/IP或重新签发时，先停止服务，将原 `certificates` 目录移到安全备份位置，再生成新证书并重新分发公钥。可通过 `DataPilot:Https:CertificateDirectory` 指定一个独立、名为 certificates 的目录；Linux 服务用户必须拥有该目录。Windows 更换服务账号前需授予新账号证书目录及私钥的读取权限。

旧服务若使用 `--urls http://...`，升级时需卸载后按新脚本重新安装，才能保留自定义端口；不要只重启旧服务。仅 Development 模式保留 HTTP 供本地开发与测试，禁止以该模式对外部署。本功能保护浏览器到 DataPilot 的通信，数据库链路加密须另行配置。工具仍无登录鉴权，不应直接暴露到公网，域名建议通过内网/VPN访问。

```powershell
./publish.ps1 -OutputDirectory "D:/release/DataPilot"
# 指定运行时可生成匹配目标平台的原生依赖（SQLite/SqlClient）
./publish.ps1 -RuntimeIdentifier "linux-x64"
```

默认使用框架依赖发布；不要仅复制 DLL，需保留 runtimes 等全部依赖。已有 DataPilot 发布目录会保留为 `.previous-<id>` 同级目录，确认后可手动清理；脚本拒绝覆盖未标记的现有目录。

普通启动：

```bat
start.bat 8088
```

```bash
bash start.sh 8088
```

### Windows Service

在发布目录中使用管理员终端：

```bat
install-service.bat -ServiceName DataPilot -Port 8088
start-service.bat
stop-service.bat
restart-service.bat
uninstall-service.bat
```

默认服务名 DataPilot；自定义名称时各管理脚本都传 `-ServiceName 名称`。安装后自动启动并设置开机启动和失败重启。默认以 LocalSystem 运行，可在 Windows 服务管理器改为具备所需目录权限的专用账户。

从旧版本升级时，先停止并卸载旧 MySqlWebTool 服务，再安装 DataPilot；不要同时启动占用同一个端口。服务安装后不要移动应用目录。

### Linux systemd

```bash
chmod +x *.sh
sudo ./install-service.sh DataPilot 8088 www-data
sudo ./start-service.sh
sudo ./stop-service.sh
sudo ./restart-service.sh
sudo ./uninstall-service.sh
```

安装参数为：服务名、端口、已存在的运行用户。省略时默认 DataPilot、8088、执行 sudo 的用户（直接以 root 执行则为 root）。自定义服务名后，其余管理脚本也传入第一个参数服务名。

如果普通用户能运行 dotnet，但 sudo 后提示找不到，通常是 sudo 重置了 PATH。脚本会检查常见安装目录以及原用户的 ~/.dotnet；非标准安装目录可明确传入：

```bash
sudo env DOTNET_PATH="$(command -v dotnet)" ./install-service.sh
# 或作为第四个安装参数指定（该用户必须能读取并执行此路径）
sudo ./install-service.sh DataPilot 8088 "$(id -un)" /opt/dotnet/dotnet
```

安装前会以服务运行用户的身份检查运行时，要求同时存在 Microsoft.NETCore.App 8.x 与 Microsoft.AspNetCore.App 8.x。仅能执行 dotnet 帮助命令不代表已安装 ASP.NET Core 运行时。

```bash
systemctl status DataPilot
journalctl -u DataPilot -f
```

卸载服务不会删除应用和 SQLite 数据。

## 配置

`server/appsettings.json` 中保留旧版 `MySqlTool` 配置段以兼容已有设置，该段现在应用于全部数据库：

```json
{
  "MySqlTool": {
    "MaxRows": 1000,
    "MaxExportRows": 50000,
    "CommandTimeoutSeconds": 60,
    "ConnectTimeoutSeconds": 10
  },
  "DataPilot": {
    "MaxUploadBytes": 104857600,
    "MaxStorageBytes": 1073741824
  }
}
```

可通过 `DataPilot:SqliteDirectory` 配置独立数据目录，建议使用绝对路径；环境变量名称为 `DataPilot__SqliteDirectory`。例如 Linux 服务中设置为 `/var/lib/datapilot`。数据目录不能位于 wwwroot。

## API 与验证

`/api/connection/test`、`/api/schema/databases`、`/api/schema/schemas`、`/api/schema/tables`、`/api/schema/table`、`/api/query`、`/api/query/data`、`/api/query/export` 使用 POST。连接类型 databaseType 为 mysql/sqlserver/postgresql/sqlite/dm8/oracle，表相关请求包含 schema。Oracle 的 database 表示服务名或 SID，oracleConnectionType 为 service 或 sid。SQLite 使用服务端文件 ID fileId，不接受客户端路径。

SQLite 文件 API：GET/POST `/api/sqlite/files`、DELETE `/api/sqlite/files/{id}`、GET `/api/sqlite/files/{id}/download`。上传字段名为 `file`。开发环境提供 Swagger。

```powershell
dotnet build server/DataPilot.Api.csproj
cd client
npm run build
# 在项目根目录，对已启动的测试实例执行 API 回归
python tests/smoke_api.py --base http://127.0.0.1:5099
```

测试在临时目录生成 SQLite 数据库，检查文件、结构、分页、DDL、CSV、只读访问和路径限制，并删除自己上传的测试文件。

Windows 可执行 `./tests/run_checks.ps1 -Python python`，自动从发布目录启动临时实例并执行 API 和浏览器回归；需要安装 Python Playwright（`python -m pip install playwright`）及 Microsoft Edge。截图和日志输出到 `tests/.artifacts`。

真实 MySQL、SQL Server、PostgreSQL 的回归入口为 `python tests/smoke_remote.py --connections tests/connections.local.json`。该文件是连接对象数组，每项包含 databaseType、host、port、user、password、database，可包含 schema/encrypt/trustServerCertificate/sslMode。请指定专用测试数据库；测试只创建随机命名的测试表和索引，结束时删除自己的表。该本地配置文件已加入 Git 忽略。

## 使用范围

DataPilot 是可信内网的数据库管理工具，没有应用层用户登录或文件用户隔离。同一部署的使用者可管理所有已上传文件，SQL 执行权限取决于提供的数据库账号。请限制网络访问，使用低权限数据库账号；如需多人隔离或公网访问，须先配置身份认证、访问控制和 HTTPS。不要直接暴露公网。

## 目录

- `server/`：DataPilot.Api，连接与元数据适配、查询执行、SQLite 文件管理。
- `client/`：Vue 3 + Vite + Element Plus 工作台。
- `publish-assets/`：随发布包复制的启动与服务脚本。
- `tests/`：API 和浏览器验证。
- `db/init.sql`、`docker-compose.yml`：可选 MySQL 演示数据库。

## 开源协议

[MIT License](LICENSE)。使用、修改和分发请保留版权声明。
