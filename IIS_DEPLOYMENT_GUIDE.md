# SportTracker - IIS 发布指南

## 前置条件

1. **Windows 服务器**（已安装 IIS）
2. **.NET 10 Runtime** 或 **.NET 10 Hosting Bundle**（在服务器上安装）
   - 下载地址：https://dotnet.microsoft.com/download/dotnet/10.0
   - 选择 "Hosting Bundle"
3. **IIS Manager** 已启用

## 发布步骤

### 第一步：在开发机器上发布应用

在 `/Users/sunny/sport` 目录下运行：

```bash
dotnet publish -c Release -o publish
```

这会生成一个 `publish` 文件夹，包含所有必要的文件。

### 第二步：将发布文件复制到服务器

将 `publish` 文件夹中的所有文件复制到 Windows 服务器上，例如：
- `C:\inetpub\wwwroot\SportTracker`

### 第三步：在 IIS 中创建应用

1. 打开 **IIS Manager**
2. 在左侧展开"Sites"，右键点击"Default Web Site"（或创建新站点）
3. 选择"Add Application"
4. 填写信息：
   - **Alias**: `sporttracker` （或其他名称）
   - **Physical path**: `C:\inetpub\wwwroot\SportTracker`
5. 点击"OK"

### 第四步：配置应用程序池

1. 选中新创建的应用程序
2. 在 IIS Manager 右侧的"Manage Application"中选择"Advanced Settings"
3. 确保：
   - **.NET CLR version**: 选择 "No Managed Code"（重要）
   - **Managed pipeline mode**: 选择 "Integrated"

### 第五步：配置数据库权限

SQLite 数据库文件需要写入权限。确保 IIS 应用程序池的身份（通常是 `IIS AppPool\SportTracker`）对 `sport.db` 文件所在目录有写入权限：

1. 右键点击应用文件夹
2. 选择"Properties" > "Security"
3. 编辑权限，为应用程序池身份添加"Modify"权限

### 第六步：配置 HTTPS（可选但推荐）

1. 在 IIS Manager 中选中应用
2. 在右侧面板中双击"SSL Settings"
3. 根据需要配置 SSL 证书

## 验证

访问应用：
- 本地：`http://localhost/sporttracker`
- 远程：`http://<server-ip>/sporttracker`

## 故障排除

### 502 Bad Gateway 错误
- 检查 .NET Runtime 是否已安装
- 重启 IIS：`iisreset /restart`
- 查看事件查看器中的错误日志

### 数据库访问被拒绝
- 检查应用程序池身份的文件权限
- 确保 `sport.db` 所在文件夹可写

### 静态文件404
- 确保 `wwwroot` 文件夹中的文件已复制到服务器

## 日志位置

应用日志默认输出到 `logs` 文件夹（在发布的应用目录中）。

## 更新应用

发布新版本时：
1. 在开发机器上运行 `dotnet publish -c Release -o publish`
2. 停止 IIS 应用程序池
3. 使用新文件替换服务器上的旧文件
4. 启动应用程序池
