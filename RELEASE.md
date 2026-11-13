# 发布可执行程序

`.github/workflows/release.yml` 构建自带 .NET 8 运行时的程序，部署机器无需安装 dotnet。

平台：Linux x86_64（linux-x64）、Linux ARM64（linux-arm64）、Windows x64（win-x64）。Linux 使用 glibc 发行版，仍需系统原生依赖（如 OpenSSL、ICU）；不支持 32 位 Linux x86。

在 GitHub Actions 页面手动运行 Build and release，或将工作流变更提交到 main，会构建并发布自动版本 v1.1.<运行编号>。也可推送未使用的版本标签指定版本，例如：

```sh
git tag v1.1.0
git push origin v1.1.0
```

三个平台全部构建通过后，工作流自动创建该标签的 GitHub Release 并上传 ZIP / tar.gz 文件和 SHA256 校验文件。版本号仅为示例，请先检查已有标签。

下载后完整解压，Windows 运行 start.bat，Linux 运行 ./start.sh，访问 https://localhost:8088。程序是本地 Web 服务，数据界面通过浏览器打开。首次启动可能使用自签名 HTTPS 证书。

各版本的实际构建状态请查看 GitHub Actions；真实数据库连接需要在目标环境验证。
