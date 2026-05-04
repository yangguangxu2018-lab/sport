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
