using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AgyCliDashboard.Services;

public class DatabaseService : IDatabaseService
{
    private static readonly string BrainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
    private static readonly string ConversationsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversations");
    private static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
    public static readonly string JetboxPbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "jetbox_summaries_proto.pb");
    public static readonly string PresencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "presence");
    public static bool IsProtobufTurboActive { get; private set; }
    public static string LoaderStatusMessage { get; private set; } = "Initialisierung...";
    private static readonly string TranscriptCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_transcript_cache.json");

    public class TranscriptCacheItem
    {
        public long LastWriteTicks { get; set; }
        public List<string> SubagentIds { get; set; } = new();
        public string? FallbackTitle { get; set; }
        public int StepCount { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TranscriptCacheItem> _transcriptCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _isCacheLoaded = false;
    private static readonly object _cacheLock = new();

    public static void EnsureTranscriptCacheLoaded()
    {
        if (_isCacheLoaded) return;
        lock (_cacheLock)
        {
            if (_isCacheLoaded) return;
            try
            {
                if (File.Exists(TranscriptCachePath))
                {
                    string json = File.ReadAllText(TranscriptCachePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, TranscriptCacheItem>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _transcriptCache[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] EnsureTranscriptCacheLoaded error: {ex.Message}");
            }
            finally
            {
                _isCacheLoaded = true;
            }
        }
    }

    public static void SaveTranscriptCache()
    {
        lock (_cacheLock)
        {
            string tmp = TranscriptCachePath + ".tmp";
            try
            {
                var dict = new Dictionary<string, TranscriptCacheItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _transcriptCache)
                {
                    dict[kvp.Key] = new TranscriptCacheItem
                    {
                        LastWriteTicks = kvp.Value.LastWriteTicks,
                        SubagentIds = kvp.Value.SubagentIds.ToList(),
                        StepCount = kvp.Value.StepCount,
                        FallbackTitle = kvp.Value.FallbackTitle,
                        CreatedAtUtc = kvp.Value.CreatedAtUtc
                    };
                }
                string json = JsonSerializer.Serialize(dict);
                File.WriteAllText(tmp, json);
                File.Move(tmp, TranscriptCachePath, true);
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] SaveTranscriptCache error: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch { }
            }
        }
    }

    private static async Task<TranscriptCacheItem> ParseTranscriptFastAsync(string transcriptFilePath, string convId, long writeTicks)
    {
        var item = new TranscriptCacheItem
        {
            LastWriteTicks = writeTicks
        };

        try
        {
            using var fs = new FileStream(transcriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, useAsync: true);
            using var reader = new StreamReader(fs);
            string? line;
            int maxStep = 0;
            bool hasUserInput = false;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 1. Subagents detection
                if (line.Contains("Created the following subagents:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("active subagent(s):", StringComparison.OrdinalIgnoreCase))
                {
                    var matches = SubagentConvIdRegex.Matches(line);
                    foreach (Match m in matches)
                    {
                        string foundId = m.Groups[1].Value;
                        if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!item.SubagentIds.Contains(foundId, StringComparer.OrdinalIgnoreCase))
                                item.SubagentIds.Add(foundId);
                        }
                    }
                }
                else if (line.Contains("\"name\":\"manage_subagents\"", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("\\\"name\\\":\\\"manage_subagents\\\"", StringComparison.OrdinalIgnoreCase))
                {
                    var matches = SubagentManageRegex.Matches(line);
                    foreach (Match m in matches)
                    {
                        string foundId = m.Groups[1].Value;
                        if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!item.SubagentIds.Contains(foundId, StringComparer.OrdinalIgnoreCase))
                                item.SubagentIds.Add(foundId);
                        }
                    }
                }
                else if (line.Contains("\"name\":\"schedule\"", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("\\\"name\\\":\\\"schedule\\\"", StringComparison.OrdinalIgnoreCase))
                {
                    var matches = SubagentTimerRegex.Matches(line);
                    foreach (Match m in matches)
                    {
                        string foundId = m.Groups[1].Value;
                        if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!item.SubagentIds.Contains(foundId, StringComparer.OrdinalIgnoreCase))
                                item.SubagentIds.Add(foundId);
                        }
                    }
                }

                // 2. Step index tracking & Fallback title
                if (line.Contains("\"step_index\"", StringComparison.Ordinal) || line.Contains("\"USER_INPUT\"", StringComparison.Ordinal) || line.Contains("\"USER_EXPLICIT\"", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("step_index", out var stepProp) && stepProp.TryGetInt32(out int s))
                        {
                            if (s > maxStep) maxStep = s;
                        }

                        if (!hasUserInput)
                        {
                            bool isUserStep = (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "USER_INPUT") ||
                                              (root.TryGetProperty("source", out var srcProp) && srcProp.GetString() == "USER_EXPLICIT");
                            if (isUserStep)
                            {
                                if (root.TryGetProperty("created_at", out var catProp))
                                {
                                    if (DateTimeOffset.TryParse(catProp.GetString(), out var dto))
                                    {
                                        item.CreatedAtUtc = dto.UtcDateTime;
                                    }
                                }
                                if (root.TryGetProperty("content", out var contentProp))
                                {
                                    string raw = contentProp.GetString() ?? "";
                                    string clean = CleanUserInputToTitle(raw);
                                    if (!string.IsNullOrWhiteSpace(clean))
                                    {
                                        item.FallbackTitle = clean;
                                        hasUserInput = true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            item.StepCount = maxStep > 0 ? maxStep + 1 : 1;
            if (item.CreatedAtUtc == null)
            {
                try { item.CreatedAtUtc = File.GetCreationTimeUtc(transcriptFilePath); } catch { }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[DatabaseService] ParseTranscriptFastAsync error for {convId}: {ex.Message}");
        }

        return item;
    }

    private static readonly Regex WorkspaceUriRegex = new(@"[A-Za-z]:\\[^""\n\r<\]]+|[A-Za-z]:\/[^""\n\r<\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WorkspaceCleanRegex = new(@"(\.\\n|\\n- |\\n1\. | an:\\n|`.*$|' and .*$| im Detail\..*$|^s:\\n.*$|^e:\\n.*$).*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex SubagentConvIdRegex = new(
        @"(?:\\""|"")conversationId(?:\\""|"")\s*:\s*(?:\\""|"")([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:\\""|"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    [Obsolete("send_message Recipient is often a parent conversation ID and should not be used to detect subagents.")]
    public static readonly Regex SubagentRecipientRegex = new(
        @"(?:\\""|"")Recipient(?:\\""|"")\s*:\s*(?:\\""|"")([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:\\""|"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex SubagentTimerRegex = new(
        @"(?:\\""|"")TimerCondition(?:\\""|"")\s*:\s*(?:\\""|"")([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:\\""|"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex SubagentManageRegex = new(
        @"(?:\\""|"")ConversationIds(?:\\""|"")\s*:\s*\[\s*(?:\\""|"")([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<List<AgyChatSession>> LoadChatsAsync()
    {
        return await Task.Run(async () =>
        {
            var chats = new List<AgyChatSession>();
            var dbSummaries = new Dictionary<string, (string Title, string Workspace, string ParentId, DateTime LastModified, int StepCount)>(StringComparer.OrdinalIgnoreCase);

            // 1. Primär: High-Speed Protobuf-Lader (jetbox_summaries_proto.pb)
            int pbLoadedCount = 0;
            if (File.Exists(JetboxPbPath))
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var pbSummaries = JetboxProtoReader.ReadSummaries(JetboxPbPath);
                    sw.Stop();
                    if (pbSummaries != null && pbSummaries.Count > 0)
                    {
                        foreach (var item in pbSummaries)
                        {
                            string title = item.Title;
                            string workspace = item.WorkspacePath;
                            if (!string.IsNullOrEmpty(workspace))
                            {
                                workspace = NormalizePath(workspace);
                            }
                            dbSummaries[item.ConversationId] = (title, workspace, item.ParentId ?? string.Empty, item.LastModified, item.StepCount);
                        }
                        pbLoadedCount = dbSummaries.Count;
                        IsProtobufTurboActive = true;
                        LoaderStatusMessage = $"⚡ Turbo-Lader ({pbLoadedCount} Chats in {sw.ElapsedMilliseconds} ms geladen)";
                        TelemetryLogger.Log($"[DatabaseService] {LoaderStatusMessage}");
                    }
                }
                catch (Exception ex)
                {
                    IsProtobufTurboActive = false;
                    LoaderStatusMessage = $"🐢 Fallback: Protobuf-Fehler ({ex.Message})";
                    TelemetryLogger.Log($"[DatabaseService] Protobuf loader fallback to SQLite: {ex.Message}");
                }
            }

            // 2. Ergänzung & Fallback: SQLite-Datenbank (für ältere Chats >30 Tage oder wenn Protobuf fehlt)
            if (File.Exists(DbPath))
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=5;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT conversation_id, title, workspace_uris, parent_conversation_id, last_modified_time, preview, step_count FROM conversation_summaries";
                    using var reader = cmd.ExecuteReader();
                    int sqliteAdded = 0;
                    while (reader.Read())
                    {
                        string id = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        if (string.IsNullOrEmpty(id)) continue;

                        // Nur ergänzen, wenn nicht bereits via Protobuf geladen
                        if (dbSummaries.ContainsKey(id)) continue;

                        string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        string preview = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        int dbStepCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);

                        if (IsValidAgyTitle(preview, dbStepCount) && (string.IsNullOrWhiteSpace(title) || title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase) || title.Equals("Untitled", StringComparison.OrdinalIgnoreCase)))
                        {
                            title = preview;
                        }
                        string uris = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        string parentId = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        DateTime modTime = DateTime.MinValue;
                        if (!reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), out DateTime parsed))
                        {
                            modTime = parsed;
                        }

                        string workspace = string.Empty;
                        if (!string.IsNullOrEmpty(uris))
                        {
                            try
                            {
                                using var uriDoc = JsonDocument.Parse(uris);
                                if (uriDoc.RootElement.ValueKind == JsonValueKind.Array && uriDoc.RootElement.GetArrayLength() > 0)
                                {
                                    string rawFirst = uriDoc.RootElement[0].GetString() ?? "";
                                    if (!string.IsNullOrEmpty(rawFirst))
                                    {
                                        workspace = NormalizePath(rawFirst);
                                    }
                                }
                            }
                            catch
                            {
                                try
                                {
                                    string unescaped = Uri.UnescapeDataString(uris);
                                    var match = WorkspaceUriRegex.Match(unescaped);
                                    if (match.Success)
                                    {
                                        workspace = NormalizePath(match.Value);
                                    }
                                }
                                catch
                                {
                                    var match = WorkspaceUriRegex.Match(uris);
                                    if (match.Success)
                                    {
                                        workspace = NormalizePath(match.Value);
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(workspace))
                        {
                            workspace = WorkspaceCleanRegex.Replace(workspace, "");
                            int newlineIndex = workspace.IndexOfAny(new[] { '\n', '\r' });
                            if (newlineIndex >= 0)
                            {
                                workspace = workspace.Substring(0, newlineIndex);
                            }
                            workspace = NormalizePath(workspace.TrimEnd('.', ',', ';', ':', ' '));
                        }

                        dbSummaries[id] = (title, workspace, parentId, modTime, dbStepCount);
                        sqliteAdded++;
                    }

                    if (pbLoadedCount == 0)
                    {
                        IsProtobufTurboActive = false;
                        LoaderStatusMessage = $"🐢 SQLite-Modus ({dbSummaries.Count} Chats geladen)";
                        TelemetryLogger.Log($"[DatabaseService] {LoaderStatusMessage}");
                    }
                    else if (sqliteAdded > 0)
                    {
                        TelemetryLogger.Log($"[DatabaseService] Hybrid-Loader: {sqliteAdded} ältere Archiv-Chats aus SQLite ergänzt (Total: {dbSummaries.Count}).");
                    }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"DatabaseService.LoadChatsAsync DB error: {ex.Message}");
                }
            }

            if (Directory.Exists(BrainPath))
            {
                var subagentIds = await CollectSubagentIdsAsync(BrainPath);
                var directories = Directory.GetDirectories(BrainPath);
                CheckBrainTripwire(directories);

                foreach (var dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.Equals("scratch", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith("proofabc-", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool hasParentInDb = dbSummaries.TryGetValue(dirName, out var summary) &&
                                         !string.IsNullOrWhiteSpace(summary.ParentId);

                    if (hasParentInDb || subagentIds.Contains(dirName))
                    {
                        continue;
                    }

                    string transcriptFile = Path.Combine(dir, ".system_generated", "logs", "transcript.jsonl");
                    if (File.Exists(transcriptFile))
                    {
                        _transcriptCache.TryGetValue(dirName, out var cachedMeta);

                        DateTime dirCreation = cachedMeta?.CreatedAtUtc != null && cachedMeta.CreatedAtUtc != DateTime.MinValue
                            ? cachedMeta.CreatedAtUtc.Value.ToLocalTime()
                            : Directory.GetCreationTime(dir);

                        DateTime fileWrite = cachedMeta != null && cachedMeta.LastWriteTicks > 0
                            ? new DateTime(cachedMeta.LastWriteTicks, DateTimeKind.Utc).ToLocalTime()
                            : File.GetLastWriteTime(transcriptFile);

                        bool isRecent = (DateTime.Now - dirCreation).TotalMinutes < 30 || (DateTime.Now - fileWrite).TotalMinutes < 30;

                        if (summary == default && !isRecent)
                        {
                            continue;
                        }

                        int stepCount = summary.StepCount;
                        if (stepCount <= 0 && cachedMeta != null && cachedMeta.StepCount > 0)
                        {
                            stepCount = cachedMeta.StepCount;
                        }
                        else if (stepCount <= 0)
                        {
                            try
                            {
                                using (var countStream = new FileStream(transcriptFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (var countReader = new StreamReader(countStream))
                                {
                                    while (countReader.ReadLine() != null) stepCount++;
                                }
                            }
                            catch { }
                        }

                        try
                        {
                            DateTime dbLastMod = summary.LastModified;
                            DateTime lastModified = dbLastMod != DateTime.MinValue
                                ? (dbLastMod > fileWrite ? dbLastMod : fileWrite)
                                : (fileWrite != DateTime.MinValue ? fileWrite : Directory.GetLastWriteTime(dir));

                            DateTime createdAt = dirCreation != DateTime.MinValue ? dirCreation : lastModified;
                            string title = summary.Title;
                            string workspace = summary.Workspace;

                            if (string.IsNullOrWhiteSpace(title) || title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase) || title.Equals("Untitled", StringComparison.OrdinalIgnoreCase))
                            {
                                if (cachedMeta != null && !string.IsNullOrWhiteSpace(cachedMeta.FallbackTitle))
                                {
                                    title = cachedMeta.FallbackTitle;
                                }
                                else
                                {
                                    string? fallback = ExtractFallbackTitleFromTranscript(transcriptFile);
                                    if (!string.IsNullOrWhiteSpace(fallback))
                                    {
                                        title = fallback;
                                        if (cachedMeta != null) cachedMeta.FallbackTitle = fallback;
                                    }
                                    else if (string.IsNullOrWhiteSpace(title))
                                    {
                                        title = "Neuer Chat";
                                    }
                                }
                            }

                            chats.Add(new AgyChatSession
                            {
                                Id = dirName,
                                Title = title,
                                CreatedAt = createdAt,
                                LastModified = lastModified,
                                Workspace = workspace,
                                StepCount = stepCount
                            });
                        }
                        catch { }
                    }
                }
            }

            return chats;
        });
    }

    private static readonly string TripwireLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_tripwire.log");
    private static readonly string BrainSnapshotPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_brain_snapshot.json");
    private static readonly object _snapshotLock = new();
    private static HashSet<string>? _brainSnapshot;

    private static HashSet<string> LoadBrainSnapshotLocked()
    {
        if (_brainSnapshot != null) return _brainSnapshot;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(BrainSnapshotPath))
            {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BrainSnapshotPath));
                if (ids != null) foreach (var id in ids) set.Add(id);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Tripwire] Snapshot load error: {ex.Message}");
        }
        _brainSnapshot = set;
        return set;
    }

    private static void SaveBrainSnapshotLocked()
    {
        try
        {
            if (_brainSnapshot == null) return;
            string tmp = BrainSnapshotPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_brainSnapshot.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()));
            File.Move(tmp, BrainSnapshotPath, true);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Tripwire] Snapshot save error: {ex.Message}");
        }
    }

    /// <summary>Entfernt vom Dashboard bewusst gelöschte Chats aus dem Snapshot, damit sie nicht als "verschwunden" gemeldet werden.</summary>
    private static void ForgetInBrainSnapshot(IEnumerable<string> ids)
    {
        try
        {
            lock (_snapshotLock)
            {
                var snap = LoadBrainSnapshotLocked();
                bool changed = false;
                foreach (var id in ids) if (snap.Remove(id)) changed = true;
                if (changed) SaveBrainSnapshotLocked();
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Tripwire] Forget error: {ex.Message}");
        }
    }

    /// <summary>Vergleicht die aktuellen Brain-Ordner mit dem letzten Snapshot. Ordner, die ohne Dashboard-Löschung
    /// verschwunden sind, werden in dashboard_tripwire.log protokolliert (Erkennung externer Löschvorgänge).</summary>
    private static void CheckBrainTripwire(string[] brainDirectories)
    {
        try
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in brainDirectories)
            {
                string name = Path.GetFileName(dir);
                if (Guid.TryParse(name, out _)) current.Add(name);
            }

            lock (_snapshotLock)
            {
                bool firstRun = !File.Exists(BrainSnapshotPath) && _brainSnapshot == null;
                var snap = LoadBrainSnapshotLocked();
                var vanished = snap.Where(id => !current.Contains(id)).ToList();
                if (vanished.Count > 0 && !firstRun)
                {
                    string msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} TRIPWIRE: {vanished.Count} brain folder(s) vanished without dashboard deletion (remaining: {current.Count}). IDs: {string.Join(", ", vanished.Take(100))}";
                    TelemetryLogger.Log("[Tripwire] " + msg);
                    try { File.AppendAllText(TripwireLogPath, msg + Environment.NewLine); } catch { }
                }

                if (!snap.SetEquals(current))
                {
                    snap.Clear();
                    foreach (var id in current) snap.Add(id);
                    SaveBrainSnapshotLocked();
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Tripwire] Check error: {ex.Message}");
        }
    }

    public async Task<List<string>> DeleteChatSessionAsync(string conversationId)
    {
        return await Task.Run(() =>
        {
            if (IsPresenceLocked(conversationId))
            {
                TelemetryLogger.Log($"[DatabaseService] Deletion of '{conversationId}' aborted: Chat is currently locked and active in a CLI process.");
                return new List<string>();
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var (topologyOk, topLevelIds, parentMap) = LoadSummaryTopology();
            if (!topologyOk)
            {
                TelemetryLogger.Log($"[DatabaseService] Deletion of '{conversationId}' aborted: summary DB topology unreadable (fail-closed).");
                return new List<string>();
            }
            DeleteChatRecursiveInternal(conversationId, visited, failed, topLevelIds, parentMap);

            // Nur wirklich gelöschte Chats melden (bei DB-Fehler bleibt der Chat unverändert bestehen).
            var deleted = visited.Where(id => !failed.Contains(id)).ToList();
            TelemetryLogger.Log($"[DatabaseService] Deletion finished for '{conversationId}': {deleted.Count} deleted, {failed.Count} failed. IDs: {string.Join(", ", deleted)}");

            bool dirty = false;
            foreach (var id in deleted)
            {
                if (_transcriptCache.TryRemove(id, out _)) dirty = true;
            }
            if (dirty) SaveTranscriptCache();
            ForgetInBrainSnapshot(deleted);
            return deleted;
        });
    }

    /// <summary>Liest einmalig alle Top-Level-IDs und die Eltern-Zuordnung (id → parent) aus der Summary-DB.
    /// Ok=false, wenn die DB existiert, aber nicht gelesen werden konnte (dann darf nicht gelöscht werden).</summary>
    private static (bool Ok, HashSet<string> TopLevel, Dictionary<string, string> ParentMap) LoadSummaryTopology()
    {
        var topLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(DbPath)) return (true, topLevel, parentMap);
        try
        {
            using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=5;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT conversation_id, parent_conversation_id FROM conversation_summaries";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0)) continue;
                string id = reader.GetString(0);
                string parent = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(id)) continue;
                parentMap[id] = parent ?? "";
                if (string.IsNullOrWhiteSpace(parent)) topLevel.Add(id);
            }
            return (true, topLevel, parentMap);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[DatabaseService] LoadSummaryTopology error: {ex.Message}");
            return (false, topLevel, parentMap);
        }
    }

    private void DeleteChatRecursiveInternal(string convId, HashSet<string> visited, HashSet<string> failed,
        HashSet<string> topLevelIds, Dictionary<string, string> parentMap)
    {
        if (string.IsNullOrWhiteSpace(convId) || visited.Contains(convId))
            return;
        visited.Add(convId);

        TelemetryLogger.Log($"[DatabaseService] Starting deep deletion for conversation ID: '{convId}'");

        var subagentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(DbPath))
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=5;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT conversation_id FROM conversation_summaries WHERE parent_conversation_id = @parentId";
                cmd.Parameters.AddWithValue("@parentId", convId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        string id = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            subagentIds.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error searching subagents for '{convId}': {ex.Message}");
            }
        }

        if (File.Exists(JetboxPbPath))
        {
            try
            {
                var pbSummaries = JetboxProtoReader.ReadSummaries(JetboxPbPath);
                if (pbSummaries != null)
                {
                    foreach (var itm in pbSummaries)
                    {
                        if (string.Equals(itm.ConversationId, convId, StringComparison.OrdinalIgnoreCase))
                        {
                            if (itm.SubagentIds != null)
                            {
                                foreach (var sId in itm.SubagentIds) subagentIds.Add(sId);
                            }
                        }
                        else if (string.Equals(itm.ParentId, convId, StringComparison.OrdinalIgnoreCase))
                        {
                            subagentIds.Add(itm.ConversationId);
                        }
                    }
                }
            }
            catch { }
        }

        if (subagentIds.Count == 0)
        {
            string parentTranscript = Path.Combine(BrainPath, convId, ".system_generated", "logs", "transcript.jsonl");
            if (File.Exists(parentTranscript))
            {
                try
                {
                    using var fs = new FileStream(parentTranscript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Contains("Created the following subagents:", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("active subagent(s):", StringComparison.OrdinalIgnoreCase))
                        {
                            var matches = SubagentConvIdRegex.Matches(line);
                            foreach (Match m in matches)
                            {
                                string foundId = m.Groups[1].Value;
                                if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                                {
                                    subagentIds.Add(foundId);
                                }
                            }
                        }
                        else if (line.Contains("\"name\":\"manage_subagents\"", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("\\\"name\\\":\\\"manage_subagents\\\"", StringComparison.OrdinalIgnoreCase))
                        {
                            var matches = SubagentManageRegex.Matches(line);
                            foreach (Match m in matches)
                            {
                                string foundId = m.Groups[1].Value;
                                if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                                {
                                    subagentIds.Add(foundId);
                                }
                            }
                        }
                        else if (line.Contains("\"name\":\"schedule\"", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("\\\"name\\\":\\\"schedule\\\"", StringComparison.OrdinalIgnoreCase))
                        {
                            var matches = SubagentTimerRegex.Matches(line);
                            foreach (Match m in matches)
                            {
                                string foundId = m.Groups[1].Value;
                                if (!string.IsNullOrEmpty(foundId) && !string.Equals(foundId, convId, StringComparison.OrdinalIgnoreCase))
                                {
                                    subagentIds.Add(foundId);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        foreach (var childId in subagentIds)
        {
            // Fail-closed: Nur Kinder kaskadieren, deren Elternschaft in der DB belegt ist.
            // Top-Level-Chats, Kinder fremder Eltern und unbekannte IDs (Transkript-Treffer ohne DB-Beleg) sind tabu.
            if (topLevelIds.Contains(childId))
            {
                TelemetryLogger.Log($"[DatabaseService] Cascade skipped top-level chat '{childId}' (referenced by '{convId}').");
                continue;
            }
            if (!parentMap.TryGetValue(childId, out var childParent) || string.IsNullOrWhiteSpace(childParent))
            {
                TelemetryLogger.Log($"[DatabaseService] Cascade skipped '{childId}': no DB parent relation proven (referenced by '{convId}').");
                continue;
            }
            if (!string.Equals(childParent, convId, StringComparison.OrdinalIgnoreCase) && !visited.Contains(childParent))
            {
                TelemetryLogger.Log($"[DatabaseService] Cascade skipped '{childId}': belongs to foreign parent '{childParent}'.");
                continue;
            }
            DeleteChatRecursiveInternal(childId, visited, failed, topLevelIds, parentMap);
        }

        // 1. DB-Zeile ZUERST löschen. Schlägt das fehl, bleibt der Chat komplett unangetastet
        // (kein Geister-Eintrag mit bereits gelöschtem Ordner).
        if (!TryDeleteSummaryRow(convId))
        {
            failed.Add(convId);
            TelemetryLogger.Log($"[DatabaseService] Deletion of '{convId}' aborted: DB row could not be removed, folders left untouched.");
            return;
        }

        // 2. Erst danach Ordner und Konversations-DB entfernen (Snapshot vorher aktualisieren, kein Tripwire-Fehlalarm).
        ForgetInBrainSnapshot(new[] { convId });
        string brainFolder = Path.Combine(BrainPath, convId);
        if (Directory.Exists(brainFolder))
        {
            try
            {
                Directory.Delete(brainFolder, true);
                TelemetryLogger.Log($"[DatabaseService] Deleted brain folder: {brainFolder}");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error deleting brain folder '{brainFolder}': {ex.Message}");
            }
        }

        string convFolder = Path.Combine(ConversationsPath, convId);
        if (Directory.Exists(convFolder))
        {
            try
            {
                Directory.Delete(convFolder, true);
                TelemetryLogger.Log($"[DatabaseService] Deleted conversation folder: {convFolder}");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error deleting conversation folder '{convFolder}': {ex.Message}");
            }
        }

        string convDbFile = Path.Combine(ConversationsPath, $"{convId}.db");
        if (File.Exists(convDbFile))
        {
            try
            {
                File.Delete(convDbFile);
                TelemetryLogger.Log($"[DatabaseService] Deleted conversation DB file: {convDbFile}");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error deleting conversation DB file '{convDbFile}': {ex.Message}");
            }
        }

        string lockFilePath = Path.Combine(PresencePath, $"{convId}.lock");
        if (File.Exists(lockFilePath))
        {
            try
            {
                File.Delete(lockFilePath);
                TelemetryLogger.Log($"[DatabaseService] Deleted presence lock file: {lockFilePath}");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error deleting presence lock file '{lockFilePath}': {ex.Message}");
            }
        }
    }

    /// <summary>Löscht nur die eigene Zeile des Chats (Kinder löschen ihre Zeile in ihrer eigenen Rekursion).
    /// Liefert true, wenn keine DB existiert oder die Zeile entfernt wurde.</summary>
    private static bool TryDeleteSummaryRow(string convId)
    {
        if (!File.Exists(DbPath)) return true;

        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={DbPath};Default Timeout=10;");
                conn.Open();
                using (var pragmaCmd = conn.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
                    pragmaCmd.ExecuteNonQuery();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM conversation_summaries WHERE conversation_id = @convId";
                cmd.Parameters.AddWithValue("@convId", convId);
                int rows = cmd.ExecuteNonQuery();
                TelemetryLogger.Log($"[DatabaseService] Removed {rows} row(s) for '{convId}' from DB.");
                return true;
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] Error deleting DB entry for '{convId}' (attempt {attempt}/{maxRetries}): {ex.Message}");
                if (attempt < maxRetries) Thread.Sleep(250);
            }
        }
        return false;
    }

    public static async Task<HashSet<string>> CollectSubagentIdsAsync(string? brainPath = null)
    {
        var subagentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownTopLevelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primär: Subagents direkt aus jetbox_summaries_proto.pb laden (<5 ms)
        if (File.Exists(JetboxPbPath))
        {
            try
            {
                var pbSummaries = JetboxProtoReader.ReadSummaries(JetboxPbPath);
                if (pbSummaries != null && pbSummaries.Count > 0)
                {
                    foreach (var item in pbSummaries)
                    {
                        if (item.IsSubagent && !string.IsNullOrEmpty(item.ConversationId))
                        {
                            subagentIds.Add(item.ConversationId);
                        }
                        else if (!string.IsNullOrEmpty(item.ConversationId) && !item.IsSubagent)
                        {
                            knownTopLevelIds.Add(item.ConversationId);
                        }

                        if (item.SubagentIds != null && item.SubagentIds.Count > 0)
                        {
                            foreach (var sId in item.SubagentIds)
                            {
                                if (!string.IsNullOrEmpty(sId))
                                    subagentIds.Add(sId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] CollectSubagentIdsAsync Protobuf error: {ex.Message}");
            }
        }

        // 2. Ergänzung & Fallback: SQLite conversation_summaries.db (<2 ms)
        if (File.Exists(DbPath))
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=3;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT conversation_id, parent_conversation_id FROM conversation_summaries";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        string id = reader.GetString(0);
                        string parent = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            if (!string.IsNullOrWhiteSpace(parent))
                            {
                                subagentIds.Add(id);
                            }
                            else
                            {
                                knownTopLevelIds.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[DatabaseService] CollectSubagentIdsAsync SQLite error: {ex.Message}");
            }
        }

        if (knownTopLevelIds.Count > 0)
        {
            subagentIds.ExceptWith(knownTopLevelIds);
        }

        return await Task.FromResult(subagentIds);
    }

    private static bool IsValidAgyTitle(string preview, int dbStepCount)
    {
        if (dbStepCount < 2) return false;
        if (string.IsNullOrWhiteSpace(preview)) return false;
        if (preview.Length < 3 || preview.Length > 80) return false;
        if (preview.Contains('{') || preview.Contains('}') || preview.Contains('<') || preview.Contains('>')) return false;
        if (preview.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || preview.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static readonly Regex UserRequestRegex = new(@"<USER_REQUEST>([\s\S]*?)<\/USER_REQUEST>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketInstructionsRegex = new(@"\[[\s\S]*?\]", RegexOptions.Compiled);
    private static readonly Regex SlashCommandsRegex = new(@"\/(plan|goal|schedule|grill-me|learn|resume)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex XmlTagsRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceCollapseRegex = new(@"\s+", RegexOptions.Compiled);

    public static (string? Title, DateTime? CreatedAtUtc, DateTime? LastInputUtc) ExtractFallbackInfoFromTranscript(string transcriptFilePath)
    {
        if (string.IsNullOrEmpty(transcriptFilePath) || !File.Exists(transcriptFilePath)) return (null, null, null);

        EnsureTranscriptCacheLoaded();
        string convId = string.Empty;
        var parts = transcriptFilePath.Replace('/', '\\').Split('\\');
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (parts[i].Equals("brain", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                convId = parts[i + 1];
                break;
            }
        }

        long writeTicks = 0;
        try { writeTicks = File.GetLastWriteTimeUtc(transcriptFilePath).Ticks; } catch { }

        if (!string.IsNullOrEmpty(convId) && _transcriptCache.TryGetValue(convId, out var cached) && cached.LastWriteTicks == writeTicks && !string.IsNullOrWhiteSpace(cached.FallbackTitle))
        {
            DateTime lastInput = new DateTime(cached.LastWriteTicks, DateTimeKind.Utc);
            return (cached.FallbackTitle, cached.CreatedAtUtc, lastInput);
        }

        string? foundTitle = null;
        DateTime? foundCreatedAtUtc = null;
        DateTime? lastInputUtc = null;

        try
        {
            using var fs = new FileStream(transcriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("\"USER_INPUT\"", StringComparison.Ordinal) && !line.Contains("\"USER_EXPLICIT\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    bool isUserStep = (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "USER_INPUT") ||
                                      (root.TryGetProperty("source", out var srcProp) && srcProp.GetString() == "USER_EXPLICIT");
                    if (isUserStep)
                    {
                        if (root.TryGetProperty("created_at", out var catProp))
                        {
                            if (DateTimeOffset.TryParse(catProp.GetString(), out var dto))
                            {
                                if (foundCreatedAtUtc == null)
                                {
                                    foundCreatedAtUtc = dto.UtcDateTime;
                                }
                                lastInputUtc = dto.UtcDateTime;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(foundTitle) && root.TryGetProperty("content", out var contentProp))
                        {
                            string rawContent = contentProp.GetString() ?? "";
                            string clean = CleanUserInputToTitle(rawContent);
                            if (!string.IsNullOrWhiteSpace(clean))
                            {
                                foundTitle = clean;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        if (foundCreatedAtUtc == null && File.Exists(transcriptFilePath))
        {
            try
            {
                foundCreatedAtUtc = File.GetCreationTimeUtc(transcriptFilePath);
            }
            catch { }
        }

        if (lastInputUtc == null && File.Exists(transcriptFilePath))
        {
            try
            {
                lastInputUtc = File.GetLastWriteTimeUtc(transcriptFilePath);
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(convId) && writeTicks > 0)
        {
            var entry = _transcriptCache.GetOrAdd(convId, _ => new TranscriptCacheItem());
            if (!string.IsNullOrWhiteSpace(foundTitle)) entry.FallbackTitle = foundTitle;
            if (foundCreatedAtUtc != null) entry.CreatedAtUtc = foundCreatedAtUtc;
            if (entry.StepCount > 0) entry.LastWriteTicks = writeTicks;
            SaveTranscriptCache();
        }

        return (foundTitle, foundCreatedAtUtc, lastInputUtc);
    }

    public static string? ExtractFallbackTitleFromTranscript(string transcriptFilePath)
    {
        return ExtractFallbackInfoFromTranscript(transcriptFilePath).Title;
    }

    public static string CleanUserInputToTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // 1. Extract content within <USER_REQUEST> if present
        var matchReq = UserRequestRegex.Match(raw);
        string text = matchReq.Success ? matchReq.Groups[1].Value : raw;

        // 2. Remove [bracketed instructions] - falls der Text jedoch vollständig aus Klammern besteht (z. B. Action-Prompts wie [Suchlauf] oder [Dead-Code]), Klammern entpacken statt leeren Titel zu erzeugen
        string textWithoutBrackets = BracketInstructionsRegex.Replace(text, " ");
        string testCollapse = WhitespaceCollapseRegex.Replace(textWithoutBrackets, " ").Trim();
        if (!string.IsNullOrWhiteSpace(testCollapse))
        {
            text = textWithoutBrackets;
        }
        else
        {
            text = text.Replace('[', ' ').Replace(']', ' ');
        }

        // 3. Remove slash commands
        text = SlashCommandsRegex.Replace(text, " ");

        // 4. Remove other XML / HTML tags like <ADDITIONAL_METADATA>...</ADDITIONAL_METADATA>
        text = XmlTagsRegex.Replace(text, " ");

        // 5. Collapse all whitespace/newlines
        text = WhitespaceCollapseRegex.Replace(text, " ").Trim();

        if (string.IsNullOrWhiteSpace(text)) return "";

        // 6. If it starts with 'go ' and has further text, strip leading 'go '
        if (text.StartsWith("go ", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(3).Trim();
        }

        // If it's just 'go', '[go]' or pure control words, return empty string so next input can be checked
        if (text.Equals("go", StringComparison.OrdinalIgnoreCase) || text.Equals("[go]", StringComparison.OrdinalIgnoreCase) || text.Equals("/goal", StringComparison.OrdinalIgnoreCase) || text.Equals("[/plan go]", StringComparison.OrdinalIgnoreCase) || text.Equals("/plan [go]", StringComparison.OrdinalIgnoreCase) || text.Equals("/plan /goal", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (text.Length > 45)
        {
            text = text.Substring(0, 42).TrimEnd() + "...";
        }

        return text;
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            string unescaped = Uri.UnescapeDataString(path.Trim());
            if (unescaped.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                unescaped = unescaped.Substring(8);
            }
            else if (unescaped.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                unescaped = unescaped.Substring(7);
            }
            string cleaned = unescaped.Replace('/', '\\').TrimEnd('\\');
            if (cleaned.Length == 2 && char.IsLetter(cleaned[0]) && cleaned[1] == ':')
            {
                cleaned += "\\";
            }
            if (cleaned.Length >= 2 && char.IsLetter(cleaned[0]) && cleaned[1] == ':')
            {
                string full = Path.GetFullPath(cleaned);
                return full.Length > 3 ? full.TrimEnd('\\') : full;
            }
            return cleaned;
        }
        catch
        {
            return (path ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
        }
    }

    public sealed class NativeWorkingStateResult
    {
        public bool IsWorking { get; set; }
        public bool Evaluated { get; set; }
        public string? Source { get; set; }
        public string? Details { get; set; }
        public Exception? SchemaError { get; set; }
    }

    /// <summary>
    /// Prüft, ob eine physische Presence-Lockdatei für die Konversation existiert.
    /// </summary>
    public static bool HasPresenceLockFile(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return false;
        string lockFilePath = Path.Combine(PresencePath, $"{conversationId}.lock");
        return File.Exists(lockFilePath);
    }

    /// <summary>
    /// Prüft in ~1 Mikrosekunde über native Windows-Betriebssystem-Locks, ob für die Konversations-ID
    /// ein aktiver CLI-Prozess (agy.exe) läuft.
    /// Gibt true zurück, wenn die Datei exklusiv von agy.exe gesperrt ist (Session aktiv).
    /// Gibt false zurück, wenn die Datei entsperrt ist (Session geschlossen/beendet) oder nicht existiert.
    /// </summary>
    public static bool IsPresenceLocked(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return false;

        string lockFilePath = Path.Combine(PresencePath, $"{conversationId}.lock");
        if (!File.Exists(lockFilePath)) return false;

        try
        {
            // Versuche die Datei exklusiv zu öffnen.
            // Hält agy.exe die Datei gesperrt (store.acquirePresenceLock via FileShare.None),
            // schlägt Open mit einer IOException fehl -> Prozess läuft aktiv!
            using var fs = new FileStream(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false; // Konnte exklusiv geöffnet werden -> agy.exe läuft NICHT mehr!
        }
        catch (IOException)
        {
            // Datei ist vom Betriebssystem gesperrt -> agy.exe hält den Presence-Lock aktiv!
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Zugriff verweigert wegen exklusiver Sperre -> ebenfalls aktiv!
            return true;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PresenceLock] Error checking lock for {conversationId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Liest den nativen Arbeitszustand einer Antigravity-Sitzung direkt aus den Google SQLite-Datenbanken aus.
    /// Liefert SchemaError zurück, falls Google das Schema (Spalten/Tabellen) geändert hat, damit das Dashboard sofort warnen kann.
    /// </summary>
    public static NativeWorkingStateResult GetNativeConversationWorkingState(string conversationId)
    {
        if (string.IsNullOrEmpty(conversationId))
            return new NativeWorkingStateResult { Evaluated = false };

        var result = new NativeWorkingStateResult();

        // 0. Zero-CPU Concurrency & Zombie-Check via Windows Presence-Lock:
        // Wenn eine .lock-Datei existiert, aber nicht mehr gesperrt ist,
        // ist der agy.exe-Prozess definitiv beendet. Dies eliminiert verwaiste Zombie-Pulse bei Abstürzen in 0 ms.
        string lockFilePath = Path.Combine(PresencePath, $"{conversationId}.lock");
        if (File.Exists(lockFilePath) && !IsPresenceLocked(conversationId))
        {
            result.Evaluated = true;
            result.IsWorking = false;
            result.Source = "presence_lock";
            result.Details = "Lockfile exists and is unlocked (CLI process terminated)";
            return result;
        }

        // 1. Primär: Session-Transaktions-Datenbank conversations/<id>.db (Tabelle steps)
        string sessionDbPath = Path.Combine(ConversationsPath, $"{conversationId}.db");
        if (File.Exists(sessionDbPath))
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={sessionDbPath};Mode=ReadOnly;Default Timeout=2;");
                conn.Open();

                int idx = -1;
                int stepType = 0;
                int status = 0;
                bool hasStep = false;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT idx, step_type, status FROM steps ORDER BY idx DESC LIMIT 1;";
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        hasStep = true;
                        idx = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        stepType = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        status = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    }
                }

                if (hasStep)
                {
                    result.Evaluated = true;
                    result.Source = "conversations.steps";
                    result.Details = $"step={idx}, type={stepType}, status={status}";

                    // 1a. Status 2 = Running (Tool), 8 = Streaming (Model) => sofort aktiv
                    if (status == 2 || status == 8)
                    {
                        result.IsWorking = true;
                        return result;
                    }

                    // 1b. Prüfe executor_metadata auf den letzten abgeschlossenen/abgebrochenen Turn-Index (Protobuf Feld 3):
                    // Solange idx > lastCompletedStepIdx ist, befindet sich die KI mitten im Turn (z. B. 10-60s Deep-Think-Pause
                    // in der Cloud nach User-Input 14, Tool-Aufruf 132 oder Task-Notification 101), auch wenn sich auf der
                    // Festplatte sekundenlang keine Datei ändert und conversation_summaries.db noch auf IDLE steht!
                    int lastCompletedStepIdx = -1;
                    try
                    {
                        using var execCmd = conn.CreateCommand();
                        execCmd.CommandText = "SELECT substr(data, 1, 24) FROM executor_metadata ORDER BY idx DESC LIMIT 1;";
                        if (execCmd.ExecuteScalar() is byte[] execBytes && execBytes.Length > 0)
                        {
                            lastCompletedStepIdx = TryParseExecutorLastStepIdx(execBytes);
                        }
                    }
                    catch
                    {
                        // Falls executor_metadata nicht existiert, bleibt lastCompletedStepIdx = -1
                    }

                    DateTime lastDbWriteUtc = File.GetLastWriteTimeUtc(sessionDbPath);
                    string walPath = sessionDbPath + "-wal";
                    if (File.Exists(walPath))
                    {
                        DateTime walWriteUtc = File.GetLastWriteTimeUtc(walPath);
                        if (walWriteUtc > lastDbWriteUtc) lastDbWriteUtc = walWriteUtc;
                    }
                    bool isRecentStepActivity = (DateTime.UtcNow - lastDbWriteUtc).TotalMinutes < 10;

                    if (stepType != 15 && status != 6 && !(idx == 0 && stepType == 101))
                    {
                        if (idx > lastCompletedStepIdx && isRecentStepActivity)
                        {
                            result.IsWorking = true;
                            result.Details = $"step={idx}, type={stepType}, status={status}, exec_last={lastCompletedStepIdx} (thinking)";
                            return result;
                        }
                        else
                        {
                            result.IsWorking = false;
                        }
                    }
                    else
                    {
                        // stepType == 15 (Model-Response abgeschlossen) oder status == 6 (Hintergrund/Abbruch):
                        // Vordergrund-Schritt ist fertig; Teil 2 (conversation_summaries) prüft, ob noch Subagents/Tasks laufen.
                        result.IsWorking = false;
                    }
                }
            }
            catch (SqliteException ex)
            {
                // Echtes Schema-Problem nur, wenn Tabelle oder Spalte fehlt (SqliteErrorCode == 1)
                if (ex.SqliteErrorCode == 1 && (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)))
                {
                    result.SchemaError = ex;
                    TelemetryLogger.Log($"[NativeTrigger SchemaWarning] conversations/{conversationId}.db: {ex.Message}");
                }
                else
                {
                    TelemetryLogger.Log($"[NativeTrigger SqliteInfo] conversations/{conversationId}.db: {ex.Message} (Code: {ex.SqliteErrorCode})");
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[NativeTrigger Error] conversations/{conversationId}.db: {ex.Message}");
            }
        }

        // 2. Sekundär & Ergänzung: conversation_summaries.db (für not_fully_idle & Subagenten-Erkennung)
        if (File.Exists(DbPath))
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=2;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT not_fully_idle, status FROM conversation_summaries WHERE conversation_id = @id LIMIT 1;";
                cmd.Parameters.AddWithValue("@id", conversationId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    int notFullyIdle = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    string status = reader.IsDBNull(1) ? "" : reader.GetString(1);

                    // not_fully_idle == 1 bedeutet: Subagenten oder Hintergrund-Tasks laufen noch!
                    if (notFullyIdle == 1 || string.Equals(status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Evaluated = true;
                        result.IsWorking = true;
                        result.Source = "conversation_summaries";
                        result.Details = $"not_fully_idle={notFullyIdle}, status={status}";
                        return result;
                    }
                    else if (!result.Evaluated)
                    {
                        result.Evaluated = true;
                        result.IsWorking = false;
                        result.Source = "conversation_summaries";
                        result.Details = $"not_fully_idle={notFullyIdle}, status={status}";
                        return result;
                    }
                }
            }
            catch (SqliteException ex)
            {
                if (ex.SqliteErrorCode == 1 && (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)))
                {
                    result.SchemaError = ex;
                    TelemetryLogger.Log($"[NativeTrigger SchemaWarning] conversation_summaries.db: {ex.Message}");
                }
                else
                {
                    TelemetryLogger.Log($"[NativeTrigger SqliteInfo] conversation_summaries.db: {ex.Message} (Code: {ex.SqliteErrorCode})");
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[NativeTrigger Error] conversation_summaries.db: {ex.Message}");
            }
        }

        return result;
    }

    private static int TryParseExecutorLastStepIdx(ReadOnlySpan<byte> buf)
    {
        int pos = 0;
        while (pos < buf.Length)
        {
            if (!TryReadProtobufVarint(buf, ref pos, out ulong tag)) break;
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            if (wireType == 0)
            {
                if (!TryReadProtobufVarint(buf, ref pos, out ulong val)) break;
                if (fieldNumber == 3)
                {
                    return (int)val;
                }
            }
            else if (wireType == 2)
            {
                if (!TryReadProtobufVarint(buf, ref pos, out ulong len)) break;
                if (len > (ulong)(buf.Length - pos)) break;
                pos += (int)len;
            }
            else
            {
                break;
            }
        }
        return -1;
    }

    private static bool TryReadProtobufVarint(ReadOnlySpan<byte> buf, ref int pos, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < buf.Length && shift < 64)
        {
            byte b = buf[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }
        return false;
    }
}
