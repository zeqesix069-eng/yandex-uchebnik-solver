using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YandexSolver
{
    public static class OcrHelper
    {
        private static readonly Type OcrEngineType = Type.GetType("Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType=WindowsRuntime");
        private static readonly Type LanguageType = Type.GetType("Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime");
        private static readonly Type StreamType = Type.GetType("Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime");
        private static readonly Type DataWriterType = Type.GetType("Windows.Storage.Streams.DataWriter, Windows.Storage.Streams, ContentType=WindowsRuntime");
        private static readonly Type DecoderType = Type.GetType("Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime");
        private static readonly Type PixelFormatType = Type.GetType("Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType=WindowsRuntime");
        private static readonly Type AlphaModeType = Type.GetType("Windows.Graphics.Imaging.BitmapAlphaMode, Windows.Graphics.Imaging, ContentType=WindowsRuntime");

        private static readonly object RuEngine = CreateEngine("ru");
        private static readonly object EnEngine = CreateEngine("en");

        private static object CreateEngine(string langPrefix)
        {
            if (OcrEngineType == null || LanguageType == null)
            {
                return null;
            }
            try
            {
                PropertyInfo langsProp = OcrEngineType.GetProperty("AvailableRecognizerLanguages");
                var langs = langsProp != null ? langsProp.GetValue(null, null) as IEnumerable : null;
                MethodInfo tryCreate = OcrEngineType.GetMethod("TryCreateFromLanguage", new[] { LanguageType });
                if (langs != null && tryCreate != null)
                {
                    foreach (object lang in langs)
                    {
                        PropertyInfo tagProp = lang.GetType().GetProperty("LanguageTag");
                        string tag = tagProp != null ? Convert.ToString(tagProp.GetValue(lang, null)) : string.Empty;
                        if (tag.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return tryCreate.Invoke(null, new[] { lang });
                        }
                    }
                }
                MethodInfo tryUser = OcrEngineType.GetMethod("TryCreateFromUserProfileLanguages");
                return tryUser != null ? tryUser.Invoke(null, null) : null;
            }
            catch
            {
                return null;
            }
        }

        private static object AwaitWinRt(object asyncOp)
        {
            if (asyncOp == null)
            {
                return null;
            }
            Type opType = asyncOp.GetType();
            PropertyInfo statusProp = opType.GetProperty("Status");
            if (statusProp != null)
            {
                int tries = 0;
                while (Convert.ToInt32(statusProp.GetValue(asyncOp, null)) == 0 && tries < 500)
                {
                    Thread.Sleep(10);
                    tries++;
                }
            }
            MethodInfo getResults = opType.GetMethod("GetResults");
            return getResults != null ? getResults.Invoke(asyncOp, null) : null;
        }

        public static Task<string> RecognizePngBase64Async(string dataUrlOrBase64)
        {
            return Task.Run(() => RecognizeSync(dataUrlOrBase64));
        }

        private static string RecognizeSync(string dataUrlOrBase64)
        {
            if (string.IsNullOrEmpty(dataUrlOrBase64) || StreamType == null || DataWriterType == null || DecoderType == null)
            {
                return string.Empty;
            }

            object engine = RuEngine ?? EnEngine;
            if (engine == null)
            {
                return string.Empty;
            }

            string b64 = dataUrlOrBase64;
            int comma = b64.IndexOf(',');
            if (comma >= 0)
            {
                b64 = b64.Substring(comma + 1);
            }

            byte[] bytes = Convert.FromBase64String(b64);
            object stream = Activator.CreateInstance(StreamType);
            try
            {
                object writer = Activator.CreateInstance(DataWriterType, stream);
                DataWriterType.GetMethod("WriteBytes", new[] { typeof(byte[]) }).Invoke(writer, new object[] { bytes });
                AwaitWinRt(DataWriterType.GetMethod("StoreAsync").Invoke(writer, null));
                AwaitWinRt(DataWriterType.GetMethod("FlushAsync").Invoke(writer, null));
                DataWriterType.GetMethod("DetachStream").Invoke(writer, null);
                if (writer is IDisposable) ((IDisposable)writer).Dispose();

                StreamType.GetMethod("Seek").Invoke(stream, new object[] { (ulong)0 });

                MethodInfo createDecoder = null;
                foreach (MethodInfo m in DecoderType.GetMethods())
                {
                    if (m.Name == "CreateAsync" && m.GetParameters().Length == 1)
                    {
                        createDecoder = m;
                        break;
                    }
                }
                if (createDecoder == null) return string.Empty;

                object decoder = AwaitWinRt(createDecoder.Invoke(null, new[] { stream }));
                if (decoder == null) return string.Empty;

                object bgra8 = Enum.ToObject(PixelFormatType, 87);
                object premult = Enum.ToObject(AlphaModeType, 0);
                MethodInfo getBmp = decoder.GetType().GetMethod("GetSoftwareBitmapAsync", new[] { PixelFormatType, AlphaModeType });
                object bitmap = AwaitWinRt(getBmp.Invoke(decoder, new[] { bgra8, premult }));
                if (bitmap == null) return string.Empty;

                try
                {
                    MethodInfo recMethod = OcrEngineType.GetMethod("RecognizeAsync");
                    object ocrRes = AwaitWinRt(recMethod.Invoke(engine, new[] { bitmap }));
                    string text = ExtractText(ocrRes);
                    if (string.IsNullOrEmpty(text) && EnEngine != null && !ReferenceEquals(engine, EnEngine))
                    {
                        object enRes = AwaitWinRt(recMethod.Invoke(EnEngine, new[] { bitmap }));
                        text = ExtractText(enRes);
                    }
                    return text;
                }
                finally
                {
                    if (bitmap is IDisposable) ((IDisposable)bitmap).Dispose();
                }
            }
            finally
            {
                if (stream is IDisposable) ((IDisposable)stream).Dispose();
            }
        }

        private static string ExtractText(object ocrRes)
        {
            if (ocrRes == null) return string.Empty;
            PropertyInfo prop = ocrRes.GetType().GetProperty("Text");
            string raw = prop != null ? Convert.ToString(prop.GetValue(ocrRes, null)) : string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }
            var sb = new StringBuilder();
            bool prevSpace = false;
            foreach (char c in raw.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!prevSpace)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    prevSpace = false;
                }
            }
            return sb.ToString();
        }
    }
}
