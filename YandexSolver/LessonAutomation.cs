using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace YandexSolver
{
    public sealed class PageTaskState
    {
        public string Url { get; set; }
        public string QuestionText { get; set; }
        public bool HasSubmit { get; set; }
        public bool HasNext { get; set; }
        public bool HasFinish { get; set; }
        public bool IsCorrectBanner { get; set; }
        public bool IsErrorBanner { get; set; }
        public int? TargetContextId { get; set; }
        public List<TaskControl> Controls { get; set; }

        public PageTaskState()
        {
            Controls = new List<TaskControl>();
        }
    }

    public sealed class TaskControl
    {
        public int Index { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public bool Checked { get; set; }
    }

    public sealed class LessonAutomation
    {
        private readonly CdpClient _cdp;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        public LessonAutomation(CdpClient cdp)
        {
            _cdp = cdp;
        }

        public async Task<PageTaskState> ExtractStateAsync(CancellationToken ct)
        {
            const string script = @"(function() {
                function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                }
                var main = document.querySelector('main') || document.body;
                var blocks = document.querySelector('.problem__block-list') || main;
                var qText = clean(blocks ? blocks.innerText : '');
                var bodyText = clean(document.body ? document.body.innerText : '');

                var buttons = Array.prototype.slice.call(document.querySelectorAll('button, a[role=""button""]'));
                var hasSubmit = false, hasNext = false, hasFinish = false;
                for (var i = 0; i < buttons.length; i++) {
                    var b = buttons[i];
                    if (!isVisible(b)) continue;
                    var t = clean(b.innerText).toLowerCase();
                    if (t === 'ответить' || t === 'проверить') hasSubmit = true;
                    else if (!b.disabled && (t === 'дальше' || t === 'далее' || t === 'продолжить')) hasNext = true;
                    else if (!b.disabled && t.indexOf('завершить') >= 0) hasFinish = true;
                }

                var controls = [];
                var rawEls = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""]');
                for (var j = 0; j < rawEls.length; j++) {
                    var el = rawEls[j];
                    if (!isVisible(el)) continue;
                    var tag = el.tagName.toLowerCase();
                    var type = (el.getAttribute('type') || el.getAttribute('role') || tag).toLowerCase();
                    if (type === 'hidden' || type === 'submit' || type === 'button') continue;
                    var labelEl = el.closest('label') || el.parentElement;
                    var label = clean(el.getAttribute('aria-label') || (labelEl ? labelEl.innerText : '') || el.placeholder || '');
                    controls.push({
                        index: controls.length,
                        kind: type,
                        label: label.slice(0, 300),
                        value: el.value || '',
                        checked: !!(el.checked || el.getAttribute('aria-checked') === 'true')
                    });
                }

                return JSON.stringify({
                    url: location.href,
                    questionText: qText.slice(0, 6000),
                    hasSubmit: hasSubmit,
                    hasNext: hasNext,
                    hasFinish: hasFinish,
                    isCorrectBanner: bodyText.indexOf('Этот ответ правильный') >= 0 || bodyText.indexOf('Правильно') >= 0,
                    isErrorBanner: bodyText.indexOf('Попробуй ещё') >= 0 || bodyText.indexOf('Неправильно') >= 0 || bodyText.indexOf('Ошибка') >= 0,
                    controls: controls
                });
            })()";

            object raw = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
            string json = Convert.ToString(raw);
            var dict = _json.Deserialize<Dictionary<string, object>>(json);

            var state = new PageTaskState
            {
                Url = GetString(dict, "url"),
                QuestionText = GetString(dict, "questionText"),
                HasSubmit = GetBool(dict, "hasSubmit"),
                HasNext = GetBool(dict, "hasNext"),
                HasFinish = GetBool(dict, "hasFinish"),
                IsCorrectBanner = GetBool(dict, "isCorrectBanner"),
                IsErrorBanner = GetBool(dict, "isErrorBanner")
            };

            if (dict != null && dict.ContainsKey("controls") && dict["controls"] is ArrayList)
            {
                foreach (object item in (ArrayList)dict["controls"])
                {
                    var c = item as Dictionary<string, object>;
                    if (c == null) continue;
                    state.Controls.Add(new TaskControl
                    {
                        Index = Convert.ToInt32(c["index"]),
                        Kind = GetString(c, "kind"),
                        Label = GetString(c, "label"),
                        Value = GetString(c, "value"),
                        Checked = GetBool(c, "checked")
                    });
                }
            }

            if (state.Controls.Count == 0)
            {
                List<int> childContexts = await _cdp.GetChildFrameContextIdsAsync(ct).ConfigureAwait(false);
                foreach (int ctxId in childContexts)
                {
                    try
                    {
                        object frameRaw = await _cdp.EvaluateAsync(script, ctxId, ct).ConfigureAwait(false);
                        string frameJson = Convert.ToString(frameRaw);
                        var frameDict = _json.Deserialize<Dictionary<string, object>>(frameJson);
                        if (frameDict != null && frameDict.ContainsKey("controls") && frameDict["controls"] is ArrayList)
                        {
                            var frameControls = (ArrayList)frameDict["controls"];
                            if (frameControls.Count > 0)
                            {
                                state.TargetContextId = ctxId;
                                foreach (object item in frameControls)
                                {
                                    var c = item as Dictionary<string, object>;
                                    if (c == null) continue;
                                    state.Controls.Add(new TaskControl
                                    {
                                        Index = Convert.ToInt32(c["index"]),
                                        Kind = GetString(c, "kind"),
                                        Label = GetString(c, "label"),
                                        Value = GetString(c, "value"),
                                        Checked = GetBool(c, "checked")
                                    });
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return state;
        }

        public async Task<string> SolveAndApplyAsync(
            PageTaskState state,
            AppSettings settings,
            string retryNote,
            CancellationToken ct)
        {
            string prompt = BuildPrompt(state, retryNote);
            string llmRaw = await Task.Run(() =>
                NativeSolverBridge.SolveTask(
                    settings.Provider,
                    settings.BaseUrl,
                    settings.ApiKey,
                    settings.Model,
                    settings.CustomSystemPrompt,
                    prompt,
                    settings.Temperature), ct).ConfigureAwait(false);

            var answerDict = _json.Deserialize<Dictionary<string, object>>(llmRaw);
            if (answerDict == null)
            {
                throw new InvalidOperationException("Модель вернула некорректный JSON: " + llmRaw);
            }

            string actionJson = _json.Serialize(answerDict);
            string applyScript = @"(function(ans) {
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                }
                function setNativeValue(el, val) {
                    var proto = el.tagName.toLowerCase() === 'textarea'
                        ? window.HTMLTextAreaElement.prototype
                        : window.HTMLInputElement.prototype;
                    var desc = Object.getOwnPropertyDescriptor(proto, 'value');
                    if (desc && desc.set) desc.set.call(el, val);
                    else el.value = val;
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                }
                var els = [];
                var raw = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""]');
                for (var i = 0; i < raw.length; i++) {
                    var el = raw[i];
                    if (!isVisible(el)) continue;
                    var t = (el.getAttribute('type') || el.getAttribute('role') || el.tagName).toLowerCase();
                    if (t === 'hidden' || t === 'submit' || t === 'button') continue;
                    els.push(el);
                }

                if (ans.type === 'choice' && Array.isArray(ans.indices)) {
                    for (var k = 0; k < ans.indices.length; k++) {
                        var idx = ans.indices[k];
                        if (els[idx]) els[idx].click();
                    }
                    return 'choice:' + ans.indices.join(',');
                }
                if (ans.type === 'inputs' && Array.isArray(ans.values)) {
                    for (var m = 0; m < ans.values.length && m < els.length; m++) {
                        setNativeValue(els[m], String(ans.values[m]));
                    }
                    return 'inputs:' + ans.values.length;
                }
                return 'noop';
            })(" + actionJson + ")";

            object applied = await _cdp.EvaluateAsync(applyScript, state.TargetContextId, ct).ConfigureAwait(false);
            return Convert.ToString(applied);
        }

        public async Task<bool> FillYandexKidsLoginAsync(string schoolCode, string login, CancellationToken ct)
        {
            string argsJson = _json.Serialize(new Dictionary<string, object>
            {
                { "school", schoolCode ?? string.Empty },
                { "login", login ?? string.Empty }
            });
            string script = @"(function(creds) {
                function setNativeValue(el, val) {
                    var desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
                    if (desc && desc.set) desc.set.call(el, val);
                    else el.value = val;
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                }
                var inputs = Array.prototype.slice.call(document.querySelectorAll('input'));
                var visible = inputs.filter(function(el) {
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 && el.type !== 'hidden';
                });
                if (visible.length === 0) return false;
                var filled = false;
                for (var i = 0; i < visible.length; i++) {
                    var el = visible[i];
                    var hint = ((el.placeholder || '') + ' ' + (el.name || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();
                    if ((hint.indexOf('школ') >= 0 || hint.indexOf('код') >= 0) && creds.school) {
                        setNativeValue(el, creds.school);
                        filled = true;
                    } else if ((hint.indexOf('логин') >= 0 || hint.indexOf('имя') >= 0 || hint.indexOf('login') >= 0) && creds.login) {
                        setNativeValue(el, creds.login);
                        filled = true;
                    }
                }
                if (!filled && visible.length >= 2) {
                    if (creds.school) setNativeValue(visible[0], creds.school);
                    if (creds.login) setNativeValue(visible[1], creds.login);
                    filled = true;
                }
                return filled;
            })(" + argsJson + ")";

            object res = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
            return res is bool && (bool)res;
        }

        public async Task<bool> ClickButtonAsync(string[] titles, CancellationToken ct)
        {
            string titlesJson = _json.Serialize(titles);
            string script = @"(function(targets) {
                function clean(s) { return (s || '').replace(/\s+/g, ' ').trim().toLowerCase(); }
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                }
                var btns = document.querySelectorAll('button, a[role=""button""]');
                for (var i = 0; i < btns.length; i++) {
                    var b = btns[i];
                    if (!isVisible(b) || b.disabled) continue;
                    var t = clean(b.innerText);
                    for (var k = 0; k < targets.length; k++) {
                        if (t === targets[k] || t.indexOf(targets[k]) === 0) {
                            b.click();
                            return true;
                        }
                    }
                }
                return false;
            })(" + titlesJson + ")";

            object res = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
            return res is bool && (bool)res;
        }

        private static string BuildPrompt(PageTaskState state, string retryNote)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Реши задание из Яндекс Учебника.");
            sb.AppendLine("Условие:");
            sb.AppendLine(state.QuestionText);
            sb.AppendLine();
            sb.AppendLine("Доступные элементы ввода:");
            foreach (TaskControl ctrl in state.Controls)
            {
                sb.AppendFormat("- index={0}, kind={1}, label=\"{2}\", value=\"{3}\"",
                    ctrl.Index, ctrl.Kind, ctrl.Label, ctrl.Value);
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(retryNote))
            {
                sb.AppendLine();
                sb.AppendLine("Примечание к прошлой попытке: " + retryNote);
            }
            sb.AppendLine();
            sb.AppendLine("Верни строго JSON одного из форматов:");
            sb.AppendLine("1) Для выбора вариантов (radio/checkbox): {\"type\":\"choice\",\"indices\":[0]}");
            sb.AppendLine("2) Для ввода текста/чисел в поля по порядку: {\"type\":\"inputs\",\"values\":[\"ответ\"]}");
            return sb.ToString();
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return string.Empty;
            return Convert.ToString(dict[key]);
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return false;
            return Convert.ToBoolean(dict[key]);
        }
    }
}
