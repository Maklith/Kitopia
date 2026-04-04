using Core.Utils;
using Microsoft.Data.Sqlite;

namespace Core.Services.DeviceCommunication;

public sealed class SqliteDeviceChatHistoryStore : IDeviceChatHistoryStore
{
    private const int DefaultQueryLimit = 200;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public event EventHandler<DeviceChatMessage>? MessageStored;

    public SqliteDeviceChatHistoryStore()
    {
        var dbDirectory = Path.Combine(KitopiaPaths.AppRoot, "databases");
        Directory.CreateDirectory(dbDirectory);
        var dbPath = Path.Combine(dbDirectory, "device_chat_history.db");

        var connectionBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = connectionBuilder.ToString();
        InitializeDatabase();
    }

    public async Task AppendAsync(DeviceChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sanitized = Sanitize(message);
        DeviceChatMessage? storedMessage = null;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO DeviceChatMessages
                (
                    PeerKey,
                    PeerId,
                    PeerName,
                    PeerAddress,
                    PeerPort,
                    Direction,
                    EntryType,
                    Content,
                    FileName,
                    FilePath,
                    FileSize,
                    RequestId,
                    Status,
                    TimestampUtcTicks
                )
                VALUES
                (
                    $peerKey,
                    $peerId,
                    $peerName,
                    $peerAddress,
                    $peerPort,
                    $direction,
                    $entryType,
                    $content,
                    $fileName,
                    $filePath,
                    $fileSize,
                    $requestId,
                    $status,
                    $timestampUtcTicks
                );
                """;

            command.Parameters.AddWithValue("$peerKey", sanitized.PeerKey);
            command.Parameters.AddWithValue("$peerId", sanitized.PeerId);
            command.Parameters.AddWithValue("$peerName", sanitized.PeerName);
            command.Parameters.AddWithValue("$peerAddress", sanitized.PeerAddress);
            command.Parameters.AddWithValue("$peerPort", sanitized.PeerPort);
            command.Parameters.AddWithValue("$direction", (int)sanitized.Direction);
            command.Parameters.AddWithValue("$entryType", (int)sanitized.EntryType);
            command.Parameters.AddWithValue("$content", sanitized.Content);
            command.Parameters.AddWithValue("$fileName", sanitized.FileName);
            command.Parameters.AddWithValue("$filePath", sanitized.FilePath);
            command.Parameters.AddWithValue("$fileSize", sanitized.FileSize);
            command.Parameters.AddWithValue("$requestId", sanitized.RequestId);
            command.Parameters.AddWithValue("$status", sanitized.Status);
            command.Parameters.AddWithValue("$timestampUtcTicks", sanitized.TimestampUtc.Ticks);

            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid();";
            var idObject = await idCommand.ExecuteScalarAsync(cancellationToken);
            var insertedId = idObject switch
            {
                long value => value,
                int value => value,
                _ => Convert.ToInt64(idObject)
            };

            storedMessage = CopyWithId(sanitized, insertedId);
        }
        finally
        {
            _dbLock.Release();
        }

        if (storedMessage is not null)
        {
            MessageStored?.Invoke(this, storedMessage);
        }
    }

    public async Task<IReadOnlyList<DeviceChatConversation>> GetConversationsAsync(int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = limit > 0 ? limit : DefaultQueryLimit;
        var conversations = new List<DeviceChatConversation>();

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    m.Id,
                    m.PeerKey,
                    m.PeerId,
                    m.PeerName,
                    m.PeerAddress,
                    m.PeerPort,
                    m.Direction,
                    m.EntryType,
                    m.Content,
                    m.FileName,
                    m.Status,
                    m.TimestampUtcTicks
                FROM DeviceChatMessages AS m
                INNER JOIN
                (
                    SELECT PeerKey, MAX(Id) AS LastId
                    FROM DeviceChatMessages
                    GROUP BY PeerKey
                ) AS latest
                ON latest.LastId = m.Id
                ORDER BY m.Id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", effectiveLimit);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                conversations.Add(ReadConversation(reader));
            }
        }
        finally
        {
            _dbLock.Release();
        }

        return conversations;
    }

    public async Task<IReadOnlyList<DeviceChatMessage>> GetMessagesAsync(string peerKey, int limit = 300,
        long? beforeMessageId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(peerKey))
        {
            return [];
        }

        var effectiveLimit = limit > 0 ? limit : DefaultQueryLimit;
        var messages = new List<DeviceChatMessage>();

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    Id,
                    PeerKey,
                    PeerId,
                    PeerName,
                    PeerAddress,
                    PeerPort,
                    Direction,
                    EntryType,
                    Content,
                    FileName,
                    FilePath,
                    FileSize,
                    RequestId,
                    Status,
                    TimestampUtcTicks
                FROM DeviceChatMessages
                WHERE PeerKey = $peerKey
                  AND ($beforeMessageId IS NULL OR Id < $beforeMessageId)
                ORDER BY Id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$peerKey", peerKey);
            command.Parameters.AddWithValue("$limit", effectiveLimit);
            command.Parameters.AddWithValue("$beforeMessageId",
                beforeMessageId.HasValue ? beforeMessageId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(ReadMessage(reader));
            }
        }
        finally
        {
            _dbLock.Release();
        }

        messages.Reverse();
        return messages;
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS DeviceChatMessages
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PeerKey TEXT NOT NULL,
                PeerId TEXT NOT NULL,
                PeerName TEXT NOT NULL,
                PeerAddress TEXT NOT NULL,
                PeerPort INTEGER NOT NULL,
                Direction INTEGER NOT NULL,
                EntryType INTEGER NOT NULL,
                Content TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                RequestId TEXT NOT NULL,
                Status TEXT NOT NULL,
                TimestampUtcTicks INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DeviceChatMessages_PeerKey_Id
                ON DeviceChatMessages (PeerKey, Id DESC);
            CREATE INDEX IF NOT EXISTS IX_DeviceChatMessages_Id
                ON DeviceChatMessages (Id DESC);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    private static DeviceChatMessage ReadMessage(SqliteDataReader reader)
    {
        return new DeviceChatMessage
        {
            Id = reader.GetInt64(0),
            PeerKey = reader.GetString(1),
            PeerId = reader.GetString(2),
            PeerName = reader.GetString(3),
            PeerAddress = reader.GetString(4),
            PeerPort = reader.GetInt32(5),
            Direction = (DeviceChatDirection)reader.GetInt32(6),
            EntryType = (DeviceChatEntryType)reader.GetInt32(7),
            Content = reader.GetString(8),
            FileName = reader.GetString(9),
            FilePath = reader.GetString(10),
            FileSize = reader.GetInt64(11),
            RequestId = reader.GetString(12),
            Status = reader.GetString(13),
            TimestampUtc = ReadUtcTicks(reader.GetInt64(14))
        };
    }

    private static DeviceChatConversation ReadConversation(SqliteDataReader reader)
    {
        return new DeviceChatConversation
        {
            LastMessageId = reader.GetInt64(0),
            PeerKey = reader.GetString(1),
            PeerId = reader.GetString(2),
            PeerName = reader.GetString(3),
            PeerAddress = reader.GetString(4),
            PeerPort = reader.GetInt32(5),
            LastDirection = (DeviceChatDirection)reader.GetInt32(6),
            LastEntryType = (DeviceChatEntryType)reader.GetInt32(7),
            LastContent = reader.GetString(8),
            LastFileName = reader.GetString(9),
            LastStatus = reader.GetString(10),
            LastTimestampUtc = ReadUtcTicks(reader.GetInt64(11))
        };
    }

    private static DeviceChatMessage Sanitize(DeviceChatMessage message)
    {
        var timestampUtc = message.TimestampUtc == default ? DateTime.UtcNow : message.TimestampUtc;
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = timestampUtc.ToUniversalTime();
        }

        return new DeviceChatMessage
        {
            Id = message.Id,
            PeerKey = message.PeerKey ?? string.Empty,
            PeerId = message.PeerId ?? string.Empty,
            PeerName = message.PeerName ?? string.Empty,
            PeerAddress = message.PeerAddress ?? string.Empty,
            PeerPort = message.PeerPort,
            Direction = message.Direction,
            EntryType = message.EntryType,
            Content = message.Content ?? string.Empty,
            FileName = message.FileName ?? string.Empty,
            FilePath = message.FilePath ?? string.Empty,
            FileSize = message.FileSize,
            RequestId = message.RequestId ?? string.Empty,
            Status = message.Status ?? string.Empty,
            TimestampUtc = timestampUtc
        };
    }

    private static DeviceChatMessage CopyWithId(DeviceChatMessage message, long id)
    {
        return new DeviceChatMessage
        {
            Id = id,
            PeerKey = message.PeerKey,
            PeerId = message.PeerId,
            PeerName = message.PeerName,
            PeerAddress = message.PeerAddress,
            PeerPort = message.PeerPort,
            Direction = message.Direction,
            EntryType = message.EntryType,
            Content = message.Content,
            FileName = message.FileName,
            FilePath = message.FilePath,
            FileSize = message.FileSize,
            RequestId = message.RequestId,
            Status = message.Status,
            TimestampUtc = message.TimestampUtc
        };
    }

    private static DateTime ReadUtcTicks(long ticks)
    {
        if (ticks <= 0)
        {
            return DateTime.UtcNow;
        }

        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
