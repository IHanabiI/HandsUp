using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using MegaCrit.Sts2.Core.Saves;

namespace HandsUp.HandsUpCode.Services;

public static class RaiseHandHotkeySettingsService
{
    private const string SettingsFileName = "handsup_hotkey_settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static int _loadedProfileId = -1;
    private static RaiseHandHotkeySettingsData _settings = CreateDefaultSettings();

    public static event Action? SettingsChanged;

    public static bool ShouldShowShortcutConfirmPopup()
    {
        EnsureLoaded();
        return _settings.ShowShortcutConfirmPopup;
    }

    public static void SetShowShortcutConfirmPopup(bool value)
    {
        EnsureLoaded();
        if (_settings.ShowShortcutConfirmPopup == value)
            return;

        _settings.ShowShortcutConfirmPopup = value;
        Save();
        NotifySettingsChanged(refreshInputMap: false);
    }

    public static Key GetBinding(RaiseHandActionKind actionKind)
    {
        EnsureLoaded();
        return GetBindingCore(actionKind);
    }

    public static void SetBinding(RaiseHandActionKind actionKind, Key key)
    {
        EnsureLoaded();
        if (IsModifierOnlyKey(key))
            return;

        if (key == Key.None)
        {
            ClearBinding(actionKind);
            return;
        }

        var existingOwner = RaiseHandActionService.OrderedActionKinds
            .FirstOrDefault(kind => kind != actionKind && GetBindingCore(kind) == key);

        if (existingOwner != default)
        {
            var currentKey = GetBindingCore(actionKind);
            SetBindingCore(existingOwner, currentKey);
        }

        SetBindingCore(actionKind, key);
        Save();
        NotifySettingsChanged();
    }

    public static void ClearBinding(RaiseHandActionKind actionKind)
    {
        EnsureLoaded();
        SetBindingCore(actionKind, Key.None);
        Save();
        NotifySettingsChanged();
    }

    public static void ResetToDefaults()
    {
        EnsureLoaded();
        _settings = CreateDefaultSettings();
        Save();
        NotifySettingsChanged();
    }

    public static bool TryGetActionForKey(Key key, out RaiseHandActionKind actionKind)
    {
        EnsureLoaded();
        foreach (var candidate in RaiseHandActionService.OrderedActionKinds)
        {
            if (GetBindingCore(candidate) != key)
                continue;

            actionKind = candidate;
            return true;
        }

        actionKind = default;
        return false;
    }

    public static string GetBindingDisplayText(RaiseHandActionKind actionKind)
    {
        var key = GetBinding(actionKind);
        return key == Key.None ? "\u672a\u7ed1\u5b9a" : key.ToString();
    }

    public static IReadOnlyDictionary<RaiseHandActionKind, Key> GetBindingsSnapshot()
    {
        EnsureLoaded();

        var bindings = new Dictionary<RaiseHandActionKind, Key>();
        foreach (var actionKind in RaiseHandActionService.OrderedActionKinds)
        {
            bindings[actionKind] = GetBindingCore(actionKind);
        }

        return bindings;
    }

    private static void EnsureLoaded()
    {
        var profileId = GetActiveProfileId();
        if (_loadedProfileId == profileId)
            return;

        _loadedProfileId = profileId;
        _settings = Load(profileId);
    }

    private static RaiseHandHotkeySettingsData Load(int profileId)
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(GetSettingsPath(profileId));
            if (!File.Exists(path))
            {
                var defaults = CreateDefaultSettings();
                WriteToPath(path, defaults);
                return defaults;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<RaiseHandHotkeySettingsData>(json, JsonOptions);
            if (parsed == null)
                return CreateDefaultSettings();

            NormalizeBindings(parsed);
            return parsed;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to load HandsUp hotkey settings: {e}");
            return CreateDefaultSettings();
        }
    }

    private static void Save()
    {
        try
        {
            WriteToPath(ProjectSettings.GlobalizePath(GetSettingsPath(_loadedProfileId)), _settings);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to save HandsUp hotkey settings: {e}");
        }
    }

    private static void NotifySettingsChanged(bool refreshInputMap = true)
    {
        if (refreshInputMap)
        {
            RaiseHandHotkeyInputMapService.RefreshBindings();
            RaiseHandHotkeyRuntimeService.EnsureRegistered();
        }

        SettingsChanged?.Invoke();
    }

    private static void WriteToPath(string path, RaiseHandHotkeySettingsData settings)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static string GetSettingsPath(int profileId)
    {
        return UserDataPathProvider.GetProfileScopedPath(profileId, SettingsFileName);
    }

    private static int GetActiveProfileId()
    {
        try
        {
            var profileId = SaveManager.Instance?.CurrentProfileId ?? 1;
            return profileId > 0 ? profileId : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static RaiseHandHotkeySettingsData CreateDefaultSettings()
    {
        return new RaiseHandHotkeySettingsData
        {
            ShowShortcutConfirmPopup = true,
            KeyboardBindings = new Dictionary<string, string>
            {
                [RaiseHandActionKind.Restart.ToString()] = Key.R.ToString(),
                [RaiseHandActionKind.SoftRestart.ToString()] = Key.L.ToString(),
                [RaiseHandActionKind.PreviousStep.ToString()] = Key.Z.ToString(),
                [RaiseHandActionKind.PreviousFloor.ToString()] = Key.F.ToString()
            }
        };
    }

    private static void NormalizeBindings(RaiseHandHotkeySettingsData settings)
    {
        settings.KeyboardBindings ??= new Dictionary<string, string>();

        foreach (var actionKind in RaiseHandActionService.OrderedActionKinds)
        {
            var key = actionKind.ToString();
            if (!settings.KeyboardBindings.ContainsKey(key))
                settings.KeyboardBindings[key] = Key.None.ToString();
        }
    }

    private static Key GetBindingCore(RaiseHandActionKind actionKind)
    {
        if (_settings.KeyboardBindings.TryGetValue(actionKind.ToString(), out var keyText)
            && Enum.TryParse<Key>(keyText, out var parsed))
        {
            return parsed;
        }

        return Key.None;
    }

    private static void SetBindingCore(RaiseHandActionKind actionKind, Key key)
    {
        _settings.KeyboardBindings[actionKind.ToString()] = key.ToString();
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;
    }

    private sealed class RaiseHandHotkeySettingsData
    {
        [JsonPropertyName("show_shortcut_confirm_popup")]
        public bool ShowShortcutConfirmPopup { get; set; } = true;

        [JsonPropertyName("keyboard_bindings")]
        public Dictionary<string, string> KeyboardBindings { get; set; } = new();
    }
}
