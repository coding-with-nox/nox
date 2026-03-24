'use strict';
window.NoxFlowDesigner = (() => {
    let _dn = null, _svg = null, _cnv = null;
    let _drag = null;
    let _panning = false, _panStart = null;
    let _vp = { x: 50, y: 50, scale: 1 };
    let _selType = '', _selId = '';

    function svgPt(e) {
        const r = _svg.getBoundingClientRect();
        return {
            x: (e.clientX - r.left - _vp.x) / _vp.scale,
            y: (e.clientY - r.top  - _vp.y) / _vp.scale
        };
    }

    function applyVp() {
        if (_cnv) _cnv.setAttribute('transform',
            `translate(${_vp.x},${_vp.y}) scale(${_vp.scale})`);
    }

    function nEl(id) {
        return _cnv && _cnv.querySelector('[data-nid="' + CSS.escape(id) + '"]');
    }

    // Apply selection highlight directly in DOM — no Blazor re-render needed
    function applySelDom() {
        if (!_cnv) return;
        _cnv.querySelectorAll('[data-selected]').forEach(el => el.removeAttribute('data-selected'));
        if (!_selId) return;
        const el = _selType === 'node'
            ? nEl(_selId)
            : _cnv.querySelector('[data-eid="' + CSS.escape(_selId) + '"]');
        if (el) el.setAttribute('data-selected', 'true');
    }

    function refreshEdges() {
        if (!_cnv) return;
        _cnv.querySelectorAll('[data-eid]').forEach(g => {
            const fn = nEl(g.dataset.from);
            const tn = nEl(g.dataset.to);
            if (!fn || !tn) return;
            const fx = parseFloat(fn.dataset.x) + parseFloat(fn.dataset.pw);
            const fy = parseFloat(fn.dataset.y) + parseFloat(fn.dataset.ph);
            const tx = parseFloat(tn.dataset.x);
            const ty = parseFloat(tn.dataset.y) + parseFloat(tn.dataset.ph);
            const dx = Math.max(60, Math.abs(tx - fx) * 0.5);
            const d = `M${fx},${fy} C${fx+dx},${fy} ${tx-dx},${ty} ${tx},${ty}`;
            g.querySelectorAll('path').forEach(p => p.setAttribute('d', d));
        });
    }

    function onDown(e) {
        const nodeG = e.target.closest('[data-nid]');
        const edgeG = e.target.closest('[data-eid]');

        if (nodeG) {
            e.stopPropagation();
            // Apply visual selection immediately in JS — no stutter
            _selType = 'node'; _selId = nodeG.dataset.nid;
            applySelDom();
            // Notify Blazor async (props panel update only)
            _dn.invokeMethodAsync('JsSel', 'node', nodeG.dataset.nid);
            const pt = svgPt(e);
            _drag = {
                id: nodeG.dataset.nid, el: nodeG,
                sx: pt.x, sy: pt.y,
                ox: parseFloat(nodeG.dataset.x),
                oy: parseFloat(nodeG.dataset.y)
            };
            return;
        }

        if (edgeG) {
            e.stopPropagation();
            _selType = 'edge'; _selId = edgeG.dataset.eid;
            applySelDom();
            _dn.invokeMethodAsync('JsSel', 'edge', edgeG.dataset.eid);
            return;
        }

        _selType = ''; _selId = '';
        applySelDom();
        _dn.invokeMethodAsync('JsSel', '', '');
        _panning = true;
        _panStart = { x: e.clientX - _vp.x, y: e.clientY - _vp.y };
    }

    function onMove(e) {
        if (_drag) {
            const pt = svgPt(e);
            const nx = Math.round(_drag.ox + pt.x - _drag.sx);
            const ny = Math.round(_drag.oy + pt.y - _drag.sy);
            _drag.el.setAttribute('transform', `translate(${nx},${ny})`);
            _drag.el.dataset.x = nx;
            _drag.el.dataset.y = ny;
            refreshEdges();
            return;
        }
        if (_panning && _panStart) {
            _vp.x = e.clientX - _panStart.x;
            _vp.y = e.clientY - _panStart.y;
            applyVp();
        }
    }

    function onUp(e) {
        if (_drag) {
            _dn.invokeMethodAsync('JsMoved',
                _drag.id,
                parseInt(_drag.el.dataset.x),
                parseInt(_drag.el.dataset.y));
            _drag = null;
        }
        _panning = false;
    }

    function onWheel(e) {
        e.preventDefault();
        const f = e.deltaY < 0 ? 1.1 : 0.9;
        const r = _svg.getBoundingClientRect();
        const mx = e.clientX - r.left, my = e.clientY - r.top;
        _vp.x = mx - (mx - _vp.x) * f;
        _vp.y = my - (my - _vp.y) * f;
        _vp.scale = Math.max(0.15, Math.min(3, _vp.scale * f));
        applyVp();
    }

    return {
        init(dotnetRef, svgId) {
            _dn = dotnetRef;
            _svg = document.getElementById(svgId);
            if (!_svg) return;
            _cnv = _svg.querySelector('#cnv');
            _vp = { x: 50, y: 50, scale: 1 };
            applyVp();
            _svg.addEventListener('mousedown', onDown);
            window.addEventListener('mousemove', onMove);
            window.addEventListener('mouseup', onUp);
            _svg.addEventListener('wheel', onWheel, { passive: false });
        },
        dispose() {
            if (_svg) {
                _svg.removeEventListener('mousedown', onDown);
                _svg.removeEventListener('wheel', onWheel);
            }
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
            _dn = null; _svg = null; _cnv = null;
            _selType = ''; _selId = '';
        },
        fitView() { _vp = { x: 50, y: 50, scale: 1 }; applyVp(); },
        // Called by Blazor after every re-render to resync visual selection
        applySelection(type, id) { _selType = type; _selId = id; applySelDom(); }
    };
})();
