using System;
using System.Runtime.InteropServices;
using System.Text;

namespace YandexSolver
{
    internal static class NativeSolverBridge
    {
        private const string DllName = "SolverCore.dll";
        private const int BufferSize = 262144;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ys_http_get(
            byte[] url,
            byte[] outBuf,
            int outBufSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ys_solve_task_ex(
            byte[] provider,
            byte[] baseUrl,
            byte[] apiKey,
            byte[] model,
            byte[] systemPromptUtf8,
            byte[] promptUtf8,
            double temperature,
            byte[] outBuf,
            int outBufSize);

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
            double temperature)
        {
            string nativeProvider = providerUiName == "Google Gemini" ? "gemini" : "openai";
            byte[] outBuf = new byte[BufferSize];
            int rc = ys_solve_task_ex(
                ToUtf8Z(nativeProvider),
                ToUtf8Z(baseUrl),
                ToUtf8Z(apiKey),
                ToUtf8Z(model),
                ToUtf8Z(systemPrompt),
                ToUtf8Z(prompt),
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
