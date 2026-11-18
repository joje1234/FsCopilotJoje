(function (root) {
    'use strict';
    function valid(value, count) {
        if (!value || typeof value.profile !== 'string' || value.profile.length > 64 || !/^[a-f0-9]*$/.test(value.profile) ||
            !Number.isSafeInteger(value.sequence) || value.sequence < 0 || !Array.isArray(value.catalog) || value.catalog.length > 16 ||
            !Array.isArray(value.changes) || value.changes.length > 12) return false;
        function index(row) { return row && Number.isInteger(row.index) && row.index >= 0 && row.index < count; }
        function text(value, size) { return typeof value === 'string' && value.length <= size && /^[\x20-\x7e]*$/.test(value); }
        return new Set(value.catalog.map(row => row && row.index)).size === value.catalog.length &&
            value.catalog.every(row => index(row) && text(row.name, 96) && row.name.length > 0 && (row.label === undefined || text(row.label, 96)) && text(row.units, 32) && typeof row.shared === 'boolean' && (row.value === null || Number.isFinite(row.value))) &&
            new Set(value.changes.map(row => row && row.index)).size === value.changes.length &&
            value.changes.every(row => index(row) && Number.isSafeInteger(row.sequence) && row.sequence > 0 && row.sequence <= value.sequence && Number.isFinite(row.before) && Number.isFinite(row.value));
    }
    class InstrumentMonitor {
        constructor(element) {
            this.element = element; this.catalog = new Map(); this.key = ''; this.open = false; this.paused = false; this.cleared = 0; this.rows = []; this.current = null;
            element.innerHTML = '<h2>Instrument sync monitor</h2><button type="button" data-monitor-action="open">Open monitor</button>' +
                '<div data-monitor-body hidden><p class="fsc-storage" data-monitor-status role="status"></p>' +
                '<p class="fsc-storage">Covered means configured for sharing. Confirming delivery requires both PCs. This monitor only reads values.</p>' +
                '<button type="button" data-monitor-action="pause">Pause activity</button> <button type="button" data-monitor-action="clear">Clear activity</button>' +
                '<h3>Recent instrument changes</h3><div data-monitor-changes></div>' +
                '<label>Find a covered instrument <input type="search" data-monitor-filter placeholder="Heading, altimeter, lights…" maxlength="96"></label>' +
                '<p class="fsc-storage" data-monitor-search-status></p><div data-monitor-matches></div>' +
                '<p class="fsc-storage">GTN screens stay local. An absent entry can also mean that the aircraft exposes no readable value.</p></div>';
        }
        find(selector) { return this.element.querySelector(selector); }
        update(snapshot, state) {
            var cockpit = snapshot && snapshot.cockpit, monitor = cockpit && cockpit.monitor;
            this.element.hidden = monitor === undefined;
            if (monitor === undefined) return;
            if (!state.online || !valid(monitor, cockpit.count)) {
                this.find('[data-monitor-status]').textContent = state.online ? 'Instrument monitor data is unavailable.' : 'Bridge disconnected. Displayed readings are historical.';
                return;
            }
            var key = snapshot.session + ':' + monitor.profile;
            if (key !== this.key) { this.key = key; this.catalog.clear(); this.cleared = 0; this.rows = []; }
            monitor.catalog.forEach(row => this.catalog.set(row.index, row));
            this.current = monitor;
            if (!this.paused) this.rows = monitor.changes.filter(row => row.sequence > this.cleared);
            this.find('[data-monitor-status]').textContent = (cockpit.sharing ? 'Shared connection active. Observing values on this PC. ' : 'Local coverage check. No peer delivery is being tested. ') +
                this.catalog.size + '/' + cockpit.count + ' definitions loaded.' + (this.paused ? ' Activity paused.' : '');
            this.paint();
        }
        label(row) { return (row.label || row.name).replace(/^[ALB]:/, '').replace(/_/g, ' '); }
        number(value) { return Number(value.toPrecision(8)).toString(); }
        paint() {
            var history = this.find('[data-monitor-changes]'); history.textContent = '';
            if (!this.rows.length) history.textContent = 'Turn a knob or change a switch. Observed changes in covered instruments appear here.';
            this.rows.forEach(change => {
                var entry = this.catalog.get(change.index), row = this.element.ownerDocument.createElement('p'); row.className = 'fsc-monitor-row';
                row.textContent = (entry ? this.label(entry) : 'Loading instrument #' + change.index) + ' · ' + this.number(change.before) + ' → ' + this.number(change.value) +
                    (entry ? ' ' + entry.units + ' · Covered · ' + (entry.shared ? 'Both pilots' : 'Host → copilot') : ''); history.appendChild(row);
            });
            var query = (this.find('[data-monitor-filter]').value || '').trim().toLowerCase();
            var matches = Array.from(this.catalog.values()).filter(row => (row.name + ' ' + (row.label || '') + ' ' + row.units).toLowerCase().includes(query));
            var list = this.find('[data-monitor-matches]'); list.textContent = '';
            this.find('[data-monitor-search-status]').textContent = !query ? 'Search the definitions loaded for this aircraft.' : matches.length ? matches.length + ' covered matches; showing up to 20.' : 'No covered match loaded. Try another name or allow the catalog to finish loading.';
            if (query) matches.slice(0, 20).forEach(entry => {
                var row = this.element.ownerDocument.createElement('p'); row.className = 'fsc-monitor-row';
                row.textContent = this.label(entry) + ' · ' + (entry.shared ? 'Both pilots' : 'Host → copilot') + ' · ' +
                    (entry.value === null ? 'Readout unavailable' : 'Last observed: ' + this.number(entry.value) + ' ' + entry.units);
                list.appendChild(row);
            });
        }
        click(target) {
            if (!target.hasAttribute('data-monitor-action')) return false;
            var action = target.getAttribute('data-monitor-action');
            if (action === 'open') { this.open = !this.open; this.find('[data-monitor-body]').hidden = !this.open; target.textContent = this.open ? 'Close monitor' : 'Open monitor'; }
            if (action === 'pause') { this.paused = !this.paused; target.textContent = this.paused ? 'Resume activity' : 'Pause activity'; }
            if (action === 'clear') { this.cleared = this.current ? this.current.sequence : 0; this.rows = []; }
            this.paint(); return true;
        }
    }
    root.FscControls = root.FscControls || {};
    root.FscControls.InstrumentMonitor = InstrumentMonitor;
    root.FscControls.validInstrumentMonitor = valid;
})(typeof window !== 'undefined' ? window : globalThis);
