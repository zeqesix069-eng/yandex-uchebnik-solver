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
        public List<DragNode> DragItems { get; set; }
        public List<DragNode> DropTargets { get; set; }

        public PageTaskState()
        {
            Controls = new List<TaskControl>();
            DragItems = new List<DragNode>();
            DropTargets = new List<DragNode>();
        }
    }

    public sealed class DragNode
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
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

        public string LastLlmResponse { get; private set; }

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
                var cmEls = document.querySelectorAll('.CodeMirror');
                for (var c = 0; c < cmEls.length; c++) {
                    var cmNode = cmEls[c];
                    if (!isVisible(cmNode) || !cmNode.CodeMirror) continue;
                    controls.push({
                        index: controls.length,
                        kind: 'code',
                        label: 'CodeMirror editor',
                        value: cmNode.CodeMirror.getValue() || '',
                        checked: false
                    });
                }

                var rawEls = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""], [role=""combobox""], [aria-haspopup=""listbox""], button.Select2-Button');
                for (var j = 0; j < rawEls.length; j++) {
                    var el = rawEls[j];
                    if (el.closest('.CodeMirror')) continue;
                    if (!isVisible(el)) continue;
                    var tag = el.tagName.toLowerCase();
                    var type = (el.getAttribute('type') || el.getAttribute('role') || (tag === 'button' ? 'dropdown' : tag)).toLowerCase();
                    if (type === 'hidden' || type === 'submit' || type === 'button') continue;
                    var labelEl = el.closest('label') || el.parentElement;
                    var label = clean(el.getAttribute('aria-label') || (labelEl ? labelEl.innerText : '') || el.placeholder || '');
                    if (tag === 'select') {
                        var opts = Array.prototype.slice.call(el.options).map(function(o) { return clean(o.text); }).filter(Boolean);
                        label = (label + ' [options: ' + opts.join(' | ') + ']').trim();
                    }
                    controls.push({
                        index: controls.length,
                        kind: type,
                        label: label.slice(0, 350),
                        value: el.value || clean(el.innerText) || '',
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
                    controls: controls,
                    dragItems: [],
                    dropTargets: []
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

            if (state.Controls.Count == 0 && state.HasSubmit)
            {
                const string dragScript = @"(function() {
                    function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                    function isVisible(el) {
                        if (!el) return false;
                        var r = el.getBoundingClientRect();
                        return r.width > 12 && r.height > 12;
                    }
                    var root = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                    var dragItems = [];
                    var dropTargets = [];
                    var all = root.querySelectorAll('div, span, li');
                    for (var i = 0; i < all.length; i++) {
                        var el = all[i];
                        if (!isVisible(el)) continue;
                        var st = window.getComputedStyle(el);
                        var r = el.getBoundingClientRect();
                        var txt = clean(el.innerText);
                        if ((el.getAttribute('draggable') === 'true' || st.cursor === 'grab' || st.cursor === '-webkit-grab') && txt && txt.length <= 120 && r.width < 450 && r.height < 120) {
                            dragItems.push({ index: dragItems.length, label: txt, x: Math.round(r.x + r.width / 2), y: Math.round(r.y + r.height / 2) });
                        } else if ((el.getAttribute('data-droppable') || (el.className && String(el.className).toLowerCase().indexOf('drop') >= 0) || (el.className && String(el.className).toLowerCase().indexOf('bucket') >= 0) || (el.className && String(el.className).toLowerCase().indexOf('column') >= 0)) && r.height >= 40 && r.width >= 60) {
                            dropTargets.push({ index: dropTargets.length, label: txt.slice(0, 80) || ('slot_' + dropTargets.length), x: Math.round(r.x + r.width / 2), y: Math.round(r.y + r.height / 2) });
                        }
                    }
                    return JSON.stringify({ dragItems: dragItems, dropTargets: dropTargets });
                })()";

                try
                {
                    object dragRaw = await _cdp.EvaluateAsync(dragScript, ct).ConfigureAwait(false);
                    var dragDict = _json.Deserialize<Dictionary<string, object>>(Convert.ToString(dragRaw));
                    if (dragDict != null)
                    {
                        if (dragDict.ContainsKey("dragItems") && dragDict["dragItems"] is ArrayList)
                        {
                            foreach (object item in (ArrayList)dragDict["dragItems"])
                            {
                                var d = item as Dictionary<string, object>;
                                if (d == null) continue;
                                state.DragItems.Add(new DragNode
                                {
                                    Index = Convert.ToInt32(d["index"]),
                                    Label = GetString(d, "label"),
                                    X = Convert.ToInt32(d["x"]),
                                    Y = Convert.ToInt32(d["y"])
                                });
                            }
                        }
                        if (dragDict.ContainsKey("dropTargets") && dragDict["dropTargets"] is ArrayList)
                        {
                            foreach (object item in (ArrayList)dragDict["dropTargets"])
                            {
                                var d = item as Dictionary<string, object>;
                                if (d == null) continue;
                                state.DropTargets.Add(new DragNode
                                {
                                    Index = Convert.ToInt32(d["index"]),
                                    Label = GetString(d, "label"),
                                    X = Convert.ToInt32(d["x"]),
                                    Y = Convert.ToInt32(d["y"])
                                });
                            }
                        }
                    }
                }
                catch
                {
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

            LastLlmResponse = llmRaw;

            var answerDict = _json.Deserialize<Dictionary<string, object>>(llmRaw);
            if (answerDict == null)
            {
                throw new InvalidOperationException("Модель вернула некорректный JSON: " + llmRaw);
            }

            string ansType = GetString(answerDict, "type");
            if (ansType == "drag" && answerDict.ContainsKey("moves") && answerDict["moves"] is ArrayList)
            {
                int moved = 0;
                foreach (object mObj in (ArrayList)answerDict["moves"])
                {
                    var m = mObj as Dictionary<string, object>;
                    if (m == null) continue;
                    int srcIdx = Convert.ToInt32(m["itemIndex"]);
                    int dstIdx = Convert.ToInt32(m["targetIndex"]);
                    if (srcIdx >= 0 && srcIdx < state.DragItems.Count && dstIdx >= 0 && dstIdx < state.DropTargets.Count)
                    {
                        DragNode src = state.DragItems[srcIdx];
                        DragNode dst = state.DropTargets[dstIdx];
                        await _cdp.DragAsync(src.X, src.Y, dst.X, dst.Y, ct).ConfigureAwait(false);
                        moved++;
                        await Task.Delay(120, ct).ConfigureAwait(false);
                    }
                }
                return "drag:" + moved;
            }

            string actionJson = _json.Serialize(answerDict);
            string applyScript = @"(function(ans) {
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                }
                function setDropdownOrSelect(el, val) {
                    var target = String(val || '').trim().toLowerCase();
                    if (el.tagName.toLowerCase() === 'select') {
                        for (var o = 0; o < el.options.length; o++) {
                            if (el.options[o].text.trim().toLowerCase() === target || el.options[o].value.trim().toLowerCase() === target) {
                                el.selectedIndex = o;
                                el.dispatchEvent(new Event('change', { bubbles: true }));
                                return true;
                            }
                        }
                        return false;
                    }
                    el.click();
                    var items = document.querySelectorAll('[role=""option""], .Select2-Item, .Menu-Item');
                    for (var k = 0; k < items.length; k++) {
                        var txt = (items[k].innerText || '').trim().toLowerCase();
                        if (txt === target || txt.indexOf(target) >= 0) {
                            items[k].click();
                            return true;
                        }
                    }
                    return false;
                }
                function setNativeValue(el, val) {
                    if (el.tagName.toLowerCase() === 'select' || el.getAttribute('role') === 'combobox' || el.getAttribute('aria-haspopup') === 'listbox' || (el.className && String(el.className).indexOf('Select2-Button') >= 0)) {
                        setDropdownOrSelect(el, val);
                        return;
                    }
                    if (el && el.CodeMirror) {
                        el.CodeMirror.setValue(val);
                        return;
                    }
                    var cm = el.closest ? el.closest('.CodeMirror') : null;
                    if (cm && cm.CodeMirror) {
                        cm.CodeMirror.setValue(val);
                        return;
                    }
                    if (window.ace && el.closest && el.closest('.ace_editor')) {
                        window.ace.edit(el.closest('.ace_editor')).setValue(val, -1);
                        return;
                    }
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
                var cmEls = document.querySelectorAll('.CodeMirror');
                for (var c = 0; c < cmEls.length; c++) {
                    if (isVisible(cmEls[c]) && cmEls[c].CodeMirror) els.push(cmEls[c]);
                }
                var raw = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""], [role=""combobox""], [aria-haspopup=""listbox""], button.Select2-Button');
                for (var i = 0; i < raw.length; i++) {
                    var el = raw[i];
                    if (el.closest('.CodeMirror')) continue;
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
            if (state.DragItems.Count > 0)
            {
                sb.AppendLine("Перетаскиваемые карточки (dragItems):");
                foreach (DragNode d in state.DragItems)
                {
                    sb.AppendFormat("- itemIndex={0}, text=\"{1}\"", d.Index, d.Label);
                    sb.AppendLine();
                }
                sb.AppendLine("Целевые зоны (dropTargets):");
                foreach (DragNode t in state.DropTargets)
                {
                    sb.AppendFormat("- targetIndex={0}, label=\"{1}\"", t.Index, t.Label);
                    sb.AppendLine();
                }
            }
            if (!string.IsNullOrEmpty(retryNote))
            {
                sb.AppendLine();
                sb.AppendLine("Примечание к прошлой попытке: " + retryNote);
            }
            sb.AppendLine();
            sb.AppendLine("Верни строго JSON одного из форматов:");
            sb.AppendLine("1) Для выбора вариантов (radio/checkbox): {\"type\":\"choice\",\"indices\":[0]} (для checkbox укажи ВСЕ подходящие варианты)");
            sb.AppendLine("2) Для ввода текста/чисел/кода/списков по порядку: {\"type\":\"inputs\",\"values\":[\"ответ\"]}");
            sb.AppendLine("3) Для перетаскивания карточек в зоны: {\"type\":\"drag\",\"moves\":[{\"itemIndex\":0,\"targetIndex\":0}]}");
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
