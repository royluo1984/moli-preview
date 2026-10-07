using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MoliWindowTiler
{
    [DataContract]
    internal sealed class TargetProfileState
    {
        [DataMember(Name = "profileId", Order = 1)] public string ProfileId;
        [DataMember(Name = "hasSelection", Order = 2)] public bool HasSelection;
        [DataMember(Name = "selectedCharacters", Order = 3)] public List<string> SelectedCharacters;
        [DataMember(Name = "clientOrder", Order = 4)] public List<string> ClientOrder;
        [DataMember(Name = "clientHotkeys", Order = 5)] public List<ClientHotkeyBinding> ClientHotkeys;

        internal TargetProfileState Clone()
        {
            return new TargetProfileState
            {
                ProfileId = ProfileId,
                HasSelection = HasSelection,
                SelectedCharacters = SelectedCharacters == null ? new List<string>() : new List<string>(SelectedCharacters),
                ClientOrder = ClientOrder == null ? new List<string>() : new List<string>(ClientOrder),
                ClientHotkeys = ClientHotkeys == null
                    ? new List<ClientHotkeyBinding>()
                    : ClientHotkeys.Where(binding => binding != null).Select(binding => binding.Clone()).ToList()
            };
        }
    }

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
        [DataMember(Name = "minimizeAllModifiers", Order = 13)] public uint MinimizeAllModifiers = HotkeyDefaults.MinimizeAllModifiers;
        [DataMember(Name = "minimizeAllKey", Order = 14)] public uint MinimizeAllKey = HotkeyDefaults.MinimizeAllKey;
        [DataMember(Name = "targetProfiles", Order = 15)] public List<TargetProfile> TargetProfiles;
        [DataMember(Name = "activeTargetProfileId", Order = 16)] public string ActiveTargetProfileId;
        [DataMember(Name = "profileStates", Order = 17)] public List<TargetProfileState> ProfileStates;
        [DataMember(Name = "cycleClientsModifiers", Order = 18)] public uint CycleClientsModifiers = HotkeyDefaults.CycleClientsModifiers;
        [DataMember(Name = "cycleClientsKey", Order = 19)] public uint CycleClientsKey = HotkeyDefaults.CycleClientsKey;
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
                bool hasMinimizeAllModifiers = json.IndexOf("\"minimizeAllModifiers\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasMinimizeAllKey = json.IndexOf("\"minimizeAllKey\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasCycleClientsModifiers = json.IndexOf("\"cycleClientsModifiers\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasCycleClientsKey = json.IndexOf("\"cycleClientsKey\"", StringComparison.OrdinalIgnoreCase) >= 0;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                {
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null) return Defaults();
                    if (!hasTraySetting) settings.MinimizeToTray = true;
                    if (!hasMinimizeAllModifiers) settings.MinimizeAllModifiers = HotkeyDefaults.MinimizeAllModifiers;
                    if (!hasMinimizeAllKey) settings.MinimizeAllKey = HotkeyDefaults.MinimizeAllKey;
                    if (!hasCycleClientsModifiers) settings.CycleClientsModifiers = HotkeyDefaults.CycleClientsModifiers;
                    if (!hasCycleClientsKey) settings.CycleClientsKey = HotkeyDefaults.CycleClientsKey;
                    EnsureTargetProfiles(settings);
                    EnsureProfileStates(settings);
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
                MinimizeToTray = true,
                MinimizeAllModifiers = HotkeyDefaults.MinimizeAllModifiers,
                MinimizeAllKey = HotkeyDefaults.MinimizeAllKey,
                CycleClientsModifiers = HotkeyDefaults.CycleClientsModifiers,
                CycleClientsKey = HotkeyDefaults.CycleClientsKey,
                TargetProfiles = new List<TargetProfile> { TargetProfile.CreateMoliDefault() },
                ActiveTargetProfileId = TargetProfile.MoliDefaultId,
                ProfileStates = new List<TargetProfileState>
                {
                    new TargetProfileState
                    {
                        ProfileId = TargetProfile.MoliDefaultId,
                        SelectedCharacters = new List<string>(),
                        ClientOrder = new List<string>(),
                        ClientHotkeys = new List<ClientHotkeyBinding>()
                    }
                }
            };
        }

        internal static void EnsureTargetProfiles(AppSettings settings)
        {
            if (settings == null) return;
            List<TargetProfile> profiles = new List<TargetProfile>();
            if (settings.TargetProfiles != null)
            {
                foreach (TargetProfile profile in settings.TargetProfiles)
                {
                    if (profile == null) continue;
                    if (string.IsNullOrWhiteSpace(profile.Id))
                        profile.Id = Guid.NewGuid().ToString("N");
                    if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = profile.Id;
                    if (!profiles.Any(existing => string.Equals(existing.Id, profile.Id, StringComparison.OrdinalIgnoreCase)))
                        profiles.Add(profile);
                }
            }
            if (!profiles.Any(profile => string.Equals(profile.Id, TargetProfile.MoliDefaultId, StringComparison.OrdinalIgnoreCase)))
                profiles.Insert(0, TargetProfile.CreateMoliDefault());
            settings.TargetProfiles = profiles;
            if (!profiles.Any(profile => string.Equals(profile.Id, settings.ActiveTargetProfileId, StringComparison.OrdinalIgnoreCase)))
                settings.ActiveTargetProfileId = TargetProfile.MoliDefaultId;
        }

        internal static void EnsureProfileStates(AppSettings settings)
        {
            if (settings == null) return;
            EnsureTargetProfiles(settings);
            List<TargetProfileState> states = new List<TargetProfileState>();
            if (settings.ProfileStates != null)
            {
                foreach (TargetProfileState state in settings.ProfileStates)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.ProfileId)) continue;
                    TargetProfile profile = settings.TargetProfiles.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, state.ProfileId, StringComparison.OrdinalIgnoreCase));
                    if (profile == null || states.Any(existing =>
                        string.Equals(existing.ProfileId, state.ProfileId, StringComparison.OrdinalIgnoreCase))) continue;
                    state.SelectedCharacters = state.SelectedCharacters ?? new List<string>();
                    state.ClientOrder = state.ClientOrder ?? new List<string>();
                    state.ClientHotkeys = state.ClientHotkeys ?? new List<ClientHotkeyBinding>();
                    states.Add(state);
                }
            }
            if (!states.Any(state => string.Equals(state.ProfileId, TargetProfile.MoliDefaultId, StringComparison.OrdinalIgnoreCase)))
            {
                states.Insert(0, new TargetProfileState
                {
                    ProfileId = TargetProfile.MoliDefaultId,
                    HasSelection = settings.HasSelection,
                    SelectedCharacters = settings.SelectedCharacters == null
                        ? new List<string>() : new List<string>(settings.SelectedCharacters),
                    ClientOrder = settings.ClientOrder == null
                        ? new List<string>() : new List<string>(settings.ClientOrder),
                    ClientHotkeys = settings.ClientHotkeys == null
                        ? new List<ClientHotkeyBinding>()
                        : settings.ClientHotkeys.Where(binding => binding != null).Select(binding => binding.Clone()).ToList()
                });
            }
            settings.ProfileStates = states;
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
