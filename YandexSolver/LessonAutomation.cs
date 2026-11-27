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
        public bool HasRetry { get; set; }
        public bool IsCorrectBanner { get; set; }
        public bool IsErrorBanner { get; set; }
        public int? TargetContextId { get; set; }
        public List<TaskControl> Controls { get; set; }
        public List<DragNode> DragItems { get; set; }
        public List<DragNode> DropTargets { get; set; }
        public bool IsReorderList { get; set; }
        public bool HasVisuals { get; set; }
        public string ScreenshotBase64 { get; set; }

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

        private async Task ExpandSpoilersAsync(CancellationToken ct)
        {
            const string expandScript = @"(function() {
                function clean(s) { return (s || '').replace(/\s+/g, ' ').trim().toLowerCase(); }
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                }
                var root = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                var clicked = 0;
                var summaries = root.querySelectorAll('details:not([open]) > summary');
                for (var i = 0; i < summaries.length && clicked < 8; i++) {
                    if (isVisible(summaries[i])) { summaries[i].click(); clicked++; }
                }
                var ariaCollapsed = root.querySelectorAll('[aria-expanded=""false""]');
                for (var j = 0; j < ariaCollapsed.length && clicked < 8; j++) {
                    var el = ariaCollapsed[j];
                    if (!isVisible(el)) continue;
                    var hp = (el.getAttribute('aria-haspopup') || '').toLowerCase();
                    var role = (el.getAttribute('role') || '').toLowerCase();
                    var cls = String(el.className || '');
                    if (hp || role === 'combobox' || /select|dropdown/i.test(cls)) continue;
                    var txt = clean(el.innerText);
                    if (!txt || txt === '?' || txt.length > 140) continue;
                    el.click();
                    clicked++;
                }
                var hints = root.querySelectorAll('button, [role=""button""]');
                for (var k = 0; k < hints.length && clicked < 8; k++) {
                    var b = hints[k];
                    if (!isVisible(b) || b.disabled) continue;
                    var t = clean(b.innerText);
                    if (t === 'подсказка' || t === 'показать подсказку') {
                        b.click();
                        clicked++;
                    }
                }
                return clicked;
            })()";

            try
            {
                object res = await _cdp.EvaluateAsync(expandScript, ct).ConfigureAwait(false);
                if (res != null && Convert.ToInt32(res) > 0)
                {
                    await Task.Delay(180, ct).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        public async Task<PageTaskState> ExtractStateAsync(CancellationToken ct)
        {
            await ExpandSpoilersAsync(ct).ConfigureAwait(false);

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
                var hasSubmit = false, hasNext = false, hasFinish = false, hasRetry = false;
                for (var i = 0; i < buttons.length; i++) {
                    var b = buttons[i];
                    if (!isVisible(b)) continue;
                    var t = clean(b.innerText).toLowerCase();
                    if (t === 'ответить' || t === 'проверить') hasSubmit = true;
                    else if (!b.disabled && (t === 'дальше' || t === 'далее' || t === 'продолжить')) hasNext = true;
                    else if (!b.disabled && t.indexOf('завершить') >= 0) hasFinish = true;
                    else if (!b.disabled && (t === 'ещё раз' || t === 'еще раз')) hasRetry = true;
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

                var pickedEls = [];
                function isDup(el) {
                    var er = el.getBoundingClientRect();
                    for (var p = 0; p < pickedEls.length; p++) {
                        var prev = pickedEls[p];
                        if (prev.contains(el) || el.contains(prev)) return true;
                        var pr = prev.getBoundingClientRect();
                        if (Math.abs((er.left + er.width / 2) - (pr.left + pr.width / 2)) < 10 && Math.abs((er.top + er.height / 2) - (pr.top + pr.height / 2)) < 10) return true;
                    }
                    return false;
                }
                var rawEls = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""], [role=""combobox""], [aria-haspopup=""listbox""], [aria-haspopup=""menu""], [aria-expanded], button.Select2-Button');
                var hasNamedDrops = document.querySelectorAll('[aria-label=""Выберите вариант""]').length >= 2;
                for (var j = 0; j < rawEls.length; j++) {
                    var el = rawEls[j];
                    if (el.closest('.CodeMirror, [role=""listbox""], [role=""menu""], [role=""option""]')) continue;
                    if (!isVisible(el)) continue;
                    if (isDup(el)) continue;
                    var tag = el.tagName.toLowerCase();
                    var roleAttr = (el.getAttribute('role') || '').toLowerCase();
                    var hasPopup = (el.getAttribute('aria-haspopup') || '').toLowerCase();
                    var cls = String(el.className || '');
                    var isDrop = tag === 'select' || roleAttr === 'combobox' || hasPopup === 'listbox' || hasPopup === 'menu' || el.hasAttribute('aria-expanded') || /select2-button/i.test(cls);
                    if (hasNamedDrops && isDrop && clean(el.getAttribute('aria-label') || '').toLowerCase() !== 'выберите вариант') continue;
                    var type = (isDrop ? 'dropdown' : (el.getAttribute('type') || roleAttr || tag)).toLowerCase();
                    if (!isDrop && (type === 'hidden' || type === 'submit' || type === 'button')) continue;
                    pickedEls.push(el);
                    var ariaLbl = clean(el.getAttribute('aria-label') || '');
                    if (ariaLbl.toLowerCase() === 'выберите вариант') ariaLbl = '';
                    var rowContainer = el.closest('p, li, tr, [class*=""row"" i], [class*=""line"" i]');
                    var labelEl = el.closest('label') || el.previousElementSibling || (rowContainer && clean(rowContainer.innerText).length <= 200 ? rowContainer : null) || (el.parentElement && clean(el.parentElement.innerText).length <= 200 ? el.parentElement : null);
                    var label = clean(ariaLbl || (labelEl ? labelEl.innerText : '') || el.placeholder || '');
                    var td = el.closest('td, th');
                    if (td) {
                        var tr = td.closest('tr');
                        var tbl = td.closest('table');
                        var colHdr = '';
                        if (tbl && tbl.rows && tbl.rows.length > 0 && tbl.rows[0].cells && tbl.rows[0].cells[td.cellIndex]) {
                            colHdr = clean(tbl.rows[0].cells[td.cellIndex].innerText);
                        }
                        var rowTxt = tr ? clean(tr.innerText) : '';
                        label = clean((colHdr ? colHdr + ' | ' : '') + rowTxt + (label ? ' | ' + label : ''));
                    }
                    var selEl = (tag === 'select') ? el : (el.querySelector ? el.querySelector('select') : null);
                    if (selEl && selEl.options) {
                        var opts = Array.prototype.slice.call(selEl.options).map(function(o) { return clean(o.text); }).filter(Boolean);
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

                if (controls.length === 0) {
                    var dropRoot = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                    var cand = dropRoot.querySelectorAll('button, [role=""button""], [tabindex], div, span');
                    var ignoreBtns = ['ответить', 'проверить', 'дальше', 'далее', 'продолжить', 'завершить', 'ещё раз', 'еще раз', 'сбросить', 'закрыть', 'подсказка', 'продолжение', 'закрыть подсказку'];
                    for (var d = 0; d < cand.length; d++) {
                        var cel = cand[d];
                        if (!isVisible(cel) || cel.disabled) continue;
                        if (cel.closest('header, nav, footer')) continue;
                        var ctag = cel.tagName.toLowerCase();
                        var crole = (cel.getAttribute('role') || '').toLowerCase();
                        if ((ctag === 'div' || ctag === 'span') && crole !== 'button' && !cel.hasAttribute('tabindex')) {
                            var cst = window.getComputedStyle(cel);
                            if (cst.cursor !== 'pointer' && !cel.querySelector('svg')) continue;
                            if (cel.querySelector('button, input, select, textarea, [role=""button""], [tabindex]')) continue;
                        }
                        var cr = cel.getBoundingClientRect();
                        if (cr.width < 24 || cr.width > 340 || cr.height < 18 || cr.height > 64) continue;
                        var ctxt = clean(cel.innerText);
                        var clow = ctxt.toLowerCase();
                        if (!ctxt || ctxt.length > 45) continue;
                        var isIgnored = false;
                        for (var ig = 0; ig < ignoreBtns.length; ig++) {
                            if (clow === ignoreBtns[ig] || clow.indexOf(ignoreBtns[ig]) >= 0) { isIgnored = true; break; }
                        }
                        if (isIgnored) continue;
                        var clblEl = cel.closest('label') || cel.previousElementSibling || (cel.parentElement && clean(cel.parentElement.innerText).length <= 200 ? cel.parentElement : null);
                        var clbl = clean(cel.getAttribute('aria-label') || (clblEl ? clblEl.innerText : '') || '');
                        controls.push({
                            index: controls.length,
                            kind: 'dropdown',
                            label: clbl.slice(0, 350),
                            value: ctxt,
                            checked: false
                        });
                    }
                }

                var hasVisuals = false;
                var visRoot = document.querySelector('.problem__block-list') || main;
                var visEls = visRoot ? visRoot.querySelectorAll('img, svg, canvas, video, math') : [];
                for (var v = 0; v < visEls.length; v++) {
                    var vr = visEls[v].getBoundingClientRect();
                    if (vr.width > 64 && vr.height > 64) { hasVisuals = true; break; }
                }

                return JSON.stringify({
                    url: location.href,
                    questionText: qText.slice(0, 6000),
                    hasSubmit: hasSubmit,
                    hasNext: hasNext,
                    hasFinish: hasFinish,
                    hasRetry: hasRetry,
                    hasVisuals: hasVisuals,
                    isCorrectBanner: bodyText.indexOf('Этот ответ правильный') >= 0 || bodyText.indexOf('Правильно') >= 0,
                    isErrorBanner: hasRetry || bodyText.indexOf('Попробуй ещё') >= 0 || bodyText.indexOf('Неправильно') >= 0 || bodyText.indexOf('Ответ неверный') >= 0 || bodyText.indexOf('Ошибка') >= 0,
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
                HasRetry = GetBool(dict, "hasRetry"),
                HasVisuals = GetBool(dict, "hasVisuals"),
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
                const string dragImageScript = @"(function() {
                    function toPng2x(img, sx, sy, sw, sh) {
                        var c = document.createElement('canvas');
                        c.width = Math.max(20, Math.round(sw * 2));
                        c.height = Math.max(20, Math.round(sh * 2));
                        var ctx = c.getContext('2d');
                        ctx.fillStyle = '#ffffff';
                        ctx.fillRect(0, 0, c.width, c.height);
                        ctx.drawImage(img, sx, sy, sw, sh, 0, 0, c.width, c.height);
                        return c.toDataURL('image/png');
                    }
                    var container = document.querySelector('.marker-dragimage');
                    if (!container) return JSON.stringify({ hasDragImage: false });

                    var choices = container.querySelectorAll('img.marker-dragimage__choice');
                    var fields = container.querySelectorAll('.marker-dragimage__field');
                    var bg = container.querySelector('img.marker-dragimage__background');

                    var items = [];
                    for (var i = 0; i < choices.length; i++) {
                        var im = choices[i];
                        var r = im.getBoundingClientRect();
                        if (r.width <= 0 || r.height <= 0) continue;
                        var b64 = '';
                        try { b64 = toPng2x(im, 0, 0, im.naturalWidth || r.width, im.naturalHeight || r.height); } catch (e) {}
                        items.push({ index: items.length, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2), img: b64 });
                    }

                    var targets = [];
                    var bgRect = bg ? bg.getBoundingClientRect() : null;
                    var scaleX = (bg && bgRect && bgRect.width > 0) ? (bg.naturalWidth / bgRect.width) : 1;
                    var scaleY = (bg && bgRect && bgRect.height > 0) ? (bg.naturalHeight / bgRect.height) : 1;

                    for (var j = 0; j < fields.length; j++) {
                        var f = fields[j];
                        var fr = f.getBoundingClientRect();
                        var cx = Math.round(fr.left + fr.width / 2);
                        var cy = Math.round(fr.top + fr.height / 2);
                        var cropB64 = '';
                        if (bg && bgRect && bgRect.width > 0) {
                            try {
                                var relX = (cx - bgRect.left) * scaleX;
                                var relY = (cy - bgRect.top) * scaleY;
                                var sx = Math.max(0, relX - 180 * scaleX);
                                var sy = Math.max(0, relY - 35 * scaleY);
                                var sw = Math.min((bg.naturalWidth || bgRect.width) - sx, 300 * scaleX);
                                var sh = Math.min((bg.naturalHeight || bgRect.height) - sy, 70 * scaleY);
                                if (sw > 10 && sh > 10) cropB64 = toPng2x(bg, sx, sy, sw, sh);
                            } catch (e) {}
                        }
                        targets.push({ index: targets.length, x: cx, y: cy, img: cropB64 });
                    }

                    return JSON.stringify({ hasDragImage: true, items: items, targets: targets });
                })()";

                try
                {
                    object diRaw = await _cdp.EvaluateAsync(dragImageScript, ct).ConfigureAwait(false);
                    var diDict = _json.Deserialize<Dictionary<string, object>>(Convert.ToString(diRaw));
                    if (diDict != null && GetBool(diDict, "hasDragImage"))
                    {
                        if (diDict.ContainsKey("items") && diDict["items"] is ArrayList)
                        {
                            foreach (object obj in (ArrayList)diDict["items"])
                            {
                                var d = obj as Dictionary<string, object>;
                                if (d == null) continue;
                                string ocr = await OcrHelper.RecognizePngBase64Async(GetString(d, "img")).ConfigureAwait(false);
                                state.DragItems.Add(new DragNode
                                {
                                    Index = Convert.ToInt32(d["index"]),
                                    Label = string.IsNullOrEmpty(ocr) ? ("item_" + d["index"]) : ocr,
                                    X = Convert.ToInt32(d["x"]),
                                    Y = Convert.ToInt32(d["y"])
                                });
                            }
                        }
                        if (diDict.ContainsKey("targets") && diDict["targets"] is ArrayList)
                        {
                            foreach (object obj in (ArrayList)diDict["targets"])
                            {
                                var d = obj as Dictionary<string, object>;
                                if (d == null) continue;
                                string ocr = await OcrHelper.RecognizePngBase64Async(GetString(d, "img")).ConfigureAwait(false);
                                state.DropTargets.Add(new DragNode
                                {
                                    Index = Convert.ToInt32(d["index"]),
                                    Label = string.IsNullOrEmpty(ocr) ? ("slot_" + d["index"]) : ("slot_" + d["index"] + " (" + ocr + ")"),
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

            if (state.Controls.Count == 0 && state.HasSubmit && state.DragItems.Count == 0)
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

                    if (state.DragItems.Count >= 2)
                    {
                        bool sameAsItems = state.DropTargets.Count == state.DragItems.Count;
                        if (sameAsItems)
                        {
                            for (int i = 0; i < state.DragItems.Count; i++)
                            {
                                DragNode di = state.DragItems[i];
                                DragNode dt = state.DropTargets[i];
                                bool closePos = Math.Abs(di.X - dt.X) <= 20 && Math.Abs(di.Y - dt.Y) <= 20;
                                bool sameText = string.Equals(di.Label, dt.Label, StringComparison.OrdinalIgnoreCase);
                                if (!closePos && !sameText)
                                {
                                    sameAsItems = false;
                                    break;
                                }
                            }
                        }

                        if (state.DropTargets.Count == 0 || sameAsItems)
                        {
                            state.IsReorderList = true;
                            state.DropTargets.Clear();
                        }
                    }
                }
                catch
                {
                }
            }

            if (state.DragItems.Count == 0 && state.HasSubmit &&
                (state.Controls.Count == 0 || (state.Controls.Count >= 4 && state.Controls.Count % 2 == 0 && !state.Controls.Exists(c => c.Kind == "checkbox" || c.Kind == "text" || c.Kind == "number" || c.Kind == "textarea" || c.Kind == "code" || c.Kind == "select"))))
            {
                const string matchScript = @"(function() {
                    function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                    function isVisible(el) {
                        if (!el) return false;
                        var r = el.getBoundingClientRect();
                        return r.width > 0 && r.height > 0;
                    }
                    function getCardText(el) {
                        var cur = el;
                        for (var step = 0; step < 4 && cur; step++) {
                            var t = clean(cur.innerText);
                            if (t && t.length <= 120) return t;
                            cur = cur.parentElement;
                        }
                        return '';
                    }
                    function splitColumns(nodes) {
                        if (nodes.length < 4 || nodes.length % 2 !== 0) return null;
                        var minX = 99999, maxX = -99999;
                        for (var i = 0; i < nodes.length; i++) {
                            if (nodes[i].x < minX) minX = nodes[i].x;
                            if (nodes[i].x > maxX) maxX = nodes[i].x;
                        }
                        if (maxX - minX < 60) return null;
                        var midX = (minX + maxX) / 2;
                        var left = [], right = [];
                        for (var j = 0; j < nodes.length; j++) {
                            if (nodes[j].x < midX) left.push(nodes[j]);
                            else right.push(nodes[j]);
                        }
                        if (left.length !== right.length || left.length < 2) return null;
                        left.sort(function(a, b) { return a.y - b.y; });
                        right.sort(function(a, b) { return a.y - b.y; });
                        return { left: left, right: right };
                    }
                    var root = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                    var pins = [];
                    var all = root.querySelectorAll('div, span, button, [role=""button""]');
                    for (var i = 0; i < all.length; i++) {
                        var el = all[i];
                        if (!isVisible(el)) continue;
                        var r = el.getBoundingClientRect();
                        if (r.width >= 10 && r.width <= 36 && r.height >= 10 && r.height <= 36) {
                            var st = window.getComputedStyle(el);
                            var cls = String(el.className || '').toLowerCase();
                            var isCircle = (st.borderRadius && st.borderRadius.indexOf('50%') >= 0) || cls.indexOf('dot') >= 0 || cls.indexOf('pin') >= 0 || cls.indexOf('point') >= 0 || cls.indexOf('circle') >= 0 || cls.indexOf('connector') >= 0;
                            if (isCircle || st.cursor === 'pointer' || st.cursor === 'grab') {
                                var lbl = getCardText(el);
                                if (lbl) {
                                    pins.push({ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2), label: lbl });
                                }
                            }
                        }
                    }
                    var res = splitColumns(pins);
                    if (res) return JSON.stringify(res);

                    var cards = [];
                    for (var k = 0; k < all.length; k++) {
                        var cel = all[k];
                        if (!isVisible(cel)) continue;
                        var cr = cel.getBoundingClientRect();
                        if (cr.width >= 60 && cr.width <= 360 && cr.height >= 24 && cr.height <= 90) {
                            if (cel.querySelector('div, span, button') && clean(cel.innerText) === '') continue;
                            var cst = window.getComputedStyle(cel);
                            if (cst.cursor === 'pointer' || cst.cursor === 'grab' || cel.getAttribute('draggable') === 'true') {
                                var ctxt = clean(cel.innerText);
                                if (ctxt && ctxt.length <= 100) {
                                    cards.push({ x: Math.round(cr.left + cr.width / 2), y: Math.round(cr.top + cr.height / 2), label: ctxt });
                                }
                            }
                        }
                    }
                    res = splitColumns(cards);
                    return res ? JSON.stringify(res) : '';
                })()";

                try
                {
                    object matchRaw = await _cdp.EvaluateAsync(matchScript, ct).ConfigureAwait(false);
                    string matchJson = Convert.ToString(matchRaw);
                    if (!string.IsNullOrEmpty(matchJson))
                    {
                        var matchDict = _json.Deserialize<Dictionary<string, object>>(matchJson);
                        if (matchDict != null && matchDict.ContainsKey("left") && matchDict.ContainsKey("right"))
                        {
                            var leftArr = matchDict["left"] as ArrayList;
                            var rightArr = matchDict["right"] as ArrayList;
                            if (leftArr != null && rightArr != null && leftArr.Count == rightArr.Count && leftArr.Count >= 2)
                            {
                                state.Controls.Clear();
                                state.IsReorderList = false;
                                for (int i = 0; i < leftArr.Count; i++)
                                {
                                    var l = leftArr[i] as Dictionary<string, object>;
                                    var r = rightArr[i] as Dictionary<string, object>;
                                    if (l == null || r == null) continue;
                                    state.DragItems.Add(new DragNode
                                    {
                                        Index = i,
                                        Label = GetString(l, "label"),
                                        X = Convert.ToInt32(l["x"]),
                                        Y = Convert.ToInt32(l["y"])
                                    });
                                    state.DropTargets.Add(new DragNode
                                    {
                                        Index = i,
                                        Label = GetString(r, "label"),
                                        X = Convert.ToInt32(r["x"]),
                                        Y = Convert.ToInt32(r["y"])
                                    });
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (state.HasSubmit && (state.HasVisuals || state.DragItems.Count > 0))
            {
                try
                {
                    state.ScreenshotBase64 = await _cdp.CaptureScreenshotBase64Async(ct).ConfigureAwait(false);
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
            Task<string> llmTask = Task.Run(() =>
                NativeSolverBridge.SolveTask(
                    settings.Provider,
                    settings.BaseUrl,
                    settings.ApiKey,
                    settings.Model,
                    settings.CustomSystemPrompt,
                    prompt,
                    settings.Temperature,
                    state.ScreenshotBase64));

            var cancelTcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelTcs.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(llmTask, cancelTcs.Task).ConfigureAwait(false);
                if (completed == cancelTcs.Task)
                {
                    ct.ThrowIfCancellationRequested();
                }
            }

            string llmRaw = await llmTask.ConfigureAwait(false);

            LastLlmResponse = llmRaw;
            return await ApplyAnswerJsonAsync(state, llmRaw, ct).ConfigureAwait(false);
        }

        public async Task<string> ApplyAnswerJsonAsync(PageTaskState state, string llmRaw, CancellationToken ct)
        {
            LastLlmResponse = llmRaw;
            var answerDict = _json.Deserialize<Dictionary<string, object>>(llmRaw);
            if (answerDict == null)
            {
                throw new InvalidOperationException("Модель вернула некорректный JSON: " + llmRaw);
            }

            string ansType = GetString(answerDict, "type");
            if (ansType == "reorder" && answerDict.ContainsKey("order") && answerDict["order"] is ArrayList)
            {
                var desiredOrder = new List<int>();
                foreach (object o in (ArrayList)answerDict["order"])
                {
                    int idx = Convert.ToInt32(o);
                    if (idx >= 0 && idx < state.DragItems.Count && !desiredOrder.Contains(idx))
                    {
                        desiredOrder.Add(idx);
                    }
                }

                var currentOrder = new List<int>();
                for (int i = 0; i < state.DragItems.Count; i++)
                {
                    currentOrder.Add(i);
                }

                int moved = 0;
                for (int pos = 0; pos < desiredOrder.Count; pos++)
                {
                    int wantedInitialIdx = desiredOrder[pos];
                    int curPos = currentOrder.IndexOf(wantedInitialIdx);
                    if (curPos < 0 || curPos == pos) continue;

                    List<DragNode> liveNodes = await GetCurrentDragNodesAsync(ct).ConfigureAwait(false);
                    if (liveNodes.Count != state.DragItems.Count)
                    {
                        liveNodes = state.DragItems;
                    }

                    string wantedLabel = state.DragItems[wantedInitialIdx].Label;
                    int liveSrcIdx = -1;
                    for (int k = pos; k < liveNodes.Count; k++)
                    {
                        if (string.Equals(liveNodes[k].Label, wantedLabel, StringComparison.Ordinal))
                        {
                            liveSrcIdx = k;
                            break;
                        }
                    }
                    if (liveSrcIdx < 0) liveSrcIdx = curPos;

                    if (liveSrcIdx != pos && liveSrcIdx < liveNodes.Count && pos < liveNodes.Count)
                    {
                        DragNode src = liveNodes[liveSrcIdx];
                        DragNode dst = liveNodes[pos];
                        await _cdp.DragAsync(src.X, src.Y, dst.X, dst.Y, ct).ConfigureAwait(false);
                        moved++;
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }

                    currentOrder.RemoveAt(curPos);
                    currentOrder.Insert(pos, wantedInitialIdx);
                }
                return "reorder:" + moved;
            }

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
                        await Task.Delay(220, ct).ConfigureAwait(false);
                    }
                }
                return "drag:" + moved;
            }

            if (ansType == "inputs" && answerDict.ContainsKey("values") && answerDict["values"] is ArrayList && state.Controls.Exists(c => c.Kind == "dropdown"))
            {
                var vals = (ArrayList)answerDict["values"];
                int appliedDrops = 0;
                for (int m = 0; m < vals.Count && m < state.Controls.Count; m++)
                {
                    string targetVal = Convert.ToString(vals[m]) ?? string.Empty;
                    string clickTriggerJs = @"(function(idx) {
                        function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                        function isVisible(el) { if (!el) return false; var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; }
                        function collect() {
                            var list = [];
                            var cmEls = document.querySelectorAll('.CodeMirror');
                            for (var c = 0; c < cmEls.length; c++) { if (isVisible(cmEls[c]) && cmEls[c].CodeMirror) list.push(cmEls[c]); }
                            function isDup(el) {
                                var er = el.getBoundingClientRect();
                                for (var p = 0; p < list.length; p++) {
                                    var prev = list[p];
                                    if (prev.contains(el) || el.contains(prev)) return true;
                                    var pr = prev.getBoundingClientRect();
                                    if (Math.abs((er.left + er.width / 2) - (pr.left + pr.width / 2)) < 10 && Math.abs((er.top + er.height / 2) - (pr.top + pr.height / 2)) < 10) return true;
                                }
                                return false;
                            }
                            var raw = document.querySelectorAll('input, textarea, select, [role=""radio""], [role=""checkbox""], [role=""combobox""], [aria-haspopup=""listbox""], [aria-haspopup=""menu""], [aria-expanded], button.Select2-Button');
                            var hasNamedDrops = document.querySelectorAll('[aria-label=""Выберите вариант""]').length >= 2;
                            for (var i = 0; i < raw.length; i++) {
                                var el = raw[i];
                                if (el.closest('.CodeMirror, [role=""listbox""], [role=""menu""], [role=""option""]') || !isVisible(el) || isDup(el)) continue;
                                var tag = el.tagName.toLowerCase();
                                var roleAttr = (el.getAttribute('role') || '').toLowerCase();
                                var hasPopup = (el.getAttribute('aria-haspopup') || '').toLowerCase();
                                var cls = String(el.className || '');
                                var isDrop = tag === 'select' || roleAttr === 'combobox' || hasPopup === 'listbox' || hasPopup === 'menu' || el.hasAttribute('aria-expanded') || /select2-button/i.test(cls);
                                if (hasNamedDrops && isDrop && clean(el.getAttribute('aria-label') || '').toLowerCase() !== 'выберите вариант') continue;
                                var t = (isDrop ? 'dropdown' : (el.getAttribute('type') || roleAttr || tag)).toLowerCase();
                                if (!isDrop && (t === 'hidden' || t === 'submit' || t === 'button')) continue;
                                list.push(el);
                            }
                            if (list.length === 0) {
                                var dropRoot = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                                var cand = dropRoot.querySelectorAll('button, [role=""button""], [tabindex], div, span');
                                var ignoreBtns = ['ответить', 'проверить', 'дальше', 'далее', 'продолжить', 'завершить', 'ещё раз', 'еще раз', 'сбросить', 'закрыть', 'подсказка', 'продолжение', 'закрыть подсказку'];
                                for (var d = 0; d < cand.length; d++) {
                                    var cel = cand[d];
                                    if (!isVisible(cel) || cel.disabled || cel.closest('header, nav, footer')) continue;
                                    var ctag = cel.tagName.toLowerCase();
                                    var crole = (cel.getAttribute('role') || '').toLowerCase();
                                    if ((ctag === 'div' || ctag === 'span') && crole !== 'button' && !cel.hasAttribute('tabindex')) {
                                        var cst = window.getComputedStyle(cel);
                                        if (cst.cursor !== 'pointer' && !cel.querySelector('svg')) continue;
                                        if (cel.querySelector('button, input, select, textarea, [role=""button""], [tabindex]')) continue;
                                    }
                                    var cr = cel.getBoundingClientRect();
                                    if (cr.width < 24 || cr.width > 340 || cr.height < 18 || cr.height > 64) continue;
                                    var ctxt = clean(cel.innerText);
                                    var clow = ctxt.toLowerCase();
                                    if (!ctxt || ctxt.length > 45) continue;
                                    var ig = false;
                                    for (var k = 0; k < ignoreBtns.length; k++) { if (clow === ignoreBtns[k] || clow.indexOf(ignoreBtns[k]) >= 0) { ig = true; break; } }
                                    if (!ig) list.push(cel);
                                }
                            }
                            return list;
                        }
                        if (idx === 0 || !window.__ysDrops) { window.__ysDrops = collect(); }
                        var els = window.__ysDrops;
                        var el = els[idx];
                        if (!el) return JSON.stringify({ status: 'missing' });
                        var sel = (el.tagName.toLowerCase() === 'select') ? el : (el.querySelector ? el.querySelector('select') : null);
                        if (sel) {
                            window.__ysActiveSelect = sel;
                            return JSON.stringify({ status: 'native-select' });
                        }
                        el.scrollIntoView({ block: 'center' });
                        var rr = el.getBoundingClientRect();
                        return JSON.stringify({ status: 'opened', x: Math.round(rr.left + rr.width / 2), y: Math.round(rr.top + rr.height / 2) });
                    })(" + m + ")";

                    object trigRaw = await _cdp.EvaluateAsync(clickTriggerJs, state.TargetContextId, ct).ConfigureAwait(false);
                    var trigDict = _json.Deserialize<Dictionary<string, object>>(Convert.ToString(trigRaw));
                    string trigStatus = GetString(trigDict, "status");
                    if (trigStatus == "native-select")
                    {
                        string selectJs = @"(function(val) {
                            var el = window.__ysActiveSelect;
                            if (!el) return false;
                            var target = String(val || '').trim().toLowerCase();
                            var matchIdx = -1;
                            for (var o = 0; o < el.options.length; o++) {
                                var ot = el.options[o].text.trim().toLowerCase();
                                var ov = el.options[o].value.trim().toLowerCase();
                                if (ot === target || ov === target) { matchIdx = o; break; }
                                if (matchIdx < 0 && target.length >= 2 && (ot.indexOf(target) >= 0 || ov.indexOf(target) >= 0)) matchIdx = o;
                            }
                            if (matchIdx < 0) return false;
                            var desc = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value');
                            if (desc && desc.set) desc.set.call(el, el.options[matchIdx].value);
                            el.selectedIndex = matchIdx;
                            el.dispatchEvent(new Event('input', { bubbles: true }));
                            el.dispatchEvent(new Event('change', { bubbles: true }));
                            return true;
                        })(" + _json.Serialize(targetVal) + ")";
                        await _cdp.EvaluateAsync(selectJs, state.TargetContextId, ct).ConfigureAwait(false);
                        appliedDrops++;
                        continue;
                    }

                    if (trigStatus == "opened")
                    {
                        int tx = Convert.ToInt32(trigDict["x"]);
                        int ty = Convert.ToInt32(trigDict["y"]);
                        await _cdp.ClickAtAsync(tx, ty, ct).ConfigureAwait(false);
                        await Task.Delay(140, ct).ConfigureAwait(false);
                        string pickJs = @"(function(activeIdx, val) {
                            function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                            function isVisible(el) { if (!el) return false; var r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; }
                            var activeTrig = window.__ysDrops ? window.__ysDrops[activeIdx] : null;
                            var trigRect = activeTrig ? activeTrig.getBoundingClientRect() : null;
                            function isOtherTrigger(el) {
                                if (!window.__ysDrops) return false;
                                for (var i = 0; i < window.__ysDrops.length; i++) {
                                    var tr = window.__ysDrops[i];
                                    if (tr === el || el.contains(tr)) return true;
                                    if (i !== activeIdx && tr.contains(el)) return true;
                                }
                                return false;
                            }
                            var target = String(val || '').trim().toLowerCase();
                            function findIn(nodeList) {
                                var exactPopup = null;
                                var exactAny = null;
                                var partial = null;
                                for (var k = 0; k < nodeList.length; k++) {
                                    var it = nodeList[k];
                                    if (!isVisible(it) || isOtherTrigger(it)) continue;
                                    var r = it.getBoundingClientRect();
                                    if (r.height < 16 || r.height > 64 || r.width < 20 || r.width > 380) continue;
                                    if (trigRect && Math.abs(r.top - trigRect.top) < 6 && Math.abs(r.left - trigRect.left) < 12) continue;
                                    var txt = clean(it.innerText).toLowerCase();
                                    if (!txt) continue;
                                    var hasMatchingChild = false;
                                    for (var c = 0; c < it.children.length; c++) {
                                        if (clean(it.children[c].innerText).toLowerCase() === txt) { hasMatchingChild = true; break; }
                                    }
                                    if (hasMatchingChild) continue;
                                    if (txt === target) {
                                        var inPopup = !!(it.closest('[role=""listbox""], [role=""menu""], [class*=""popup"" i], [class*=""menu"" i], [class*=""dropdown"" i], [class*=""list"" i]'));
                                        if (inPopup) exactPopup = it;
                                        exactAny = it;
                                    } else if (!partial && target.length >= 2 && txt.indexOf(target) >= 0 && txt.length <= target.length + 20) {
                                        partial = it;
                                    }
                                }
                                return exactPopup || exactAny || partial;
                            }
                            var scope = (activeTrig && activeTrig.parentElement) ? activeTrig.parentElement : document;
                            var chosen = findIn(scope.querySelectorAll('[role=""option""], [role=""menuitem""], .Select2-Item, .Menu-Item, [role=""listbox""] *, [role=""menu""] *, ul li, button, div, span, a'));
                            if (!chosen && scope !== document) {
                                chosen = findIn(document.querySelectorAll('[role=""option""], [role=""menuitem""], .Select2-Item, .Menu-Item, [role=""listbox""] *, [role=""menu""] *, ul li'));
                            }
                            if (chosen) {
                                chosen.scrollIntoView({ block: 'nearest' });
                                var rr = chosen.getBoundingClientRect();
                                return JSON.stringify({ found: true, x: Math.round(rr.left + rr.width / 2), y: Math.round(rr.top + rr.height / 2) });
                            }
                            return JSON.stringify({ found: false });
                        })(" + m + ", " + _json.Serialize(targetVal) + ")";
                        object pickRaw = await _cdp.EvaluateAsync(pickJs, state.TargetContextId, ct).ConfigureAwait(false);
                        var pickDict = _json.Deserialize<Dictionary<string, object>>(Convert.ToString(pickRaw));
                        if (pickDict != null && GetBool(pickDict, "found"))
                        {
                            int ox = Convert.ToInt32(pickDict["x"]);
                            int oy = Convert.ToInt32(pickDict["y"]);
                            await _cdp.ClickAtAsync(ox, oy, ct).ConfigureAwait(false);
                            appliedDrops++;
                        }
                        else
                        {
                            await _cdp.ClickAtAsync(tx, ty, ct).ConfigureAwait(false);
                        }
                        await Task.Delay(80, ct).ConfigureAwait(false);
                    }
                }
                return "inputs:" + appliedDrops;
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
                    if (creds.login) setNativeValue(visible[0], creds.login);
                    if (creds.school) setNativeValue(visible[1], creds.school);
                    filled = true;
                }
                return filled;
            })(" + argsJson + ")";

            object res = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
            return res is bool && (bool)res;
        }

        public async Task<string> ExtractDebugDomAsync(CancellationToken ct)
        {
            const string script = @"(function() {
                var root = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                return JSON.stringify({
                    url: location.href,
                    text: root ? (root.innerText || '').slice(0, 4000) : '',
                    html: root ? (root.outerHTML || '').slice(0, 14000) : ''
                });
            })()";
            object raw = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
            return Convert.ToString(raw) ?? string.Empty;
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
                        if (t === targets[k] || t.indexOf(targets[k]) >= 0) {
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

        private async Task<List<DragNode>> GetCurrentDragNodesAsync(CancellationToken ct)
        {
            const string script = @"(function() {
                function clean(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
                function isVisible(el) {
                    if (!el) return false;
                    var r = el.getBoundingClientRect();
                    return r.width > 12 && r.height > 12;
                }
                var root = document.querySelector('.problem__block-list') || document.querySelector('main') || document.body;
                var items = [];
                var all = root.querySelectorAll('div, span, li');
                for (var i = 0; i < all.length; i++) {
                    var el = all[i];
                    if (!isVisible(el)) continue;
                    var st = window.getComputedStyle(el);
                    var r = el.getBoundingClientRect();
                    var txt = clean(el.innerText);
                    if ((el.getAttribute('draggable') === 'true' || st.cursor === 'grab' || st.cursor === '-webkit-grab') && txt && txt.length <= 120 && r.width < 450 && r.height < 120) {
                        items.push({ index: items.length, label: txt, x: Math.round(r.x + r.width / 2), y: Math.round(r.y + r.height / 2) });
                    }
                }
                items.sort(function(a, b) {
                    if (Math.abs(a.y - b.y) > 10) return a.y - b.y;
                    return a.x - b.x;
                });
                for (var k = 0; k < items.length; k++) items[k].index = k;
                return JSON.stringify(items);
            })()";

            var nodes = new List<DragNode>();
            try
            {
                object raw = await _cdp.EvaluateAsync(script, ct).ConfigureAwait(false);
                var arr = _json.Deserialize<ArrayList>(Convert.ToString(raw));
                if (arr != null)
                {
                    foreach (object obj in arr)
                    {
                        var d = obj as Dictionary<string, object>;
                        if (d == null) continue;
                        nodes.Add(new DragNode
                        {
                            Index = Convert.ToInt32(d["index"]),
                            Label = GetString(d, "label"),
                            X = Convert.ToInt32(d["x"]),
                            Y = Convert.ToInt32(d["y"])
                        });
                    }
                }
            }
            catch
            {
            }
            return nodes;
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
                if (state.IsReorderList)
                {
                    sb.AppendLine("Элементы для расстановки в правильном порядке (сейчас сверху вниз / слева направо):");
                    foreach (DragNode d in state.DragItems)
                    {
                        sb.AppendFormat("- itemIndex={0}, text=\"{1}\"", d.Index, d.Label);
                        sb.AppendLine();
                    }
                }
                else
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
            }
            if (!string.IsNullOrEmpty(retryNote))
            {
                sb.AppendLine();
                sb.AppendLine("Примечание к прошлой попытке: " + retryNote);
            }
            sb.AppendLine();
            sb.AppendLine("Верни строго JSON одного из форматов:");
            sb.AppendLine("1) Для выбора вариантов (radio/checkbox): {\"type\":\"choice\",\"indices\":[0]}");
            sb.AppendLine("   - Если kind=radio: выбери ровно 1 индекс.");
            sb.AppendLine("   - Если kind=checkbox (квадратики): в Яндекс Учебнике обычно 2 и более верных ответа — обязательно укажи ВСЕ подходящие индексы (не меньше 2).");
            sb.AppendLine("2) Для ввода текста/чисел/кода/списков по порядку: {\"type\":\"inputs\",\"values\":[\"ответ\"]}");
            sb.AppendLine("   - Для kind=dropdown укажи в values точный текст выбранного пункта для каждого выпадающего списка по порядку.");
            sb.AppendLine("   - ВАЖНО для kind=code: поле value содержит ВЕСЬ код файла! Верни в values ПОЛНЫЙ обновлённый код всего файла целиком (сохранив <html>, <head>, <style>, <body> и прочий шаблон), а не только добавленный фрагмент.");
            if (state.IsReorderList)
            {
                sb.AppendLine("3) Для расстановки элементов списка в правильном порядке: {\"type\":\"reorder\",\"order\":[0,1,2]}");
                sb.AppendLine("   - В массиве order перечисли ВСЕ itemIndex в нужном итоговом порядке (от первого к последнему, сверху вниз).");
            }
            else
            {
                sb.AppendLine("3) Для перетаскивания карточек в зоны: {\"type\":\"drag\",\"moves\":[{\"itemIndex\":0,\"targetIndex\":0}]}");
            }
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
