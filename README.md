# 运动记录

一个适合手机访问的小型家庭运动记录应用。主页为哥哥和弟弟分别生成每天 5 项随机运动目标，完成后可单项提交；记录页支持按日期、用户、项目筛选。

## 运行

需要安装 .NET 9 SDK。

```bash
dotnet restore
dotnet run
```

打开终端中显示的本地地址，例如 `http://localhost:5000` 或 `https://localhost:5001`。

默认开发配置也提供了固定地址：

- `http://localhost:7335`

## GitHub Codespaces

这个仓库已经带了 `.devcontainer` 配置，可以直接在 GitHub Codespaces 里打开。

### 打开方式

1. 在 GitHub 仓库页点击 `Code`
2. 切到 `Codespaces`
3. 选择 `Create codespace on main`

### 启动后会发生什么

- Codespaces 会自动运行 `dotnet restore`
- 会自动转发 `7335` 端口
- 连接成功后会自动执行：

```bash
dotnet watch run --project SportTracker.csproj --launch-profile SportTracker --no-restore
```

### 在手机上开发

- 用手机浏览器打开 GitHub 仓库
- 进入 Codespace 后用网页版 VS Code 编辑
- 打开转发出来的 `7335` 端口预览站点

### 数据文件

如果没有设置 `SPORT_DB_PATH`，Codespaces 里会默认在项目目录生成 `sport.db`。

## 数据

应用默认会在以下位置创建 `sport.db`，使用 SQLite 保存：

- 本地开发：项目根目录
- Azure App Service：`%HOME%/data/sporttracker/sport.db`
- 也可以通过环境变量 `SPORT_DB_PATH` 自定义路径

- 每日随机运动目标
- 每次提交的完成记录

## 页面

- `/`：今日运动
- `/records.html`：查看记录
