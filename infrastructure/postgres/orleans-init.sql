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

-- WriteToStorage function (Orleans 10 grain persistence)
CREATE OR REPLACE FUNCTION WriteToStorage(
    _GrainIdHash integer,
    _GrainIdN0 bigint,
    _GrainIdN1 bigint,
    _GrainTypeHash integer,
    _GrainTypeString character varying,
    _GrainIdExtensionString character varying,
    _ServiceId character varying,
    _GrainStateVersion integer,
    _PayloadBinary bytea)
    RETURNS TABLE(NewGrainStateVersion integer)
    LANGUAGE 'plpgsql'
AS $function$
    DECLARE
     _newGrainStateVersion integer := _GrainStateVersion;
     RowCountVar integer := 0;
    BEGIN
    IF _GrainStateVersion IS NOT NULL
    THEN
        UPDATE OrleansStorage
        SET
            PayloadBinary = _PayloadBinary,
            ModifiedOn = (now() at time zone 'utc'),
            Version = Version + 1
        WHERE
            GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL;
        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0
        THEN
            _newGrainStateVersion := _GrainStateVersion + 1;
        END IF;
    END IF;
    IF _GrainStateVersion IS NULL
    THEN
        INSERT INTO OrleansStorage
        (
            GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString,
            GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version
        )
        SELECT
            _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString,
            _GrainIdExtensionString, _ServiceId, _PayloadBinary, (now() at time zone 'utc'), 1
        WHERE NOT EXISTS (
            SELECT 1 FROM OrleansStorage
            WHERE
                GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
                AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
                AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
                AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
                AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
                AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        );
        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0
        THEN
            _newGrainStateVersion := 1;
        END IF;
    END IF;
    RETURN QUERY SELECT _newGrainStateVersion AS NewGrainStateVersion;
    END
$function$;

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
    WITH ins AS (
        INSERT INTO OrleansMembershipVersionTable(DeploymentId)
        SELECT @DeploymentId
        WHERE NOT EXISTS (SELECT 1 FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId)
        RETURNING 1
    )
    SELECT COUNT(*) > 0 FROM ins
'
),
(
    'InsertMembershipKey','
    WITH
    v_ins AS (
        INSERT INTO OrleansMembershipVersionTable(DeploymentId, Timestamp, Version)
        SELECT @DeploymentId, now() AT TIME ZONE ''utc'', 0
        WHERE NOT EXISTS (SELECT 1 FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId)
        RETURNING 1
    ),
    m_ins AS (
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
        )
        RETURNING 1
    ),
    v_upd AS (
        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now() AT TIME ZONE ''utc'', Version = Version + 1
        WHERE DeploymentId = @DeploymentId AND Version = @Version
        RETURNING 1
    )
    SELECT COUNT(*) > 0 FROM v_upd
'
),
(
    'UpdateMembershipKey','
    WITH
    m_upd AS (
        UPDATE OrleansMembershipTable
        SET Status = @Status, SuspectTimes = @SuspectTimes, IAmAliveTime = @IAmAliveTime
        WHERE DeploymentId = @DeploymentId AND Address = @Address AND Port = @Port AND Generation = @Generation
        RETURNING 1
    ),
    v_upd AS (
        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now() AT TIME ZONE ''utc'', Version = Version + 1
        WHERE DeploymentId = @DeploymentId AND Version = @Version
        RETURNING 1
    )
    SELECT COUNT(*) > 0 FROM v_upd
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
    'WriteToStorageKey','
        select * from WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);
'
),
(
    'ClearStorageKey','
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        Version = Version + 1
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
    Returning Version as NewGrainStateVersion
'
),
(
    'ReadFromStorageKey','
    SELECT
        PayloadBinary,
        (now() at time zone ''utc''),
        Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
'
),
(
    'DeleteStorageKey','
    DELETE FROM OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
    Returning Version + 1 as NewGrainStateVersion
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
),
(
    'GatewaysQueryKey','
    SELECT Address, ProxyPort, Generation
    FROM OrleansMembershipTable
    WHERE DeploymentId = @DeploymentId AND Status = 3 AND ProxyPort > 0
'
),
(
    'CleanupDefunctSiloEntriesKey','
    DELETE FROM OrleansMembershipTable
    WHERE DeploymentId = @DeploymentId
      AND IAmAliveTime < @IAmAliveTime
      AND Status != 3
'
)
ON CONFLICT (QueryKey) DO NOTHING;
