using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace YandexSolver
{
    internal static class NativeSolverBridge
    {
        private const string DllName = "SolverCore.dll";
        private const int BufferSize = 262144;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ys_http_get(
            byte[] url,
            byte[] outBuf,
            int outBufSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ys_solve_task_vision(
            byte[] provider,
            byte[] baseUrl,
            byte[] apiKey,
            byte[] model,
            byte[] systemPromptUtf8,
            byte[] promptUtf8,
            byte[] imageBase64Utf8,
            double temperature,
            byte[] outBuf,
            int outBufSize);

        static NativeSolverBridge()
        {
            EnsureNativeLibraryLoaded();
        }

        private static void EnsureNativeLibraryLoaded()
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName);
            if (File.Exists(localPath))
            {
                LoadLibrary(localPath);
                return;
            }

            string extractDir = Path.Combine(Path.GetTempPath(), "EduToolsNative");
            Directory.CreateDirectory(extractDir);
            string extractedPath = Path.Combine(extractDir, DllName);

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DllName))
            {
                if (stream == null)
                {
                    return;
                }

                bool needsWrite = !File.Exists(extractedPath);
                if (!needsWrite)
                {
                    var fi = new FileInfo(extractedPath);
                    if (fi.Length != stream.Length)
                    {
                        needsWrite = true;
                    }
                }

                if (needsWrite)
                {
                    try
                    {
                        using (var fs = new FileStream(extractedPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            stream.CopyTo(fs);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            if (File.Exists(extractedPath))
            {
                LoadLibrary(extractedPath);
            }
        }

        private static byte[] ToUtf8Z(string value)
        {
            byte[] raw = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] z = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, z, 0, raw.Length);
            return z;
        }

        private static string FromUtf8Z(byte[] buf)
        {
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0)
            {
                len = buf.Length;
            }
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        public static string HttpGet(string url)
        {
            byte[] outBuf = new byte[BufferSize];
            int rc = ys_http_get(ToUtf8Z(url), outBuf, outBuf.Length);
            string text = FromUtf8Z(outBuf);
            if (rc != 0)
            {
                throw new InvalidOperationException(text);
            }
            return text;
        }

        public static string SolveTask(
            string providerUiName,
            string baseUrl,
            string apiKey,
            string model,
            string systemPrompt,
            string prompt,
            double temperature,
            string imageBase64 = null)
        {
            string nativeProvider = providerUiName == "Google Gemini" ? "gemini" : "openai";
            byte[] outBuf = new byte[BufferSize];
            int rc = ys_solve_task_vision(
                ToUtf8Z(nativeProvider),
                ToUtf8Z(baseUrl),
                ToUtf8Z(apiKey),
                ToUtf8Z(model),
                ToUtf8Z(systemPrompt),
                ToUtf8Z(prompt),
                ToUtf8Z(imageBase64 ?? string.Empty),
                temperature,
                outBuf,
                outBuf.Length);
            string text = FromUtf8Z(outBuf);
            if (rc != 0)
            {
                throw new InvalidOperationException(text);
            }
            return text;
        }
    }
}
