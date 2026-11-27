using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace YandexSolver
{
    internal static class AnswerCache
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        public static string ComputeTaskHash(PageTaskState state)
        {
            if (state == null) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine(state.QuestionText ?? string.Empty);
            foreach (TaskControl c in state.Controls)
            {
                sb.Append(c.Kind).Append('|').AppendLine(c.Label);
            }
            foreach (DragNode d in state.DragItems)
            {
                sb.Append("D|").AppendLine(d.Label);
            }
            foreach (DragNode t in state.DropTargets)
            {
                sb.Append("T|").AppendLine(t.Label);
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    hex.Append(b.ToString("x2"));
                }
                return hex.ToString();
            }
        }

        public static bool TryGet(string hash, AppSettings settings, out string answerJson)
        {
            answerJson = null;
            if (string.IsNullOrEmpty(hash)) return false;
            Dictionary<string, string> cache = Load(settings);
            return cache.TryGetValue(hash, out answerJson) && !string.IsNullOrEmpty(answerJson);
        }

        public static void Save(string hash, string answerJson, AppSettings settings)
        {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(answerJson)) return;
            try
            {
                Dictionary<string, string> cache = Load(settings);
                cache[hash] = answerJson;
                string path = GetCachePath(settings);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, Json.Serialize(cache), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetCachePath(AppSettings settings)
        {
            if (settings != null && settings.PortableMode)
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "answers_cache.json");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexUchebnikSolver",
                "answers_cache.json");
        }

        private static Dictionary<string, string> Load(AppSettings settings)
        {
            string path = GetCachePath(settings);
            if (!File.Exists(path)) return new Dictionary<string, string>();
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                return Json.Deserialize<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
