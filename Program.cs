using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sporttracker-auth";
        options.LoginPath = "/login.html";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect("/");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddAuthorization();

var app = builder.Build();

var dbPath = ResolveDatabasePath(app.Environment);
var connectionString = $"Data Source={dbPath}";
Database.Initialize(connectionString);

app.UseAuthentication();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
    var isProtectedPage = path is "/" or "/index.html" or "/records.html";

    if (!isAuthenticated && isProtectedPage)
    {
        context.Response.Redirect("/login.html");
        return;
    }

    if (isAuthenticated && path.Equals("/login.html", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/");
        return;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (HttpContext context, LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "请输入用户名和密码。" });
    }

    var account = AppData.Authenticate(request.Username, request.Password);
    if (account is null)
    {
        return Results.BadRequest(new { message = "用户名或密码不正确。" });
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, account.Username),
        new(ClaimTypes.Name, account.DisplayName),
        new(ClaimTypes.Role, account.Role)
    };

    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(AppData.ToSession(account));
});

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "已退出登录。" });
});

app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
{
    var account = AppData.GetAccount(principal);
    return account is null ? Results.Unauthorized() : Results.Ok(AppData.ToSession(account));
});

app.MapGet("/api/tasks/today", (ClaimsPrincipal principal) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsUser)
    {
        return Results.Forbid();
    }

    var today = DateOnly.FromDateTime(DateTime.Now);
    var tasks = Database.GetOrCreateDailyTasks(connectionString, today, account.DisplayName);

    return Results.Ok(new TodayTasksResponse(
        today.ToString("yyyy-MM-dd"),
        account.DisplayName,
        tasks));
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.User });

app.MapGet("/api/home-scoreboard", (ClaimsPrincipal principal) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsUser)
    {
        return Results.Forbid();
    }

    var today = DateOnly.FromDateTime(DateTime.Now);
    var summaries = AppData.Users
        .Select(userName => new HomeScoreSummaryDto(
            userName,
            Database.GetWeeklySummary(connectionString, today, userName, null)))
        .ToArray();

    return Results.Ok(new HomeScoreboardResponse(
        today.ToString("yyyy-MM-dd"),
        summaries));
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.User });

app.MapGet("/api/admin/sports", (ClaimsPrincipal principal) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsAdmin)
    {
        return Results.Forbid();
    }

    return Results.Ok(Database.GetSports(connectionString));
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Admin });

app.MapPost("/api/admin/sports", (ClaimsPrincipal principal, SportDefinitionEditorRequest request) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsAdmin)
    {
        return Results.Forbid();
    }

    var validationMessage = ValidateSportDefinition(request);
    if (validationMessage is not null)
    {
        return Results.BadRequest(new { message = validationMessage });
    }

    var normalizedName = request.Name.Trim();
    if (Database.SportNameExists(connectionString, normalizedName))
    {
        return Results.BadRequest(new { message = "已经有同名的运动项目了。" });
    }

    var sport = Database.CreateSport(connectionString, request with { Name = normalizedName });
    return Results.Created($"/api/admin/sports/{sport.Id}", sport);
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Admin });

app.MapPut("/api/admin/sports/{sportId:long}", (ClaimsPrincipal principal, long sportId, SportDefinitionEditorRequest request) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsAdmin)
    {
        return Results.Forbid();
    }

    var validationMessage = ValidateSportDefinition(request);
    if (validationMessage is not null)
    {
        return Results.BadRequest(new { message = validationMessage });
    }

    var normalizedName = request.Name.Trim();
    if (Database.SportNameExists(connectionString, normalizedName, sportId))
    {
        return Results.BadRequest(new { message = "已经有同名的运动项目了。" });
    }

    var sport = Database.UpdateSport(connectionString, sportId, request with { Name = normalizedName });
    return sport is null
        ? Results.NotFound(new { message = "没有找到要修改的运动项目。" })
        : Results.Ok(sport);
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Admin });

app.MapPost("/api/records", (ClaimsPrincipal principal, SubmitRecordRequest request) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsUser)
    {
        return Results.Forbid();
    }

    if (request.ActualValue <= 0)
    {
        return Results.BadRequest(new { message = "实际完成数值必须大于 0。" });
    }

    var task = Database.GetDailyTask(connectionString, request.TaskId);
    if (task is null || task.UserName != account.DisplayName)
    {
        return Results.NotFound(new { message = "没有找到今天对应的运动项目。" });
    }

    var existingRecord = Database.GetRecordByTaskId(connectionString, request.TaskId);
    if (existingRecord is not null)
    {
        return Results.Conflict(new { message = "这个项目今天已经记录过了。" });
    }

    var record = Database.CreateRecord(connectionString, task, request.ActualValue);
    return Results.Created($"/api/records/{record.Id}", record);
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.User });

app.MapGet("/api/records", (
    ClaimsPrincipal principal,
    DateOnly? date,
    [FromQuery(Name = "user")] string? userFilter,
    string? sport) =>
{
    var account = AppData.GetRequiredAccount(principal);

    if (!string.IsNullOrWhiteSpace(userFilter) && !AppData.Users.Contains(userFilter))
    {
        return Results.BadRequest(new { message = "用户筛选条件不正确。" });
    }

    if (!string.IsNullOrWhiteSpace(sport) && !Database.IsKnownSportName(connectionString, sport))
    {
        return Results.BadRequest(new { message = "项目筛选条件不正确。" });
    }

    var scopedUser = account.IsAdmin ? userFilter : account.DisplayName;
    var today = DateOnly.FromDateTime(DateTime.Now);

    var records = Database.SearchRecords(connectionString, date, scopedUser, sport);
    var weeklySummary = Database.GetWeeklySummary(connectionString, today, scopedUser, sport);

    return Results.Ok(new RecordSearchResponse(
        records,
        weeklySummary,
        account.IsAdmin,
        AppData.ToSession(account)));
}).RequireAuthorization();

app.MapPost("/api/records/{recordId:long}/score", (ClaimsPrincipal principal, long recordId, ScoreRecordRequest request) =>
{
    var account = AppData.GetRequiredAccount(principal);
    if (!account.IsAdmin)
    {
        return Results.Forbid();
    }

    if (request.Score is < 0 or > 10)
    {
        return Results.BadRequest(new { message = "评分必须在 0 到 10 分之间。" });
    }

    var record = Database.UpdateRecordScore(connectionString, recordId, request.Score, account.Username);
    return record is null
        ? Results.NotFound(new { message = "没有找到要评分的记录。" })
        : Results.Ok(record);
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Admin });

app.MapGet("/api/sports", () => Results.Ok(Database.GetSports(connectionString))).RequireAuthorization();

app.Run();

static string ResolveDatabasePath(IWebHostEnvironment environment)
{
    var configuredPath = Environment.GetEnvironmentVariable("SPORT_DB_PATH");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        var fullConfiguredPath = Path.GetFullPath(configuredPath);
        EnsureDirectoryExists(Path.GetDirectoryName(fullConfiguredPath));
        return fullConfiguredPath;
    }

    var homePath = Environment.GetEnvironmentVariable("HOME");
    if (!string.IsNullOrWhiteSpace(homePath))
    {
        var dataDirectory = Path.Combine(homePath, "data", "sporttracker");
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, "sport.db");
    }

    return Path.Combine(environment.ContentRootPath, "sport.db");
}

static void EnsureDirectoryExists(string? directoryPath)
{
    if (!string.IsNullOrWhiteSpace(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }
}

static string? ValidateSportDefinition(SportDefinitionEditorRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return "请输入运动项目名称。";
    }

    if (request.MinTarget <= 0)
    {
        return "最小随机值必须大于 0。";
    }

    if (request.MaxTarget < request.MinTarget)
    {
        return "最大随机值不能小于最小随机值。";
    }

    return null;
}

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class AppData
{
    public static readonly AppAccount[] Accounts =
    {
        new("yihong", "yihong", "哥哥", AppRoles.User),
        new("yichen", "yichen", "弟弟", AppRoles.User),
        new("ygx", "00134", "ygx", AppRoles.Admin),
        new("cby", "00134", "cby", AppRoles.Admin)
    };

    public static readonly string[] Users = Accounts
        .Where(account => account.IsUser)
        .Select(account => account.DisplayName)
        .ToArray();

    public static readonly SportDefinition[] SeedSports =
    {
        new("吊单杠", TargetKind.TimeSeconds, 40, 100),
        new("接力棒", TargetKind.Count, 40, 100),
        new("拉力器", TargetKind.Count, 40, 100),
        new("跳高", TargetKind.Count, 40, 100),
        new("握力器", TargetKind.Count, 40, 100),
        new("橡皮拉伸", TargetKind.Count, 40, 100),
        new("杠铃", TargetKind.Count, 40, 100),
        new("哑铃", TargetKind.Count, 40, 100),
        new("自行车", TargetKind.TimeMinutes, 10, 30),
        new("跑步机", TargetKind.TimeMinutes, 10, 30),
        new("椭圆仪", TargetKind.TimeMinutes, 10, 30)
    };

    public static AppAccount? Authenticate(string username, string password) =>
        Accounts.FirstOrDefault(account =>
            account.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            account.Password == password);

    public static AppAccount? GetAccount(ClaimsPrincipal principal)
    {
        var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(username)
            ? null
            : Accounts.FirstOrDefault(account => account.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public static AppAccount GetRequiredAccount(ClaimsPrincipal principal) =>
        GetAccount(principal) ?? throw new InvalidOperationException("当前登录信息无效。");

    public static UserSessionDto ToSession(AppAccount account) =>
        new(account.Username, account.DisplayName, account.Role, account.IsAdmin);
}

public record AppAccount(string Username, string Password, string DisplayName, string Role)
{
    public bool IsAdmin => Role == AppRoles.Admin;
    public bool IsUser => Role == AppRoles.User;
}

public enum TargetKind
{
    Count,
    TimeMinutes,
    TimeSeconds
}

public record SportDefinition(long Id, string Name, TargetKind Kind, int MinTarget, int MaxTarget)
{
    public SportDefinition(string name, TargetKind kind, int minTarget, int maxTarget)
        : this(0, name, kind, minTarget, maxTarget)
    {
    }

    public string Unit => Kind switch
    {
        TargetKind.TimeMinutes => "分钟",
        TargetKind.TimeSeconds => "秒",
        _ => "次"
    };
}

public record UserSessionDto(string Username, string DisplayName, string Role, bool IsAdmin);

public record LoginRequest(string Username, string Password);

public record SubmitRecordRequest(long TaskId, int ActualValue);

public record ScoreRecordRequest(int? Score);

public record SportDefinitionEditorRequest(string Name, TargetKind Kind, int MinTarget, int MaxTarget);

public record TodayTasksResponse(string Date, string User, IReadOnlyList<DailyTaskDto> Tasks);

public record HomeScoreboardResponse(string Date, IReadOnlyList<HomeScoreSummaryDto> Summaries);

public record HomeScoreSummaryDto(string UserName, WeeklySummaryDto WeeklySummary);

public record RecordSearchResponse(
    IReadOnlyList<SportRecordDto> Records,
    WeeklySummaryDto WeeklySummary,
    bool CanScore,
    UserSessionDto CurrentUser);

public record WeeklySummaryDto(
    string StartDate,
    string EndDate,
    int DaysElapsed,
    int TotalScore,
    double AverageScore);

public record DailyTaskDto(
    long Id,
    string Date,
    string UserName,
    string SportName,
    TargetKind TargetKind,
    string Unit,
    int TargetValue,
    long? RecordId,
    int? ActualValue,
    string? SubmittedAt,
    int? Score);

public record SportRecordDto(
    long Id,
    long? DailyTaskId,
    string TaskDate,
    string UserName,
    string SportName,
    TargetKind TargetKind,
    string Unit,
    int TargetValue,
    int ActualValue,
    string SubmittedAt,
    int? Score,
    string? ScoredBy,
    string? ScoredAt);

public static class Database
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS DailyTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                UserName TEXT NOT NULL,
                SportName TEXT NOT NULL,
                TargetKind TEXT NOT NULL,
                Unit TEXT NOT NULL,
                TargetValue INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UNIQUE(Date, UserName, SportName)
            );

            CREATE TABLE IF NOT EXISTS SportRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL,
                SportName TEXT NOT NULL,
                TargetKind TEXT NOT NULL,
                Unit TEXT NOT NULL,
                TargetValue INTEGER NOT NULL,
                ActualValue INTEGER NOT NULL,
                SubmittedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Sports (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                TargetKind TEXT NOT NULL,
                MinTarget INTEGER NOT NULL,
                MaxTarget INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "SportRecords", "DailyTaskId", "INTEGER");
        EnsureColumn(connection, "SportRecords", "TaskDate", "TEXT");
        EnsureColumn(connection, "SportRecords", "Score", "INTEGER");
        EnsureColumn(connection, "SportRecords", "ScoredBy", "TEXT");
        EnsureColumn(connection, "SportRecords", "ScoredAt", "TEXT");

        using var migration = connection.CreateCommand();
        migration.CommandText =
            """
            UPDATE SportRecords
            SET TaskDate = substr(SubmittedAt, 1, 10)
            WHERE TaskDate IS NULL OR TaskDate = '';

            CREATE UNIQUE INDEX IF NOT EXISTS IX_SportRecords_DailyTaskId
            ON SportRecords(DailyTaskId)
            WHERE DailyTaskId IS NOT NULL;
            """;
        migration.ExecuteNonQuery();

        SeedSports(connection);
    }

    public static IReadOnlyList<DailyTaskDto> GetOrCreateDailyTasks(string connectionString, DateOnly date, string user)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var existing = GetDailyTasks(connection, date, user);
        if (existing.Count > 0)
        {
            return existing;
        }

        var selectedSports = GetSports(connection)
            .OrderBy(_ => Random.Shared.Next())
            .Take(5)
            .ToArray();

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var sport in selectedSports)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO DailyTasks (Date, UserName, SportName, TargetKind, Unit, TargetValue, CreatedAt)
                    VALUES ($date, $userName, $sportName, $targetKind, $unit, $targetValue, $createdAt);
                    """;
                insert.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
                insert.Parameters.AddWithValue("$userName", user);
                insert.Parameters.AddWithValue("$sportName", sport.Name);
                insert.Parameters.AddWithValue("$targetKind", sport.Kind.ToString());
                insert.Parameters.AddWithValue("$unit", sport.Unit);
                insert.Parameters.AddWithValue("$targetValue", Random.Shared.Next(sport.MinTarget, sport.MaxTarget + 1));
                insert.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException)
        {
            transaction.Rollback();
        }

        return GetDailyTasks(connection, date, user);
    }

    public static DailyTaskDto? GetDailyTask(string connectionString, long taskId)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Date, UserName, SportName, TargetKind, Unit, TargetValue
            FROM DailyTasks
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", taskId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTaskDefinition(reader) : null;
    }

    public static SportRecordDto? GetRecordByTaskId(string connectionString, long taskId)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DailyTaskId, COALESCE(TaskDate, substr(SubmittedAt, 1, 10)), UserName, SportName,
                   TargetKind, Unit, TargetValue, ActualValue, SubmittedAt, Score, ScoredBy, ScoredAt
            FROM SportRecords
            WHERE DailyTaskId = $dailyTaskId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$dailyTaskId", taskId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    public static SportRecordDto CreateRecord(string connectionString, DailyTaskDto task, int actualValue)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var submittedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO SportRecords (
                DailyTaskId,
                TaskDate,
                UserName,
                SportName,
                TargetKind,
                Unit,
                TargetValue,
                ActualValue,
                SubmittedAt
            )
            VALUES (
                $dailyTaskId,
                $taskDate,
                $userName,
                $sportName,
                $targetKind,
                $unit,
                $targetValue,
                $actualValue,
                $submittedAt
            );
            """;
        insert.Parameters.AddWithValue("$dailyTaskId", task.Id);
        insert.Parameters.AddWithValue("$taskDate", task.Date);
        insert.Parameters.AddWithValue("$userName", task.UserName);
        insert.Parameters.AddWithValue("$sportName", task.SportName);
        insert.Parameters.AddWithValue("$targetKind", task.TargetKind.ToString());
        insert.Parameters.AddWithValue("$unit", task.Unit);
        insert.Parameters.AddWithValue("$targetValue", task.TargetValue);
        insert.Parameters.AddWithValue("$actualValue", actualValue);
        insert.Parameters.AddWithValue("$submittedAt", submittedAt);
        insert.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT last_insert_rowid();";
        var id = (long)(idCommand.ExecuteScalar() ?? 0L);

        return new SportRecordDto(
            id,
            task.Id,
            task.Date,
            task.UserName,
            task.SportName,
            task.TargetKind,
            task.Unit,
            task.TargetValue,
            actualValue,
            submittedAt,
            null,
            null,
            null);
    }

    public static SportRecordDto? UpdateRecordScore(string connectionString, long recordId, int? score, string adminUserName)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var scoredAt = score is null ? null : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SportRecords
            SET Score = $score,
                ScoredBy = $scoredBy,
                ScoredAt = $scoredAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$score", score is null ? (object)DBNull.Value : score.Value);
        command.Parameters.AddWithValue("$scoredBy", score is null ? (object)DBNull.Value : adminUserName);
        command.Parameters.AddWithValue("$scoredAt", score is null ? (object)DBNull.Value : scoredAt!);
        command.Parameters.AddWithValue("$id", recordId);

        var rows = command.ExecuteNonQuery();
        return rows == 0 ? null : GetRecordById(connection, recordId);
    }

    public static IReadOnlyList<SportDefinition> GetSports(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        return GetSports(connection);
    }

    public static SportDefinition CreateSport(string connectionString, SportDefinitionEditorRequest request)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Sports (Name, TargetKind, MinTarget, MaxTarget, CreatedAt, UpdatedAt)
            VALUES ($name, $targetKind, $minTarget, $maxTarget, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$name", request.Name);
        command.Parameters.AddWithValue("$targetKind", request.Kind.ToString());
        command.Parameters.AddWithValue("$minTarget", request.MinTarget);
        command.Parameters.AddWithValue("$maxTarget", request.MaxTarget);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT last_insert_rowid();";
        var id = (long)(idCommand.ExecuteScalar() ?? 0L);
        return GetSportById(connection, id)!;
    }

    public static SportDefinition? UpdateSport(string connectionString, long sportId, SportDefinitionEditorRequest request)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var currentSport = GetSportById(connection, sportId);
        if (currentSport is null)
        {
            return null;
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var transaction = connection.BeginTransaction();

        using (var updateSport = connection.CreateCommand())
        {
            updateSport.Transaction = transaction;
            updateSport.CommandText =
                """
                UPDATE Sports
                SET Name = $name,
                    TargetKind = $targetKind,
                    MinTarget = $minTarget,
                    MaxTarget = $maxTarget,
                    UpdatedAt = $updatedAt
                WHERE Id = $id;
                """;
            updateSport.Parameters.AddWithValue("$name", request.Name);
            updateSport.Parameters.AddWithValue("$targetKind", request.Kind.ToString());
            updateSport.Parameters.AddWithValue("$minTarget", request.MinTarget);
            updateSport.Parameters.AddWithValue("$maxTarget", request.MaxTarget);
            updateSport.Parameters.AddWithValue("$updatedAt", now);
            updateSport.Parameters.AddWithValue("$id", sportId);
            updateSport.ExecuteNonQuery();
        }

        if (!string.Equals(currentSport.Name, request.Name, StringComparison.Ordinal))
        {
            using var renameRows = connection.CreateCommand();
            renameRows.Transaction = transaction;
            renameRows.CommandText =
                """
                UPDATE DailyTasks
                SET SportName = $newName
                WHERE SportName = $oldName;

                UPDATE SportRecords
                SET SportName = $newName
                WHERE SportName = $oldName;
                """;
            renameRows.Parameters.AddWithValue("$newName", request.Name);
            renameRows.Parameters.AddWithValue("$oldName", currentSport.Name);
            renameRows.ExecuteNonQuery();
        }

        transaction.Commit();
        return GetSportById(connection, sportId);
    }

    public static bool SportNameExists(string connectionString, string name, long? excludingId = null)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM Sports
                WHERE Name = $name
                  AND ($excludingId IS NULL OR Id <> $excludingId)
            );
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$excludingId", excludingId is null ? (object)DBNull.Value : excludingId.Value);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    public static bool IsKnownSportName(string connectionString, string name)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CASE
                WHEN EXISTS(SELECT 1 FROM Sports WHERE Name = $name) THEN 1
                WHEN EXISTS(SELECT 1 FROM DailyTasks WHERE SportName = $name) THEN 1
                WHEN EXISTS(SELECT 1 FROM SportRecords WHERE SportName = $name) THEN 1
                ELSE 0
            END;
            """;
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    public static IReadOnlyList<SportRecordDto> SearchRecords(string connectionString, DateOnly? date, string? user, string? sport)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DailyTaskId, COALESCE(TaskDate, substr(SubmittedAt, 1, 10)), UserName, SportName,
                   TargetKind, Unit, TargetValue, ActualValue, SubmittedAt, Score, ScoredBy, ScoredAt
            FROM SportRecords
            WHERE ($date IS NULL OR COALESCE(TaskDate, substr(SubmittedAt, 1, 10)) = $date)
              AND ($user IS NULL OR UserName = $user)
              AND ($sport IS NULL OR SportName = $sport)
            ORDER BY COALESCE(TaskDate, substr(SubmittedAt, 1, 10)) DESC, SubmittedAt DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$date", date is null ? (object)DBNull.Value : date.Value.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? (object)DBNull.Value : user);
        command.Parameters.AddWithValue("$sport", string.IsNullOrWhiteSpace(sport) ? (object)DBNull.Value : sport);

        var records = new List<SportRecordDto>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public static WeeklySummaryDto GetWeeklySummary(string connectionString, DateOnly today, string? user, string? sport)
    {
        var startOfWeek = GetStartOfWeek(today);
        var daysElapsed = today.DayNumber - startOfWeek.DayNumber + 1;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(COALESCE(Score, 0)), 0)
            FROM SportRecords
            WHERE COALESCE(TaskDate, substr(SubmittedAt, 1, 10)) >= $startDate
              AND COALESCE(TaskDate, substr(SubmittedAt, 1, 10)) <= $endDate
              AND ($user IS NULL OR UserName = $user)
              AND ($sport IS NULL OR SportName = $sport);
            """;
        command.Parameters.AddWithValue("$startDate", startOfWeek.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$endDate", today.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? (object)DBNull.Value : user);
        command.Parameters.AddWithValue("$sport", string.IsNullOrWhiteSpace(sport) ? (object)DBNull.Value : sport);

        var totalScore = Convert.ToInt32(command.ExecuteScalar() ?? 0);
        var averageScore = Math.Round(totalScore / (double)daysElapsed, 2);

        return new WeeklySummaryDto(
            startOfWeek.ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM-dd"),
            daysElapsed,
            totalScore,
            averageScore);
    }

    private static IReadOnlyList<DailyTaskDto> GetDailyTasks(SqliteConnection connection, DateOnly date, string user)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.Id, t.Date, t.UserName, t.SportName, t.TargetKind, t.Unit, t.TargetValue,
                   r.Id, r.ActualValue, r.SubmittedAt, r.Score
            FROM DailyTasks t
            LEFT JOIN SportRecords r ON r.DailyTaskId = t.Id
            WHERE t.Date = $date AND t.UserName = $userName
            ORDER BY t.Id;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$userName", user);

        var tasks = new List<DailyTaskDto>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tasks.Add(ReadDailyTask(reader));
        }

        return tasks;
    }

    private static SportRecordDto? GetRecordById(SqliteConnection connection, long recordId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DailyTaskId, COALESCE(TaskDate, substr(SubmittedAt, 1, 10)), UserName, SportName,
                   TargetKind, Unit, TargetValue, ActualValue, SubmittedAt, Score, ScoredBy, ScoredAt
            FROM SportRecords
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", recordId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private static SportDefinition? GetSportById(SqliteConnection connection, long sportId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, TargetKind, MinTarget, MaxTarget
            FROM Sports
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", sportId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSport(reader) : null;
    }

    private static IReadOnlyList<SportDefinition> GetSports(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, TargetKind, MinTarget, MaxTarget
            FROM Sports
            ORDER BY Id;
            """;

        var sports = new List<SportDefinition>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sports.Add(ReadSport(reader));
        }

        return sports;
    }

    private static DailyTaskDto ReadTaskDefinition(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<TargetKind>(reader.GetString(4)),
            reader.GetString(5),
            reader.GetInt32(6),
            null,
            null,
            null,
            null);

    private static DailyTaskDto ReadDailyTask(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<TargetKind>(reader.GetString(4)),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10));

    private static SportRecordDto ReadRecord(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            Enum.Parse<TargetKind>(reader.GetString(5)),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));

    private static SportDefinition ReadSport(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            Enum.Parse<TargetKind>(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetInt32(4));

    private static void SeedSports(SqliteConnection connection)
    {
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM Sports;";
        var existingCount = Convert.ToInt32(countCommand.ExecuteScalar() ?? 0);
        if (existingCount > 0)
        {
            return;
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var transaction = connection.BeginTransaction();

        foreach (var sport in AppData.SeedSports)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO Sports (Name, TargetKind, MinTarget, MaxTarget, CreatedAt, UpdatedAt)
                VALUES ($name, $targetKind, $minTarget, $maxTarget, $createdAt, $updatedAt);
                """;
            insert.Parameters.AddWithValue("$name", sport.Name);
            insert.Parameters.AddWithValue("$targetKind", sport.Kind.ToString());
            insert.Parameters.AddWithValue("$minTarget", sport.MinTarget);
            insert.Parameters.AddWithValue("$maxTarget", sport.MaxTarget);
            insert.Parameters.AddWithValue("$createdAt", now);
            insert.Parameters.AddWithValue("$updatedAt", now);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        var exists = false;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
