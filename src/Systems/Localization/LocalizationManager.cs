using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CorditeWars.Systems.Localization;

/// <summary>
/// Manages game translations via Godot's TranslationServer.
/// Loads JSON translation files from data/locale/ and registers them
/// programmatically. Provides static helpers for UI code.
///
/// Supported languages are defined in <see cref="SupportedLocales"/>.
/// Audio stays in English; only UI text and subtitles are translated.
/// </summary>
public partial class LocalizationManager : Node
{
    private const string LocaleDir = "res://data/locale";
    private const string SettingsPath = "user://settings.cfg";
    private const string SettingsSection = "localization";
    private const string SettingsKeyLanguage = "language";
    private const string SettingsKeySubtitles = "subtitles_enabled";

    /// <summary>Singleton set by Godot autoload.</summary>
    public static LocalizationManager? Instance { get; private set; }

    /// <summary>Whether subtitles are enabled for voice lines.</summary>
    public bool SubtitlesEnabled { get; private set; } = true;

    /// <summary>
    /// All supported locale codes and their display names.
    /// Order matches the language selector dropdown.
    /// </summary>
    public static readonly (string Code, string DisplayName)[] SupportedLocales =
    {
        ("en",    "English"),
        ("fr",    "Français"),
        ("de",    "Deutsch"),
        ("es",    "Español"),
        ("it",    "Italiano"),
        ("pt_BR", "Português (BR)"),
        ("ru",    "Русский"),
        ("zh_CN", "简体中文"),
        ("zh_TW", "繁體中文"),
        ("ja",    "日本語"),
        ("ko",    "한국어"),
        ("pl",    "Polski"),
        ("tr",    "Türkçe"),
        ("nl",    "Nederlands"),
        ("sv",    "Svenska"),
        ("nb",    "Norsk"),
        ("da",    "Dansk"),
        ("fi",    "Suomi"),
        ("cs",    "Čeština"),
        ("hu",    "Magyar"),
        ("ro",    "Română"),
        ("th",    "ไทย"),
        ("vi",    "Tiếng Việt"),
        ("ar",    "العربية"),
        ("uk",    "Українська"),
    };

    /// <summary>Fired when the language changes so UI can refresh.</summary>
    [Signal]
    public delegate void LanguageChangedEventHandler(string localeCode);

    private readonly Dictionary<string, Translation> _translations = new();

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[LocalizationManager] Initialized.");
    }

    /// <summary>
    /// Loads all translation files from the locale directory and
    /// registers them with Godot's TranslationServer.
    /// Call during the loading screen sequence.
    /// </summary>
    public void LoadAllTranslations()
    {
        int count = 0;

        foreach (var (code, _) in SupportedLocales)
        {
            string path = $"{LocaleDir}/{code}.json";

            try
            {
                string json = ReadFile(path);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json,
                    new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                if (entries == null)
                {
                    GD.PrintErr($"[LocalizationManager] Empty translation file: {path}");
                    continue;
                }

                var translation = new Translation();
                translation.Locale = code;

                foreach (var (key, value) in entries)
                {
                    translation.AddMessage(key, value);
                }

                TranslationServer.AddTranslation(translation);
                _translations[code] = translation;
                count++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LocalizationManager] Failed to load {path}: {ex.Message}");
            }
        }

        GD.Print($"[LocalizationManager] Loaded {count}/{SupportedLocales.Length} translation files.");

        // Apply saved language preference
        LoadSettings();
    }

    /// <summary>
    /// Sets the active locale and notifies listeners.
    /// Only accepts codes listed in <see cref="SupportedLocales"/>; falls back to English
    /// for unrecognised codes.
    /// </summary>
    /// <param name="localeCode">A locale code from <see cref="SupportedLocales"/>.</param>
    public void SetLanguage(string localeCode)
    {
        if (!IsLocaleSupported(localeCode))
        {
            GD.PushWarning($"[LocalizationManager] Unsupported locale '{localeCode}', falling back to 'en'.");
            localeCode = "en";
        }
        TranslationServer.SetLocale(localeCode);
        GD.Print($"[LocalizationManager] Language set to: {localeCode}");
        EmitSignal(SignalName.LanguageChanged, localeCode);
    }

    /// <summary>
    /// Returns the current locale code.
    /// </summary>
    public string GetCurrentLocale()
    {
        return TranslationServer.GetLocale();
    }

    /// <summary>
    /// Sets the subtitle enabled state.
    /// </summary>
    public void SetSubtitlesEnabled(bool enabled)
    {
        SubtitlesEnabled = enabled;
    }

    /// <summary>
    /// Saves the current language and subtitle preferences to the config file.
    /// </summary>
    public void SaveSettings()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath); // Load existing (may fail silently if new)
        config.SetValue(SettingsSection, SettingsKeyLanguage, GetCurrentLocale());
        config.SetValue(SettingsSection, SettingsKeySubtitles, SubtitlesEnabled);
        config.Save(SettingsPath);
    }

    /// <summary>
    /// Loads language and subtitle preferences from the config file.
    /// Falls back to system locale or English.
    /// </summary>
    private void LoadSettings()
    {
        var config = new ConfigFile();
        Error err = config.Load(SettingsPath);

        string locale;
        if (err == Error.Ok && config.HasSectionKey(SettingsSection, SettingsKeyLanguage))
        {
            locale = config.GetValue(SettingsSection, SettingsKeyLanguage).AsString();
        }
        else
        {
            // Try to match system locale
            locale = TranslationServer.GetLocale();
            if (!IsLocaleSupported(locale))
            {
                // Try base language (e.g. "pt_BR" from "pt")
                string baseLang = locale.Contains('_') ? locale.Split('_')[0] : locale;
                locale = FindClosestLocale(baseLang);
            }
        }

        if (err == Error.Ok && config.HasSectionKey(SettingsSection, SettingsKeySubtitles))
        {
            SubtitlesEnabled = config.GetValue(SettingsSection, SettingsKeySubtitles).AsBool();
        }

        SetLanguage(locale);
    }

    /// <summary>
    /// Checks if a locale code is in our supported list.
    /// </summary>
    public static bool IsLocaleSupported(string locale)
    {
        foreach (var (code, _) in SupportedLocales)
        {
            if (string.Equals(code, locale, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the closest supported locale for a base language code.
    /// Returns "en" if no match is found.
    /// </summary>
    private static string FindClosestLocale(string baseLang)
    {
        foreach (var (code, _) in SupportedLocales)
        {
            if (code.StartsWith(baseLang, StringComparison.OrdinalIgnoreCase))
                return code;
        }
        return "en";
    }

    /// <summary>
    /// Returns the index of a locale in <see cref="SupportedLocales"/>, or 0 (English) if not found.
    /// </summary>
    public static int GetLocaleIndex(string localeCode)
    {
        for (int i = 0; i < SupportedLocales.Length; i++)
        {
            if (string.Equals(SupportedLocales[i].Code, localeCode, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Static translation helper that calls TranslationServer.Translate.
    /// Falls back to the raw key when no translation is found.
    /// Convenience for non-Node classes.
    /// </summary>
    public static string Translate(string key)
    {
        return TranslationServer.Translate(key) ?? key;
    }

    /// <summary>
    /// Static translation helper with string.Format support.
    /// Falls back to the raw key when no translation is found.
    /// </summary>
    public static string Translate(string key, params object[] args)
    {
        string translated = TranslationServer.Translate(key) ?? key;
        return string.Format(translated, args);
    }

    // ── File reading ────────────────────────────────────────────────

    private static string ReadFile(string path)
    {
        using var fa = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (fa != null)
            return fa.GetAsText();

        if (System.IO.File.Exists(path))
            return System.IO.File.ReadAllText(path);

        throw new InvalidOperationException(
            $"[LocalizationManager] Cannot open file: '{path}' " +
            $"(Godot error: {FileAccess.GetOpenError()})");
    }
}
