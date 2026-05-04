#!/bin/bash
# SportTracker - IIS 发布脚本（macOS/Linux 本地发布）
# 此脚本在当前机器发布应用，生成的文件需要手动复制到 Windows IIS 服务器

set -e

echo "================ SportTracker 发布脚本 ================"
echo ""
echo "1. 清理旧的发布文件..."
if [ -d "publish" ]; then
    rm -rf publish
    echo "   ✓ 已清理 publish 文件夹"
fi

echo ""
echo "2. 发布应用（Release 配置）..."
echo "   构建配置: Release"
echo "   输出目录: ./publish"
dotnet publish -c Release -o publish

if [ $? -ne 0 ]; then
    echo "   ✗ 发布失败！"
    exit 1
fi
echo "   ✓ 发布完成"

echo ""
echo "3. 生成部署清单..."
cat > publish/DEPLOYMENT_INFO.txt << EOF
SportTracker - IIS 部署信息
生成时间: $(date)
.NET 版本: $(dotnet --version)

部署步骤：
1. 将 publish 文件夹中的所有文件上传到 Windows 服务器
   目标路径示例: C:\\inetpub\\wwwroot\\SportTracker

2. 在 Windows 服务器上运行 publish.bat 脚本来完成自动化部署
   或参考 IIS_DEPLOYMENT_GUIDE.md 手动配置

3. 数据库文件 (sport.db) 会在首次运行时自动创建

需要的服务器要求：
- Windows Server 2016 或更高版本
- IIS 10.0 或更高版本
- .NET 10.0 Runtime 或 Hosting Bundle
- ASP.NET Core Runtime 10.0

更详细的说明请参考项目中的 IIS_DEPLOYMENT_GUIDE.md
EOF

echo "   ✓ 生成部署信息"

echo ""
echo "4. 统计发布文件..."
FILE_COUNT=$(find publish -type f | wc -l)
TOTAL_SIZE=$(du -sh publish | cut -f1)
echo "   文件总数: $FILE_COUNT"
echo "   文件大小: $TOTAL_SIZE"

echo ""
echo "================ 发布完成 ================"
echo ""
echo "下一步："
echo "1. 将 publish 文件夹中的所有文件上传到 Windows IIS 服务器"
echo "2. 在 Windows 上运行 publish.bat 脚本或按照 IIS_DEPLOYMENT_GUIDE.md 进行手动配置"
echo ""
