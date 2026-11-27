using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace YandexSolver
{
    public sealed class AppSettings
    {
        public string Provider { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public int CdpPort { get; set; }
        public double Temperature { get; set; }
        public string CustomSystemPrompt { get; set; }
        public int SubmitDelayMs { get; set; }
        public int StepDelayMs { get; set; }
        public int MaxSteps { get; set; }
        public bool AutoAdvance { get; set; }
        public bool SkipTheory { get; set; }
        public bool RetryOnWrong { get; set; }
        public string Theme { get; set; }
        public string Accent { get; set; }
        public string CornerStyle { get; set; }
        public double WindowOpacity { get; set; }
        public bool PortableMode { get; set; }
        public bool BossHotkeyEnabled { get; set; }
        public string SchoolCode { get; set; }
        public string StudentLogin { get; set; }

        public AppSettings()
        {
            Provider = "OpenRouter";
            BaseUrl = "https://openrouter.ai/api/v1";
            ApiKey = string.Empty;
            Model = "google/gemini-2.0-flash-exp:free";
            CdpPort = 9222;
            Temperature = 0.1;
            CustomSystemPrompt = string.Empty;
            SubmitDelayMs = 700;
            StepDelayMs = 1500;
            MaxSteps = 30;
            AutoAdvance = true;
            SkipTheory = true;
            RetryOnWrong = true;
            Theme = "Dark (Slate)";
            Accent = "Синий";
            CornerStyle = "Стандарт";
            WindowOpacity = 1.0;
            PortableMode = false;
            BossHotkeyEnabled = true;
            SchoolCode = string.Empty;
            StudentLogin = string.Empty;
        }

        public static string GetPortableSettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        }

        public static string GetLocalSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexUchebnikSolver",
                "settings.ini");
        }

        public string GetActiveProfileDir(string folderName)
        {
            if (PortableMode)
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles", folderName);
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexUchebnikSolver",
                folderName);
        }

        private static string ResolveLoadPath()
        {
            string portable = GetPortableSettingsPath();
            if (File.Exists(portable))
            {
                return portable;
            }
            return GetLocalSettingsPath();
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            string path = ResolveLoadPath();
            if (!File.Exists(path))
            {
                return settings;
            }

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                int intVal;
                double dblVal;
                bool boolVal;

                switch (key)
                {
                    case "provider":
                        settings.Provider = val;
                        break;
                    case "base_url":
                        settings.BaseUrl = val;
                        break;
                    case "api_key":
                        settings.ApiKey = val;
                        break;
                    case "model":
                        settings.Model = val;
                        break;
                    case "cdp_port":
                        if (int.TryParse(val, out intVal) && intVal > 0 && intVal <= 65535)
                            settings.CdpPort = intVal;
                        break;
                    case "temperature":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out dblVal))
                            settings.Temperature = Math.Max(0.0, Math.Min(1.5, dblVal));
                        break;
                    case "system_prompt":
                        settings.CustomSystemPrompt = val.Replace("\\n", "\n");
                        break;
                    case "submit_delay_ms":
                        if (int.TryParse(val, out intVal) && intVal >= 0 && intVal <= 30000)
                            settings.SubmitDelayMs = intVal;
                        break;
                    case "step_delay_ms":
                        if (int.TryParse(val, out intVal) && intVal >= 200 && intVal <= 30000)
                            settings.StepDelayMs = intVal;
                        break;
                    case "max_steps":
                        if (int.TryParse(val, out intVal) && intVal >= 1 && intVal <= 500)
                            settings.MaxSteps = intVal;
                        break;
                    case "auto_advance":
                        if (bool.TryParse(val, out boolVal))
                            settings.AutoAdvance = boolVal;
                        break;
                    case "skip_theory":
                        if (bool.TryParse(val, out boolVal))
                            settings.SkipTheory = boolVal;
                        break;
                    case "retry_on_wrong":
                        if (bool.TryParse(val, out boolVal))
                            settings.RetryOnWrong = boolVal;
                        break;
                    case "theme":
                        settings.Theme = val;
                        break;
                    case "accent":
                        settings.Accent = val;
                        break;
                    case "corner_style":
                        settings.CornerStyle = val;
                        break;
                    case "window_opacity":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out dblVal))
                            settings.WindowOpacity = Math.Max(0.75, Math.Min(1.0, dblVal));
                        break;
                    case "portable_mode":
                        if (bool.TryParse(val, out boolVal))
                            settings.PortableMode = boolVal;
                        break;
                    case "boss_hotkey":
                        if (bool.TryParse(val, out boolVal))
                            settings.BossHotkeyEnabled = boolVal;
                        break;
                    case "school_code":
                        settings.SchoolCode = val;
                        break;
                    case "student_login":
                        settings.StudentLogin = val;
                        break;
                }
            }

            return settings;
        }

        public void Save()
        {
            string path = PortableMode ? GetPortableSettingsPath() : GetLocalSettingsPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sb = new StringBuilder();
            sb.AppendLine("provider=" + (Provider ?? string.Empty));
            sb.AppendLine("base_url=" + (BaseUrl ?? string.Empty));
            sb.AppendLine("api_key=" + (ApiKey ?? string.Empty));
            sb.AppendLine("model=" + (Model ?? string.Empty));
            sb.AppendLine("cdp_port=" + CdpPort);
            sb.AppendLine("temperature=" + Temperature.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("system_prompt=" + (CustomSystemPrompt ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\\n"));
            sb.AppendLine("submit_delay_ms=" + SubmitDelayMs);
            sb.AppendLine("step_delay_ms=" + StepDelayMs);
            sb.AppendLine("max_steps=" + MaxSteps);
            sb.AppendLine("auto_advance=" + AutoAdvance);
            sb.AppendLine("skip_theory=" + SkipTheory);
            sb.AppendLine("retry_on_wrong=" + RetryOnWrong);
            sb.AppendLine("theme=" + (Theme ?? "Dark (Slate)"));
            sb.AppendLine("accent=" + (Accent ?? "Синий"));
            sb.AppendLine("corner_style=" + (CornerStyle ?? "Стандарт"));
            sb.AppendLine("window_opacity=" + WindowOpacity.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("portable_mode=" + PortableMode);
            sb.AppendLine("boss_hotkey=" + BossHotkeyEnabled);
            sb.AppendLine("school_code=" + (SchoolCode ?? string.Empty));
            sb.AppendLine("student_login=" + (StudentLogin ?? string.Empty));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static KeyValuePair<string, string> GetDefaultsForProvider(string provider)
        {
            switch (provider)
            {
                case "Pollinations.ai":
                    return new KeyValuePair<string, string>(
                        "https://gen.pollinations.ai/v1",
                        "openai/gpt-5.4-nano");
                case "Google Gemini":
                    return new KeyValuePair<string, string>(
                        "https://generativelanguage.googleapis.com/v1beta",
                        "gemini-2.5-flash");
                case "OpenRouter":
                    return new KeyValuePair<string, string>(
                        "https://openrouter.ai/api/v1",
                        "google/gemini-2.0-flash-exp:free");
                case "Groq":
                    return new KeyValuePair<string, string>(
                        "https://api.groq.com/openai/v1",
                        "llama-3.3-70b-versatile");
                case "OpenAI":
                    return new KeyValuePair<string, string>(
                        "https://api.openai.com/v1",
                        "gpt-4o-mini");
                case "WusRouter":
                    return new KeyValuePair<string, string>(
                        "https://wusrouter.com/v1",
                        "jailbreak/gpt-6-sol");
                case "Ollama (локально)":
                    return new KeyValuePair<string, string>(
                        "http://localhost:11434/v1",
                        "qwen2.5:7b");
                default:
                    return new KeyValuePair<string, string>(string.Empty, string.Empty);
            }
        }
    }
}
