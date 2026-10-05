using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    [DataContract]
    public sealed class SavedWindowPosition
    {
        [DataMember(Name = "character", Order = 1)] public string Character;
        [DataMember(Name = "left", Order = 2)] public int Left;
        [DataMember(Name = "top", Order = 3)] public int Top;
        [DataMember(Name = "width", Order = 4)] public int Width;
        [DataMember(Name = "height", Order = 5)] public int Height;
        [DataMember(Name = "screen", Order = 6)] public string Screen;
        [DataMember(Name = "thread", Order = 7)] public uint ThreadId;
        [DataMember(Name = "updated", Order = 8)] public string Updated;
        [DataMember(Name = "aliases", Order = 9)] public List<string> Aliases;
        [DataMember(Name = "pid", Order = 10)] public int ProcessId;
        [DataMember(Name = "started", Order = 11)] public long Started;
        [DataMember(Name = "threadDescription", Order = 12)] public string ThreadDescription;
        [DataMember(Name = "title", Order = 13)] public string Title;
        [DataMember(Name = "profileId", Order = 14)] public string ProfileId;

        public Rectangle Bounds { get { return new Rectangle(Left, Top, Width, Height); } }
    }

    [DataContract]
    internal sealed class PositionDocument
    {
        [DataMember(Name = "positions", Order = 1)]
        public List<SavedWindowPosition> Positions;
    }

    public sealed class PositionStore
    {
        private readonly Dictionary<string, SavedWindowPosition> positions =
            new Dictionary<string, SavedWindowPosition>(StringComparer.OrdinalIgnoreCase);

        public string FilePath { get; private set; }

        public PositionStore()
        {
            FilePath = FindFilePath();
            Load();
        }

        public SavedWindowPosition Get(string character)
        {
            string key = NameKey(character);
            if (key == null) return null;
            SavedWindowPosition value;
            return positions.TryGetValue(ScopedKey(TargetProfile.MoliDefaultId, key), out value) ? value : null;
        }

        public SavedWindowPosition Get(GameWindow game)
        {
            if (game == null) return null;
            foreach (string key in CandidateKeys(game))
            {
                SavedWindowPosition value;
                if (positions.TryGetValue(key, out value)) return value;
            }
            return null;
        }

        public void Set(GameWindow game, Rectangle bounds)
        {
            if (game == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            List<string> keys = CandidateKeys(game).ToList();
            if (keys.Count == 0)
                return;
            SavedWindowPosition record = Get(game) ?? new SavedWindowPosition();
            string identity = IdentityFor(game);
            Screen screen = null;
            try { screen = Screen.FromHandle(game.Handle); }
            catch { }
            record.Character = identity;
            record.ProfileId = ProfileIdFor(game);
            record.Left = bounds.Left;
            record.Top = bounds.Top;
            record.Width = bounds.Width;
            record.Height = bounds.Height;
            record.Screen = screen == null ? "" : screen.DeviceName;
            record.ThreadId = game.ThreadId;
            record.ProcessId = game.Pid;
            record.Started = game.Started;
            record.ThreadDescription = game.ThreadDescription ?? "";
            record.Title = game.Title ?? "";
            record.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<string> aliases = record.Aliases == null
                ? new List<string>()
                : new List<string>(record.Aliases);
            foreach (string key in keys)
            {
                string alias = AliasFromKey(key);
                if (!string.IsNullOrWhiteSpace(alias) &&
                    !aliases.Any(existing => string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase)))
                    aliases.Add(alias);
                positions[key] = record;
            }
            record.Aliases = aliases;
            string characterKey = NameKey(record.Character);
            if (characterKey != null) positions[ScopedKey(record.ProfileId, characterKey)] = record;
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                PositionDocument document = new PositionDocument
                {
                    Positions = positions.Values.Distinct().OrderBy(p => p.Character, StringComparer.OrdinalIgnoreCase).ToList()
                };
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PositionDocument));
                string temporary = FilePath + ".tmp";
                using (FileStream stream = File.Create(temporary)) serializer.WriteObject(stream, document);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch
            {
                // The layout operation should still work if the folder is read-only.
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PositionDocument));
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    PositionDocument document = serializer.ReadObject(stream) as PositionDocument;
                    if (document == null || document.Positions == null) return;
                    foreach (SavedWindowPosition position in document.Positions)
                    {
                        if (position != null && !string.IsNullOrWhiteSpace(position.Character) &&
                            position.Width > 0 && position.Height > 0)
                        {
                            if (string.IsNullOrWhiteSpace(position.ProfileId))
                                position.ProfileId = TargetProfile.MoliDefaultId;
                            Index(position);
                        }
                    }
                }
            }
            catch
            {
                // An incomplete file should not stop the window manager from opening.
            }
        }

        private static string FindFilePath()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (string.Equals(directory.Name, "moli-preview", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(directory.FullName, "character-positions.json");
                directory = directory.Parent;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character-positions.json");
        }

        public static string IdentityFor(GameWindow game)
        {
            if (game == null) return "";
            foreach (string candidate in Native.CharacterCandidates(game))
            {
                if (!IsAnonymous(candidate)) return candidate.Trim();
            }
            return game.ThreadId == 0 ? "" : "线程" + game.ThreadId;
        }

        public static bool MatchesIdentity(string saved, GameWindow game)
        {
            if (string.IsNullOrWhiteSpace(saved) || game == null) return false;
            string expected = Normalize(saved);
            foreach (string candidate in Native.CharacterCandidates(game))
            {
                if (!IsAnonymous(candidate) &&
                    string.Equals(expected, Normalize(candidate), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return string.Equals(expected, IdentityFor(game), StringComparison.OrdinalIgnoreCase);
        }

        private void Index(SavedWindowPosition position)
        {
            string profileId = ProfileIdFor(position);
            AddIndex(ScopedKey(profileId, NameKey(position.Character)), position);
            if (position.Aliases != null)
            {
                foreach (string alias in position.Aliases)
                    AddIndex(ScopedKey(profileId, NameKey(alias)), position);
            }
            if (position.ThreadId != 0) AddIndex(ScopedKey(profileId, ThreadKey(position.ThreadId)), position);
            if (position.ProcessId != 0 && position.Started != 0)
                AddIndex(ScopedKey(profileId, ProcessKey(position.ProcessId, position.Started)), position);
        }

        private void AddIndex(string key, SavedWindowPosition position)
        {
            if (string.IsNullOrWhiteSpace(key) || position == null) return;
            SavedWindowPosition existing;
            if (!positions.TryGetValue(key, out existing) || IsNewer(position, existing))
                positions[key] = position;
        }

        private static bool IsNewer(SavedWindowPosition candidate, SavedWindowPosition existing)
        {
            if (existing == null) return true;
            return string.Compare(candidate.Updated ?? "", existing.Updated ?? "", StringComparison.Ordinal) >= 0;
        }

        private static IEnumerable<string> CandidateKeys(GameWindow game)
        {
            List<string> keys = new List<string>();
            string profileId = ProfileIdFor(game);
            foreach (string candidate in Native.CharacterCandidates(game))
            {
                if (IsAnonymous(candidate)) continue;
                string key = NameKey(candidate);
                key = ScopedKey(profileId, key);
                if (key != null && !keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))) keys.Add(key);
            }
            string threadKey = ThreadKey(game.ThreadId);
            if (threadKey != null) keys.Add(ScopedKey(profileId, threadKey));
            string processKey = ProcessKey(game.Pid, game.Started);
            if (processKey != null) keys.Add(ScopedKey(profileId, processKey));
            return keys;
        }

        private static string AliasFromKey(string key)
        {
            int separator = key == null ? -1 : key.IndexOf('|');
            if (separator >= 0) key = key.Substring(separator + 1);
            if (key == null || !key.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) return null;
            return key.Substring(5);
        }

        private static string ProfileIdFor(GameWindow game)
        {
            return game == null || string.IsNullOrWhiteSpace(game.TargetProfileId)
                ? TargetProfile.MoliDefaultId : game.TargetProfileId.Trim();
        }

        private static string ProfileIdFor(SavedWindowPosition position)
        {
            return position == null || string.IsNullOrWhiteSpace(position.ProfileId)
                ? TargetProfile.MoliDefaultId : position.ProfileId.Trim();
        }

        private static string ScopedKey(string profileId, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return "profile:" + (string.IsNullOrWhiteSpace(profileId) ? TargetProfile.MoliDefaultId : profileId.Trim()) + "|" + key;
        }

        private static string NameKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return "name:" + Normalize(value);
        }

        private static string ThreadKey(uint threadId)
        {
            return threadId == 0 ? null : "thread:" + threadId;
        }

        private static string ProcessKey(int pid, long started)
        {
            return pid == 0 || started == 0 ? null : "process:" + pid + ":" + started;
        }

        private static bool IsAnonymous(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string normalized = Normalize(value);
            return normalized.StartsWith("未命名-线程", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("线程", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "reincarnation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "cg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "主线程", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().Trim('-', ' ', '\t', '[', ']').ToLowerInvariant();
        }
    }
}
