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
            if (string.IsNullOrWhiteSpace(character)) return null;
            SavedWindowPosition value;
            return positions.TryGetValue(character.Trim(), out value) ? value : null;
        }

        public void Set(GameWindow game, Rectangle bounds)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.CharacterName) || bounds.Width <= 0 || bounds.Height <= 0)
                return;
            Screen screen = null;
            try { screen = Screen.FromHandle(game.Handle); }
            catch { }
            positions[game.CharacterName.Trim()] = new SavedWindowPosition
            {
                Character = game.CharacterName.Trim(),
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Screen = screen == null ? "" : screen.DeviceName,
                ThreadId = game.ThreadId,
                Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                PositionDocument document = new PositionDocument
                {
                    Positions = positions.Values.OrderBy(p => p.Character, StringComparer.OrdinalIgnoreCase).ToList()
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
                            positions[position.Character.Trim()] = position;
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
    }
}
