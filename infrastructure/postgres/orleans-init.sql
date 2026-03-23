-- Orleans 9 ADO.NET PostgreSQL schema
-- Source: https://github.com/dotnet/orleans/blob/main/src/AdoNet/Shared/SQL/PostgreSQL-Main.sql
-- Applied to nox_orleans database on first container startup

-- ============================================================
-- Membership / Clustering
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable
(
    DeploymentId        varchar(150) NOT NULL,
    Timestamp           timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    Version             bigint       NOT NULL DEFAULT 0,
    CONSTRAINT PK_MembershipVersionTable PRIMARY KEY(DeploymentId)
);

CREATE TABLE IF NOT EXISTS OrleansMembershipTable
(
    DeploymentId        varchar(150) NOT NULL,
    Address             varchar(45)  NOT NULL,
    Port                int          NOT NULL,
    Generation          int          NOT NULL,
    SiloName            varchar(150) NOT NULL,
    HostName            varchar(150) NOT NULL,
    Status              int          NOT NULL,
    ProxyPort           int          NULL,
    SuspectTimes        varchar(8000) NULL,
    StartTime           timestamp    NOT NULL,
    IAmAliveTime        timestamp    NOT NULL,
    CONSTRAINT PK_MembershipTable PRIMARY KEY(DeploymentId, Address, Port, Generation),
    CONSTRAINT FK_MembershipTable_MembershipVersionTable FOREIGN KEY(DeploymentId)
        REFERENCES OrleansMembershipVersionTable(DeploymentId)
);

-- ============================================================
-- Reminders
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansRemindersTable
(
    ServiceId           varchar(150) NOT NULL,
    GrainId             varchar(150) NOT NULL,
    ReminderName        varchar(150) NOT NULL,
    StartTime           timestamp    NOT NULL,
    Period              bigint       NOT NULL,
    GrainHash           int          NOT NULL,
    Version             int          NOT NULL,
    CONSTRAINT PK_RemindersTable PRIMARY KEY(ServiceId, GrainId, ReminderName)
);

CREATE INDEX IF NOT EXISTS IX_RemindersTable_GrainHash
    ON OrleansRemindersTable (ServiceId, GrainHash);

-- ============================================================
-- Grain Storage
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansStorage
(
    GrainIdHash         integer      NOT NULL,
    GrainIdN0           bigint       NOT NULL,
    GrainIdN1           bigint       NOT NULL,
    GrainTypeHash       integer      NOT NULL,
    GrainTypeString     varchar(512) NOT NULL,
    GrainIdExtensionString varchar(512),
    ServiceId           varchar(150) NOT NULL,
    PayloadBinary       bytea,
    ModifiedOn          timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    Version             integer
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Storage
    ON OrleansStorage (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId);

-- ============================================================
-- Queries (required by Orleans ADO.NET provider)
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansQuery
(
    QueryKey            varchar(64)   NOT NULL,
    QueryText           varchar(8000) NOT NULL,
    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
(
    'UpdateIAmAlivetimeKey','
    UPDATE OrleansMembershipTable
    SET IAmAliveTime = @IAmAliveTime
    WHERE DeploymentId = @DeploymentId
    AND Address = @Address
    AND Port = @Port
    AND Generation = @Generation
'
),
(
    'InsertMembershipVersionKey','
    INSERT INTO OrleansMembershipVersionTable(DeploymentId)
    SELECT @DeploymentId
    WHERE NOT EXISTS (SELECT 1 FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId)
'
),
(
    'InsertMembershipKey','
    BEGIN;

    INSERT INTO OrleansMembershipVersionTable(DeploymentId, Timestamp, Version)
    SELECT @DeploymentId, now() AT TIME ZONE ''utc'', 0
    WHERE NOT EXISTS (SELECT 1 FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId);

    INSERT INTO OrleansMembershipTable
    (
        DeploymentId,
        Address,
        Port,
        Generation,
        SiloName,
        HostName,
        Status,
        ProxyPort,
        StartTime,
        IAmAliveTime
    )
    SELECT @DeploymentId, @Address, @Port, @Generation, @SiloName, @HostName, @Status, @ProxyPort, @StartTime, @IAmAliveTime
    WHERE NOT EXISTS (
        SELECT 1 FROM OrleansMembershipTable
        WHERE DeploymentId = @DeploymentId AND Address = @Address AND Port = @Port AND Generation = @Generation
    );

    UPDATE OrleansMembershipVersionTable
    SET Timestamp = now() AT TIME ZONE ''utc'', Version = Version + 1
    WHERE DeploymentId = @DeploymentId AND Version = @Version;

    COMMIT;
'
),
(
    'UpdateMembershipKey','
    BEGIN;

    UPDATE OrleansMembershipTable
    SET Status = @Status, SuspectTimes = @SuspectTimes, IAmAliveTime = @IAmAliveTime
    WHERE DeploymentId = @DeploymentId AND Address = @Address AND Port = @Port AND Generation = @Generation;

    UPDATE OrleansMembershipVersionTable
    SET Timestamp = now() AT TIME ZONE ''utc'', Version = Version + 1
    WHERE DeploymentId = @DeploymentId AND Version = @Version;

    COMMIT;
'
),
(
    'UpsertReminderRowKey','
    INSERT INTO OrleansRemindersTable
    (
        ServiceId,
        GrainId,
        ReminderName,
        StartTime,
        Period,
        GrainHash,
        Version
    )
    VALUES(@ServiceId, @GrainId, @ReminderName, @StartTime, @Period, @GrainHash, 0)
    ON CONFLICT (ServiceId, GrainId, ReminderName)
    DO UPDATE SET
        StartTime    = excluded.StartTime,
        Period       = excluded.Period,
        GrainHash    = excluded.GrainHash,
        Version      = OrleansRemindersTable.Version + 1
    RETURNING Version
'
),
(
    'UpsertGrainStateKey','
    INSERT INTO OrleansStorage
    (
        GrainIdHash,
        GrainIdN0,
        GrainIdN1,
        GrainTypeHash,
        GrainTypeString,
        GrainIdExtensionString,
        ServiceId,
        PayloadBinary,
        ModifiedOn,
        Version
    )
    VALUES(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @PayloadBinary, now() AT TIME ZONE ''utc'', 1)
    ON CONFLICT (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId)
    DO UPDATE SET
        PayloadBinary = excluded.PayloadBinary,
        ModifiedOn    = excluded.ModifiedOn,
        Version       = OrleansStorage.Version + 1
    WHERE OrleansStorage.Version IS NULL OR @Version IS NULL OR OrleansStorage.Version = @Version
    RETURNING Version
'
),
(
    'ClearStorageKey','
    UPDATE OrleansStorage
    SET PayloadBinary = NULL,
        ModifiedOn    = now() AT TIME ZONE ''utc'',
        Version       = Version + 1
    WHERE GrainIdHash              = @GrainIdHash
    AND   GrainIdN0                = @GrainIdN0
    AND   GrainIdN1                = @GrainIdN1
    AND   GrainTypeHash            = @GrainTypeHash
    AND   GrainTypeString          = @GrainTypeString
    AND   GrainIdExtensionString IS NOT DISTINCT FROM @GrainIdExtensionString
    AND   ServiceId                = @ServiceId
    AND   (@Version IS NULL OR Version = @Version)
    RETURNING Version
'
),
(
    'ReadFromStorageKey','
    SELECT PayloadBinary, ModifiedOn, Version
    FROM OrleansStorage
    WHERE GrainIdHash              = @GrainIdHash
    AND   GrainIdN0                = @GrainIdN0
    AND   GrainIdN1                = @GrainIdN1
    AND   GrainTypeHash            = @GrainTypeHash
    AND   GrainTypeString          = @GrainTypeString
    AND   GrainIdExtensionString IS NOT DISTINCT FROM @GrainIdExtensionString
    AND   ServiceId                = @ServiceId
'
),
(
    'MembershipReadAllKey','
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
    WHERE v.DeploymentId = @DeploymentId
'
),
(
    'MembershipReadRowKey','
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT JOIN OrleansMembershipTable m
        ON v.DeploymentId = m.DeploymentId
        AND m.Address = @Address AND m.Port = @Port AND m.Generation = @Generation
    WHERE v.DeploymentId = @DeploymentId
'
),
(
    'DeleteMembershipTableEntriesKey','
    DELETE FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId;
    DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId;
'
),
(
    'ReadReminderRowsKey','
    SELECT GrainId, ReminderName, StartTime, Period, Version
    FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId AND GrainId = @GrainId
'
),
(
    'ReadReminderRowKey','
    SELECT GrainId, ReminderName, StartTime, Period, Version
    FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId AND GrainId = @GrainId AND ReminderName = @ReminderName
'
),
(
    'ReadRangeRows1Key','
    SELECT GrainId, ReminderName, StartTime, Period, Version
    FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId AND GrainHash > @BeginHash AND GrainHash <= @EndHash
'
),
(
    'ReadRangeRows2Key','
    SELECT GrainId, ReminderName, StartTime, Period, Version
    FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId AND (GrainHash > @BeginHash OR GrainHash <= @EndHash)
'
),
(
    'DeleteReminderRowKey','
    DELETE FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId AND GrainId = @GrainId AND ReminderName = @ReminderName AND Version = @Version
    RETURNING 1
'
),
(
    'DeleteReminderRowsKey','
    DELETE FROM OrleansRemindersTable
    WHERE ServiceId = @ServiceId
'
)
ON CONFLICT (QueryKey) DO NOTHING;
