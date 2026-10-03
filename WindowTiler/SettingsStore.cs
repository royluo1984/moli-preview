using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MoliWindowTiler
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Name = "layoutMode", Order = 1)] public int LayoutMode;
        [DataMember(Name = "customRows", Order = 2)] public string CustomRows;
        [DataMember(Name = "alignment", Order = 3)] public int Alignment;
        [DataMember(Name = "gap", Order = 4)] public int Gap;
        [DataMember(Name = "margin", Order = 5)] public int Margin;
        [DataMember(Name = "monitor", Order = 6)] public string Monitor;
        [DataMember(Name = "showSwitcher", Order = 7)] public bool ShowSwitcher = true;
        [DataMember(Name = "hasSelection", Order = 8)] public bool HasSelection;
        [DataMember(Name = "selectedCharacters", Order = 9)] public List<string> SelectedCharacters;
        [DataMember(Name = "clientOrder", Order = 10)] public List<string> ClientOrder;
        [DataMember(Name = "clientHotkeys", Order = 11)] public List<ClientHotkeyBinding> ClientHotkeys;
        [DataMember(Name = "minimizeToTray", Order = 12)] public bool MinimizeToTray = true;
    }

    internal sealed class SettingsStore
    {
        public string FilePath { get; private set; }

        public SettingsStore()
        {
            FilePath = FindFilePath();
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return Defaults();
                string json = File.ReadAllText(FilePath);
                bool hasTraySetting = json.IndexOf("\"minimizeToTray\"", StringComparison.OrdinalIgnoreCase) >= 0;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                {
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null) return Defaults();
                    if (!hasTraySetting) settings.MinimizeToTray = true;
                    return settings;
                }
            }
            catch
            {
                return Defaults();
            }
        }

        private static AppSettings Defaults()
        {
            return new AppSettings
            {
                LayoutMode = 0,
                CustomRows = "3,3",
                Alignment = 4,
                Gap = 0,
                Margin = 8,
                Monitor = "",
                ShowSwitcher = true,
                HasSelection = false,
                SelectedCharacters = new List<string>(),
                ClientOrder = new List<string>(),
                ClientHotkeys = new List<ClientHotkeyBinding>(),
                MinimizeToTray = true
            };
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) return;
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                string temporary = FilePath + ".tmp";
                using (FileStream stream = File.Create(temporary)) serializer.WriteObject(stream, settings);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch
            {
                // Settings are optional; a read-only folder should not affect arranging.
            }
        }

        private static string FindFilePath()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (string.Equals(directory.Name, "moli-preview", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(directory.FullName, "moli-settings.json");
                directory = directory.Parent;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "moli-settings.json");
        }
    }
}
