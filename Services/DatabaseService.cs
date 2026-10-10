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

            if (File.Exists(DbPath))
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=5;");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT conversation_id, title, workspace_uris, parent_conversation_id, last_modified_time, preview, step_count FROM conversation_summaries";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string id = reader.IsDBNull(0) ? "" : reader.GetString(0);
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

                        if (!string.IsNullOrEmpty(id))
                        {
                            dbSummaries[id] = (title, workspace, parentId, modTime, dbStepCount);
                        }
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

        EnsureTranscriptCacheLoaded();
        if (_transcriptCache.TryGetValue(convId, out var cachedItem) && cachedItem.SubagentIds != null && cachedItem.SubagentIds.Count > 0)
        {
            foreach (var sId in cachedItem.SubagentIds)
            {
                subagentIds.Add(sId);
            }
        }
        else
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

    public static async Task<HashSet<string>> CollectSubagentIdsAsync(string brainPath)
    {
        EnsureTranscriptCacheLoaded();
        var subagentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownTopLevelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect from SQLite conversation_summaries.db if parent_conversation_id is present
        if (File.Exists(DbPath))
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Default Timeout=5;");
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
            catch { }
        }

        // 2. Scan parent transcripts using persistent cache + delta scanning
        if (!Directory.Exists(brainPath)) return subagentIds;

        var dirs = Directory.EnumerateDirectories(brainPath);
        var filesToScan = new List<(string DirName, string TranscriptFile, long WriteTicks)>();

        foreach (var dir in dirs)
        {
            string parentConvId = Path.GetFileName(dir);
            if (parentConvId.Equals("scratch", StringComparison.OrdinalIgnoreCase) ||
                parentConvId.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase) ||
                parentConvId.StartsWith("proofabc-", StringComparison.OrdinalIgnoreCase) ||
                parentConvId.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string transcriptFile = Path.Combine(dir, ".system_generated", "logs", "transcript.jsonl");
            if (!File.Exists(transcriptFile)) continue;

            long writeTicks = File.GetLastWriteTimeUtc(transcriptFile).Ticks;
            if (_transcriptCache.TryGetValue(parentConvId, out var cached) && cached.LastWriteTicks == writeTicks)
            {
                if (cached.SubagentIds != null && cached.SubagentIds.Count > 0)
                {
                    foreach (var sId in cached.SubagentIds)
                    {
                        subagentIds.Add(sId);
                    }
                }
            }
            else
            {
                filesToScan.Add((parentConvId, transcriptFile, writeTicks));
            }
        }

        if (filesToScan.Count > 0)
        {
            await Parallel.ForEachAsync(filesToScan, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, async (item, ct) =>
            {
                var parsed = await ParseTranscriptFastAsync(item.TranscriptFile, item.DirName, item.WriteTicks);
                _transcriptCache[item.DirName] = parsed;
            });

            foreach (var item in filesToScan)
            {
                if (_transcriptCache.TryGetValue(item.DirName, out var entry) && entry.SubagentIds != null)
                {
                    foreach (var sId in entry.SubagentIds)
                    {
                        subagentIds.Add(sId);
                    }
                }
            }
            SaveTranscriptCache();
        }

        if (knownTopLevelIds.Count > 0)
        {
            subagentIds.ExceptWith(knownTopLevelIds);
        }

        return subagentIds;
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
}
