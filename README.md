# 运动记录

一个适合手机访问的小型家庭运动记录应用。主页为哥哥和弟弟分别生成每天 5 项随机运动目标，完成后可单项提交；记录页支持按日期、用户、项目筛选。

## 运行

需要安装 .NET 8 SDK。

```bash
dotnet restore
dotnet run
```

打开终端中显示的本地地址，例如 `http://localhost:5000` 或 `https://localhost:5001`。

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
