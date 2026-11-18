(function (root) {
    'use strict';
    var ns = 'http://www.w3.org/2000/svg';
    var pairs = ['throttle', 'propeller', 'mixture'];
    var groups = ['steering', 'quadrant'].concat(pairs);
    var titles = { steering: 'Steering', quadrant: 'Engine quadrant', throttle: 'Throttle', propeller: 'RPM', mixture: 'Mixture' };
    function toleranceName(percent) { return percent === 10 ? 'Tight' : percent === 20 ? 'Medium' : percent === 30 ? 'Loose' : 'Custom'; }
    var extraAxes = [];
    var detailRadius = 6; // Half the default 12-unit corner radius.
    var cockpit = '<svg viewBox="0 0 900 680" role="group" aria-label="Cockpit control assignment" data-cockpit><g data-cockpit-body>' +
        '<path class="fsc-shell" d="M70 638V160C70 58 230 24 450 24S830 58 830 160V638A12 12 0 0 1 818 650H82A12 12 0 0 1 70 638Z"/>' +
        '<path class="fsc-window" d="M130 119Q203 55 437 47V108L273 135Z M463 47Q697 55 770 119L627 135L463 108Z"/>' +
        '<rect class="fsc-panel" x="112" y="151" width="676" height="71" rx="12"/>' +
        '<g data-extra-axes></g>' +
        '<g class="fsc-control fsc-unconfirmed" data-control="steering" role="button" tabindex="0" aria-disabled="true" aria-label="Assign steering to Me">' +
        '<title>Steering includes both yokes, rudder pedals, nosewheel and toe brakes. Trims are shared separately. Select a seat, then tap a yoke or pedal.</title>' +
        '<rect class="fsc-hit fsc-yoke-hit" x="137" y="244" width="186" height="199" rx="12"/>' +
        '<rect class="fsc-hit fsc-yoke-hit" x="577" y="244" width="186" height="199" rx="12"/>' +
        [230, 670].map(function (x) {
            return '<g class="fsc-pedals" transform="translate(' + x + ' 266)" data-pedal-station>' +
                '<g data-rudder-pair>' + ['left','right'].map(function (side,index) {
                    var px=index ? 10 : -34;
                    return '<rect class="fsc-pedal-base" x="'+px+'" y="0" width="24" height="40" rx="'+detailRadius+'"/>' +
                        '<rect class="fsc-pedal-reference" data-pedal-target="'+side+'" x="'+px+'" y="0" width="24" height="40" rx="'+detailRadius+'"/>' +
                        '<rect class="fsc-pedal-input" data-pedal-input="'+side+'" x="'+px+'" y="0" width="24" height="40" rx="'+detailRadius+'"/>';
                }).join('') + '<title data-rudder-title></title></g>' +
                '<g data-brake-graphic="leftBrake" transform="translate(-57 0)"></g>' +
                '<g data-brake-graphic="rightBrake" transform="translate(57 0)"></g></g>';
        }).join('') +
        [230, 670].map(function (x) {
            var path = '<path d="M-43 -26L-46 4Q0 22 46 4L43 -26M0 13V40"/>' +
                '<rect class="fsc-yoke-hub" x="-14" y="-1" width="28" height="28" rx="'+detailRadius+'"/>';
            return '<g transform="translate(' + x + ' 364)"><g class="fsc-yoke-base">' + path + '</g><g class="fsc-yoke-target" data-yoke-target>' + path + '</g>' +
                '<g class="fsc-yoke-input" data-yoke-input>' + path + '</g></g>';
        }).join('') +
        '<text class="fsc-group-label" x="230" y="466">STEERING</text>' +
        '<text class="fsc-group-owner" x="225" y="368" data-owner="steering">Unconfirmed</text>' +
        '<text class="fsc-group-summary" x="535" y="347" data-summary="steering">Waiting for simulator</text>' +
        '<text class="fsc-group-summary" x="535" y="368" data-hint="steering"></text>' +
        '<g data-roll-graphic transform="translate(181 426)"></g>' +
        '<g data-pitch-graphic transform="translate(621 426)"></g></g>' +
        '<g class="fsc-control fsc-unconfirmed" data-control="quadrant" role="button" tabindex="0" aria-disabled="true" aria-label="Assign engine quadrant to Me">' +
        '<title>Throttle, RPM and mixture are separate controls. Select Me to request control; double-click a pair to force takeover.</title>' +
        '<rect class="fsc-quadrant-shell" x="366" y="250" width="168" height="236" rx="12"/>' +
        '<g data-quadrant-axes></g>' +
        '<text class="fsc-group-label" x="450" y="510">QUADRANT</text>' +
        '<text class="fsc-group-owner" x="380" y="468" data-owner="quadrant">Unconfirmed</text>' +
        '<text class="fsc-group-summary" x="380" y="490" data-summary="quadrant">Waiting for simulator</text>' +
        '<text class="fsc-group-summary" x="380" y="510" data-hint="quadrant"></text></g>' +
        '<g class="fsc-seat fsc-seat-me" data-seat-choice="me" role="button" tabindex="0" aria-pressed="true" aria-label="Select Me for control assignment">' +
        '<rect class="fsc-seat-back" x="155" y="493" width="150" height="113" rx="12"/><rect x="147" y="568" width="166" height="45" rx="12"/>' +
        '<text class="fsc-seat-name" x="230" y="538">ME</text><text class="fsc-seat-sub" x="230" y="560">LEFT PILOT</text>' +
        '<text class="fsc-seat-selected" x="230" y="598">TAKE CONTROL</text></g>' +
        '<g class="fsc-seat fsc-seat-mock" data-seat-choice="mock" role="button" tabindex="0" aria-pressed="false" aria-label="Select Mock copilot for control assignment">' +
        '<rect class="fsc-seat-back" x="595" y="493" width="150" height="113" rx="12"/><rect x="587" y="568" width="166" height="45" rx="12"/>' +
        '<text class="fsc-seat-name" x="670" y="538">MOCK</text><text class="fsc-seat-sub" x="670" y="560">COPILOT</text>' +
        '<text class="fsc-seat-selected" x="670" y="598">ASSIGN</text></g>' +
        [['steering', 'Steering', 150, 355], ['quadrant', 'Engine quadrant', 380, 544]].map(function (entry) {
            return '<g class="fsc-group-actions" data-menu-for="' + entry[0] + '" transform="translate(' + entry[2] + ' ' + entry[3] + ')" role="button" tabindex="-1" aria-disabled="true" aria-haspopup="menu" aria-expanded="false" aria-label="' + entry[1] + ' actions">' +
                '<title>' + entry[1] + ' actions: Force takeover</title><rect x="-18" y="-16" width="36" height="32" rx="12"/>' +
                '<circle cx="-8" cy="0" r="2"/><circle cx="0" cy="0" r="2"/><circle cx="8" cy="0" r="2"/></g>';
        }).join('') +
        '</g></svg>';

    function settings(group, title) {
        return '<section class="fsc-settings-group" data-settings-group="' + group + '"><h2>' + title + '</h2>' +
            '<div class="fsc-selector"><button type="button" data-group="' + group + '" data-owner-choice="me" disabled>Me</button>' +
            '<button type="button" data-group="' + group + '" data-owner-choice="mock" disabled>Mock copilot</button></div>' +
            '<div class="fsc-pickup-settings"><label>Tolerance <select data-tolerance="' + group + '" disabled>' +
            (group === 'quadrant' ? '<option value="mixed" disabled hidden>Per pair</option>' : '') +
            [1, 2, 3, 5, 10, 20, 30].map(value => '<option value="' + value + '"' + (value === 10 ? ' selected' : '') + (value < 10 ? ' hidden' : '') + '>' + (value < 10 ? 'Custom ±' + value + '%' : toleranceName(value)) + '</option>').join('') +
            '</select></label><button type="button" data-learn="' + group + '" disabled>Relearn inputs</button></div>' +
            '<p class="fsc-pickup-hint" data-pickup-hint="' + group + '"></p>' +
            '<p class="fsc-activity">Seen <strong data-seen="' + group + '">0</strong> · Blocked <strong data-blocked="' + group + '">0</strong></p>' +
            '<p class="fsc-last" data-last="' + group + '"></p><div data-axis-list="' + group + '"></div>' + (group === 'steering' ?
            '<div data-force-button hidden><h3>Force takeover button</h3><p class="fsc-storage">Choose a dedicated hardware button. It takes Steering immediately, including when the tablet is closed.</p>' +
            '<label>Controller <select data-force-device></select></label> <button type="button" data-force-binding="listen">Assign button</button> ' +
            '<button type="button" data-force-binding="cancel" hidden>Cancel assignment</button> <button type="button" data-force-binding="clear">Remove binding</button>' +
            '<p class="fsc-storage" data-force-binding-status role="status" aria-live="polite"></p></div>' : '') + '</section>';
    }
    function connectionChoices(key, title, choices) {
        return '<div class="fsc-connection-choice-field"><span>' + title + '</span>' +
            '<input type="hidden" data-connection-field="' + key + '">' +
            '<div class="fsc-connection-choices" role="group" aria-label="' + title + '">' +
            choices.map(choice => '<button type="button" data-connection-choice="' + key + '" data-choice-value="' + choice[0] + '" aria-pressed="false">' + choice[1] + '</button>').join('') + '</div></div>';
    }
    function mount(element, controller, options) {
        options = options || {};
        var selected = null;
        var resetConfirm = false;
        var assignmentRequests = new Map(), cancelledRequests = new Set(), flushingCancellation = false, queuedForce = null;
        var connectionRevision = -1;
        var connectionDirty = false;
        var localCode = Math.floor(Math.random() * 4294967296).toString(16).padStart(8, '0');
        var menuGroup = null, menuSession = null, menuAnchor = null;
        var hovered = null, heldPress = null, holdTimer = null, suppressedClick = null;
        element.innerHTML = '<div class="fsc-controls"><div class="fsc-overview">' +
            (options.preview ? '<span class="fsc-preview">PREVIEW · DISCONNECTED</span>' : '') + cockpit + '</div>' +
            '<div class="fsc-cancel-row"><button type="button" data-cancel="steering" hidden>Cancel steering takeover</button>' +
            '<button type="button" data-cancel="quadrant" hidden>Cancel quadrant takeover</button>' +
            pairs.map(group => '<button type="button" data-cancel="' + group + '" hidden>Cancel ' + titles[group] + '</button>').join('') + '</div>' +
            '<p class="fsc-error" data-error role="alert" hidden></p>' +
            '<footer class="fsc-footer"><span class="fsc-connection" data-connection>Waiting for bridge</span>' +
            '<span class="fsc-assignment" data-assignment>Assign to Me · tap controls</span>' +
            '<span class="fsc-legend"><i></i> Hardware <b></b> Cockpit</span>' +
            '<button type="button" data-cycle-tolerance disabled title="Applies to all controls: Tight ±10%, Medium ±20%, Loose ±30%">Tolerance</button>' +
            '<button type="button" data-settings aria-expanded="false">Setup</button></footer>' +
            '<div class="fsc-context-menu" data-control-menu role="menu" aria-label="Control actions" hidden>' +
            '<span data-menu-title></span><button type="button" data-force role="menuitem">Force takeover</button>' +
            '<button type="button" data-check-output role="menuitem" hidden>Check shared output</button>' +
            '<button type="button" data-auto role="menuitemcheckbox" aria-checked="false" hidden>Auto takeover: off</button>' +
            '<button type="button" data-advanced role="menuitem" hidden>Advanced engine controls</button>' +
            pairs.map(group => '<button type="button" data-menu-pair="' + group + '" role="menuitem" hidden>' + titles[group] + ' actions…</button>').join('') +
            '<small data-menu-help hidden></small></div>' +
            '<section class="fsc-setup" data-setup role="dialog" aria-label="Control setup and diagnostics" hidden>' +
            '<div class="fsc-setup-header"><h2>Setup</h2><button type="button" data-close-settings>Close</button></div>' +
            '<div class="fsc-setup-body" data-setup-scroll tabindex="0" aria-label="Scrollable control settings">' +
            '<p class="fsc-note" data-output-check hidden></p>' +
            '<p data-auto-recovery hidden>Automatic quadrant takeover is paused for recovery. <button type="button" data-resume-auto>Resume automatic takeover</button></p>' +
            '<section class="fsc-wizard" data-wizard hidden aria-label="Shared output setup wizard">' +
            '<button type="button" data-wizard-start>Setup wizard</button>' +
            '<button type="button" data-setup-reset hidden>Reset setup</button>' +
            '<p class="fsc-storage" data-wizard-ready></p><div class="fsc-wizard-results" data-wizard-results></div>' +
            '<p class="fsc-storage" data-setup-reset-reason hidden></p>' +
            '<div data-setup-reset-confirm hidden><p>Clear learned controls and saved checks for this aircraft? Connection settings, tolerance and your Force button are kept. Controls stay on Me.</p>' +
            '<button type="button" data-setup-reset-yes>Clear setup</button> <button type="button" data-setup-reset-no>Cancel</button></div>' +
            '<h3 data-wizard-title hidden></h3><p data-wizard-instruction role="status" aria-live="polite"></p>' +
            '<div class="fsc-wizard-actions">' +
            '<button type="button" data-wizard-cancel hidden>Cancel wizard</button>' +
            '<button type="button" data-wizard-force hidden>Force takeover this group</button></div><div data-wizard-axes></div></section>' +
            '<section class="fsc-shared-settings" data-connection-settings hidden><h2>Shared cockpit</h2>' +
            '<div class="fsc-connection-fields">' +
            '<label>Your code<input data-connection-field="local" maxlength="8" spellcheck="false" autocomplete="off" aria-label="Your connection code"></label>' +
            '<label>Friend’s code<input data-connection-field="peer" maxlength="8" spellcheck="false" autocomplete="off" aria-label="Friend connection code"></label>' +
            connectionChoices('role', 'Flight', [[1, 'Host flight'], [2, 'Join flight']]) +
            connectionChoices('seat', 'Your seat', [[1, 'Pilot · left'], [2, 'Copilot · right']]) +
            '</div><p class="fsc-storage">The host runs the aircraft physics. Either person can use either seat.</p>' +
            '<details><summary>Server</summary><div class="fsc-connection-fields">' +
            '<label>Hostname or IPv4<input data-connection-field="relay" maxlength="127" spellcheck="false" autocomplete="off"></label>' +
            '<label>Port<input data-connection-field="port" inputmode="numeric" maxlength="5" autocomplete="off"></label></div></details>' +
            '<p class="fsc-storage" data-connection-validation></p><button type="button" data-save-connection disabled>Save connection</button>' +
            '<p class="fsc-storage" data-backend-status role="status" aria-live="polite"></p></section>' +
            '<p class="fsc-storage" data-shared-status hidden></p><button type="button" data-shared-session hidden disabled>Join shared session</button>' +
            '<section data-cockpit-recovery hidden><h2>Cockpit synchronization</h2><button type="button" data-resync-cockpit disabled>Resync cockpit from host</button>' +
            '<p class="fsc-storage" data-recovery-status role="status" aria-live="polite"></p></section>' +
            '<section class="fsc-settings-group" data-instrument-monitor hidden></section>' +
            '<div data-seat-swap hidden><p class="fsc-storage" data-seat-status role="status" aria-live="polite"></p>' +
            '<button type="button" data-seat-action="request">Swap seats</button>' +
            '<button type="button" data-seat-action="accept" hidden>Accept seat swap</button> ' +
            '<button type="button" data-seat-action="decline" hidden>Decline</button>' +
            '<button type="button" data-seat-action="cancel" hidden>Cancel seat swap</button></div>' +
            '<p class="fsc-storage" data-camera-status role="status" aria-live="polite" hidden></p>' +
            '<section data-altimeters hidden><h2 data-altimeter-title>Altimeters · local</h2><p class="fsc-storage" data-altimeter-status role="status" aria-live="polite">Synchronization unavailable.</p>' +
            '<p class="fsc-storage" data-altimeter="0"></p><p class="fsc-storage" data-altimeter="1"></p>' +
            '<button type="button" data-instruments-local hidden disabled>Use local altimeters</button></section>' +
            '<section class="fsc-settings-group" data-navigation><h2>Flight plan</h2>' +
            '<p class="fsc-storage">GTN screens and touches stay local. Route copying is not enabled yet.</p>' +
            '<p class="fsc-storage">The supported GTN setup requires PMS50 GTN750 Premium activated on both PCs and working MSFS 2024 EFB route integration.</p>' +
            '<p class="fsc-storage">Both pilots may use the shared AP buttons and selectors. Navigation guidance currently comes from the flight host. Selecting the other pilot\'s GTN is not enabled yet.</p>' +
            '<button type="button" data-read-avionics-route disabled>Read avionics route</button>' +
            '<p class="fsc-storage" data-navigation-status role="status" aria-live="polite"></p>' +
            '<p class="fsc-storage" data-navigation-summary></p></section>' +
            '<p class="fsc-storage" data-mixture-reference></p>' +
            '<p class="fsc-storage" data-learning-storage></p>' + groups.map(group => settings(group, titles[group])).join('') +
            '<p class="fsc-note">Select Me, then double-click a control to force takeover. Right-click or press and hold a control for its Force menu. Force bypasses alignment for that group. Mock copilot is a local test.</p></div></section></div>';

        var instrumentMonitor = new root.FscControls.InstrumentMonitor(element.querySelector('[data-instrument-monitor]'));
        function setText(node, value) { var next = String(value); if (node.textContent !== next) node.textContent = next; }
        function text(selector, value) { setText(element.querySelector(selector), value); }
        function connectionDraft() {
            var value = {};
            element.querySelectorAll('[data-connection-field]').forEach(field => {
                var key = field.getAttribute('data-connection-field');
                value[key] = ['port', 'role', 'seat'].includes(key) ? Number(field.value) : field.value.trim();
            });
            return value;
        }
        function renderConnection(snapshot, state) {
            var settings = snapshot && snapshot.connection;
            var section = element.querySelector('[data-connection-settings]'); section.hidden = !settings;
            if (!settings) { connectionRevision = -1; connectionDirty = false; return; }
            if (connectionRevision !== settings.revision) {
                var source = settings.revision ? settings : { relay: 'p2p.fscopilot.com', port: 3600, local: localCode, peer: '', role: 1, seat: 1 };
                element.querySelectorAll('[data-connection-field]').forEach(field => {
                    var value = String(source[field.getAttribute('data-connection-field')]);
                    if (field.tagName.toLowerCase() === 'select') {
                        var choices = Array.from(field.querySelectorAll('option'));
                        choices.forEach(option => { option.selected = false; });
                        var chosen = choices.find(option => option.value === value); if (chosen) chosen.selected = true;
                    }
                    else field.value = value;
                });
                connectionRevision = settings.revision;
            }
            var locked = !state.usable || snapshot.shared && ([1, 2, 3].includes(snapshot.shared.phase) || snapshot.shared.holding);
            element.querySelectorAll('[data-connection-field]').forEach(field => { field.disabled = !!locked; });
            element.querySelectorAll('[data-connection-choice]').forEach(button => {
                var field = element.querySelector('[data-connection-field="' + button.getAttribute('data-connection-choice') + '"]');
                button.disabled = !!locked;
                button.setAttribute('aria-pressed', String(field.value === button.getAttribute('data-choice-value')));
            });
            var draft = connectionDraft();
            var valid = root.FscControls.validConnectionSettings(draft);
            connectionDirty = !settings.revision || Object.keys(draft).some(key => draft[key] !== settings[key]);
            element.querySelector('[data-save-connection]').disabled = !!locked || !valid;
            text('[data-connection-validation]', locked && snapshot.shared && snapshot.shared.holding ? 'Recover local controls before changing the connection.' :
                valid ? connectionDirty ? 'Save these changes before joining.' : 'Both players use the same server and each other’s codes. Join below when ready.' : 'Use two different eight-character lowercase codes, a hostname or IPv4, and a port from 1 to 65535.');
            var messages = ['Start the FS Copilot EFB helper on this PC.', 'Save your connection settings.', 'Helper ready. Join shared controls below.',
                'Connecting to the other pilot…', 'Confirming control ownership…', 'Shared cockpit connected.', 'Session ended. Recover local controls, then join again.', 'Connection unavailable.'];
            var codes = ['', 'Shared controls are not enabled in this build. Local control testing remains available.', 'Learn the required controls while Me is active, then join again.',
                'Could not connect. Check the server and your friend’s code.', 'Session ended. Recover local controls, then join again.', 'Waiting for the simulator and its control bridge.', 'Save the connection again before joining.'];
            text('[data-backend-status]', codes[settings.code] || messages[settings.backend]);
        }
        function controlElement(group) { return element.querySelector('[data-control="' + group + '"], [data-pair-group="' + group + '"]'); }
        function svg(parent, name, attributes) {
            var node = element.ownerDocument.createElementNS(ns, name);
            Object.keys(attributes || {}).forEach(key => node.setAttribute(key, attributes[key]));
            parent.appendChild(node); return node;
        }
        function markGraphic(parent, id, label, length, vertical) {
            var row = svg(parent, 'g', { 'data-axis-graphic': id, 'data-length': length, 'data-vertical': vertical ? 'true' : 'false' });
            var caption = svg(row, 'text', { 'class': 'fsc-axis-caption', x: vertical ? 0 : length / 2, y: -10 }); caption.textContent = label;
            svg(row, 'rect', { 'class': 'fsc-graphic-track', x: vertical ? -3 : 0, y: 0, width: vertical ? 6 : length, height: vertical ? length : 5, rx: 3 });
            svg(row, 'rect', { 'class': 'fsc-graphic-band', 'data-band': '' });
            svg(row, 'line', { 'class': 'fsc-graphic-target', 'data-target': '' });
            svg(row, 'circle', { 'class': 'fsc-graphic-input', r: 5, 'data-input': '' });
            svg(row, 'text', { 'class': 'fsc-graphic-state', 'data-graphic-state': '', x: vertical ? 0 : length + 12, y: vertical ? length + 21 : 7 });
            svg(row, 'title', { 'data-graphic-title': '' });
            return row;
        }
        markGraphic(element.querySelector('[data-roll-graphic]'), 'roll', 'ROLL', 98, false);
        markGraphic(element.querySelector('[data-pitch-graphic]'), 'pitch', 'PITCH', 98, false);
        element.querySelectorAll('[data-brake-graphic]').forEach(function (holder) {
            markGraphic(holder, holder.getAttribute('data-brake-graphic'), '', 40, true);
        });
        extraAxes.forEach(function (axis, index) {
            var holder = svg(element.querySelector('[data-extra-axes]'), 'g', { transform: 'translate(' + (177 + index * 60) + ' 179)', 'data-extra': axis[0] });
            markGraphic(holder, axis[0], axis[1], 40, false);
        });
        var quadrantKey = '';
        function quadrantGraphics(axes) {
            var engines = Math.max(0, ...axes.map(function (axis) { return /[1-4]$/.test(axis.id) ? Number(axis.id.slice(-1)) : 0; })) || 2;
            var key = String(engines);
            if (quadrantKey === key) return;
            quadrantKey = key;
            var parent = element.querySelector('[data-quadrant-axes]'); parent.textContent = '';
            var rows = engines > 2 ? 2 : 1;
            pairs.forEach(function (kind, type) {
                var center = 450 + (type - 1) * 52;
                var pair = svg(parent, 'g', { 'data-pair-group': kind });
                svg(pair, 'rect', { 'class': 'fsc-pair-hit fsc-hit', x: center - 25, y: 260, width: 50, height: 217, rx: 12 });
                for (var engineRow = 0; engineRow < rows; engineRow++) {
                var columns = Math.min(2, engines - engineRow * 2);
                    for (var engineColumn = 0; engineColumn < columns; engineColumn++) {
                        var engine = engineRow * 2 + engineColumn + 1;
                        var x = center + (engineColumn ? 13 : -13);
                        var holder = svg(pair, 'g', { transform: 'translate(' + x + ' ' + (rows === 1 ? 286 : 281 + engineRow * 86) + ')' });
                        markGraphic(holder, kind + engine, kind[0].toUpperCase() + engine, rows === 1 ? 134 : 48, true);
                    }
                }
                var owner = svg(pair, 'text', { 'class': 'fsc-pair-owner', x: center, y: 455, 'data-pair-owner': kind });
                owner.textContent = 'Me';
                var mode = svg(pair, 'text', { 'class': 'fsc-pair-mode', x: center, y: 472, 'data-pair-mode': kind });
                mode.textContent = 'MANUAL';
            });
        }
        function requiresAlignment(axis) {
            return !axis || ['rudder','steering','leftBrake','rightBrake','elevatorTrim','aileronTrim','rudderTrim'].indexOf(axis.id) < 0;
        }
        function reason(axis, known, blocked) {
            if (!known) return 'Unavailable';
            if (!requiresAlignment(axis)) return 'Transfers with Steering · no alignment needed';
            if (axis && axis.relative) return !blocked ? 'Relative input · no learning needed' : axis.aligned ?
                'Ready · relative input' : axis.id === 'rudder' ? 'Center cockpit rudder for takeover' : 'Release cockpit brake for takeover';
            if (axis && axis.fixed && axis.input === null) return 'Standard input · move control to read position';
            if (axis && axis.saved) return 'Saved: move control to read position';
            if (!axis || !axis.calibrated) return blocked ? 'Not learned: return to Me' : 'Learning: pause at two positions';
            if (axis.input === null) return 'Input outside learned range';
            if (!axis.fresh) return 'Move axis to refresh';
            if (axis.target === null) return 'Waiting for cockpit position';
            return aligned(axis, known) ? 'Aligned' : axis.input < axis.target ? 'Increase input ↑' : 'Decrease input ↓';
        }
        function aligned(axis, known) {
            if (known && axis && !requiresAlignment(axis)) return true;
            return !!(known && axis && axis.calibrated && axis.fresh && (axis.relative || axis.input !== null) && axis.target !== null && axis.aligned);
        }
        function renderGraphic(row, axis, tolerance, known, blocked) {
            var length = Number(row.getAttribute('data-length'));
            var vertical = row.getAttribute('data-vertical') === 'true';
            var matched = aligned(axis, known);
            row.setAttribute('class', 'fsc-axis-graphic' + (matched ? ' fsc-axis-aligned' : ''));
            var band = row.querySelector('[data-band]');
            var target = row.querySelector('[data-target]');
            var input = row.querySelector('[data-input]');
            var targetValid = !!(known && axis && axis.target !== null);
            var inputValid = !!(known && axis && axis.calibrated && axis.fresh && axis.input !== null);
            band.setAttribute('visibility', targetValid && requiresAlignment(axis) ? 'visible' : 'hidden');
            target.setAttribute('visibility', targetValid ? 'visible' : 'hidden');
            input.setAttribute('visibility', inputValid ? 'visible' : 'hidden');
            if (targetValid) {
                var low = Math.max(0, axis.target - tolerance) / 100;
                var high = Math.min(100, axis.target + tolerance) / 100;
                if (vertical) {
                    band.setAttribute('x', -7); band.setAttribute('y', (1 - high) * length); band.setAttribute('width', 14); band.setAttribute('height', (high - low) * length);
                    target.setAttribute('x1', -10); target.setAttribute('x2', 10); target.setAttribute('y1', (1 - axis.target / 100) * length); target.setAttribute('y2', (1 - axis.target / 100) * length);
                } else {
                    band.setAttribute('x', low * length); band.setAttribute('y', -3); band.setAttribute('width', (high - low) * length); band.setAttribute('height', 11);
                    target.setAttribute('x1', axis.target / 100 * length); target.setAttribute('x2', axis.target / 100 * length); target.setAttribute('y1', -5); target.setAttribute('y2', 10);
                }
            }
            if (inputValid) {
                input.setAttribute('cx', vertical ? 0 : axis.input / 100 * length);
                input.setAttribute('cy', vertical ? (1 - axis.input / 100) * length : 2.5);
            }
            var symbol = !known ? '—' : !requiresAlignment(axis) ? '' : axis && axis.saved ? '…' : !axis || !axis.calibrated ? '?' :
                axis.relative ? matched ? '' : '!' : !inputValid || !targetValid ? '!' : matched ? '' : axis.input < axis.target ? '↑' : '↓';
            row.querySelector('[data-graphic-state]').textContent = symbol;
            row.querySelector('[data-graphic-title]').textContent = (axis ? axis.label + ': ' : '') + reason(axis, known, blocked) +
                (axis && axis.input !== null && axis.target !== null ? ' · hardware ' + axis.input.toFixed(1) + '%, cockpit ' + axis.target.toFixed(1) + '%' : '');
        }
        function renderDiagnostics(group, pickup, known, blocked) {
            var list = element.querySelector('[data-axis-list="' + group + '"]');
            var present = new Set();
            var diagnosticAxes = pickup.axes.slice();
            if (group === 'steering') {
                [['roll', 'Yoke roll'], ['pitch', 'Yoke pitch'], ['rudder', 'Rudder'], ['leftBrake', 'Left brake'], ['rightBrake', 'Right brake']].forEach(function (entry) {
                    if (!diagnosticAxes.some(axis => axis.id === entry[0])) diagnosticAxes.push({ id: entry[0], label: entry[1],
                        input: null, target: null, calibrated: false, fresh: false, aligned: false, undetected: true });
                });
                var order = ['roll', 'pitch', 'rudder', 'leftBrake', 'rightBrake'];
                diagnosticAxes.sort((a, b) => (order.includes(a.id) ? order.indexOf(a.id) : 99) - (order.includes(b.id) ? order.indexOf(b.id) : 99));
            }
            diagnosticAxes.forEach(function (axis) {
                present.add(axis.id);
                var row = list.querySelector('[data-axis="' + axis.id + '"]');
                if (!row) {
                    row = element.ownerDocument.createElement('div'); row.setAttribute('data-axis', axis.id);
                    row.innerHTML = '<strong data-label></strong><span data-axis-state></span><small data-axis-values></small>'; list.appendChild(row);
                }
                row.className = 'fsc-axis' + (aligned(axis, known) ? ' fsc-axis-aligned' : '');
                setText(row.querySelector('[data-label]'), axis.label);
                setText(row.querySelector('[data-axis-state]'), axis.undetected ? !known ? 'Waiting for simulator' :
                    blocked ? 'Input not detected · return to Me to set up' : axis.id === 'leftBrake' || axis.id === 'rightBrake' ?
                        'Input not detected · move pedal or press brake button' : 'Input not detected · move rudder control' : reason(axis, known, blocked));
                setText(row.querySelector('[data-axis-values]'), (axis.relative ? 'Local demand ' : 'Hardware ') + (axis.input === null ? '?' : axis.input.toFixed(1) + '%') + ' · Cockpit ' + (axis.target === null ? '?' : axis.target.toFixed(1) + '%'));
            });
            Array.from(list.children).forEach(row => { if (!present.has(row.getAttribute('data-axis'))) row.remove(); });
            diagnosticAxes.forEach(function (axis, index) {
                var row = list.querySelector('[data-axis="' + axis.id + '"]');
                if (list.children[index] !== row) list.insertBefore(row, list.children[index] || null);
            });
        }
        function renderWizard(snapshot, state) {
            var wizard = snapshot && snapshot.wizard;
            var panel = element.querySelector('[data-wizard]'); panel.hidden = !wizard;
            if (!wizard) return;
            var active = wizard.phase >= 1 && wizard.phase <= 5;
            var title = titles[wizard.group], check = snapshot.outputCheck;
            var start = element.querySelector('[data-wizard-start]'); start.hidden = active;
            start.disabled = !state.online || !wizard.ready || !!state.pending;
            setText(start, wizard.phase === 7 ? 'Retry setup wizard' : 'Setup wizard');
            var reset = element.querySelector('[data-setup-reset]'); reset.hidden = wizard.resetReady === undefined;
            reset.disabled = !state.online || !wizard.resetReady || !!state.pending;
            var confirm = element.querySelector('[data-setup-reset-confirm]');
            if (reset.disabled) resetConfirm = false;
            confirm.hidden = !resetConfirm;
            element.querySelector('[data-setup-reset-yes]').disabled = reset.disabled;
            var resetReason = element.querySelector('[data-setup-reset-reason]'); resetReason.hidden = reset.hidden;
            setText(resetReason, !state.online ? 'Waiting for the control bridge.' : state.pending ? 'Waiting for simulator confirmation.' : wizard.resetReason || 'Reset clears this aircraft’s learned controls and saved checks without restarting MSFS.');
            text('[data-wizard-ready]', active ? 'Auto is paused. Follow the hardware prompts; other setup controls are locked.' :
                !state.online ? 'Waiting for the control bridge.' : state.pending ? 'Waiting for simulator confirmation.' :
                wizard.prerequisite || (check && check.saved === 15 ? 'All four checks are saved. Use Reset setup to start again.' : 'The wizard holds one group, checks a 1% cockpit movement and guides aligned return. Move hardware when asked, then hold still.'));
            text('[data-wizard-results]', ['steering', 'throttle', 'propeller', 'mixture'].map((group, index) => titles[group] + ': ' +
                (active && wizard.group === group ? 'In progress' : wizard.phase === 7 && wizard.group === group ? 'Failed' :
                    check && (check.saved & (1 << index)) ? 'Saved' : check && (check.passed & (1 << index)) ? 'Passed · saving' : 'Still needed')).join(' · '));
            var heading = element.querySelector('[data-wizard-title]'); heading.hidden = !active && wizard.phase !== 7;
            setText(heading, title + ' · ' + ({1:'Return to Me', 2:'Move hardware', 3:'Holding controls', 4:'Checking output', 5:'Align to take over', 7:'Setup stopped'}[wizard.phase] || ''));
            var instruction = wizard.phase === 1 || wizard.phase === 5 ? (wizard.group === 'steering' ? 'Align yoke roll and pitch with the cockpit. Rudder and brakes transfer without matching. Control returns automatically.' : 'Align hardware with the cockpit positions. Control returns automatically.') :
                wizard.phase === 2 ? (wizard.group === 'steering' ? 'Move roll and pitch, then center the yoke and hold still. Rudder and brakes use fixed simulator mappings; no manual learning or matching. Trims sync separately.' :
                    'Move each ' + title.toLowerCase() + ' lever and hold still. If learning is needed, pause at two positions.') :
                wizard.phase === 3 || wizard.phase === 4 ? 'Keep hardware still. Checking a small cockpit movement and restoring the original positions.' :
                wizard.phase === 6 ? 'Setup complete. Valid results are saved; tested controls are on Me and the original Auto setting is restored.' :
                wizard.phase === 8 ? 'Wizard cancelled. This group is back on Me and the original Auto setting is restored.' : '';
            var pickup = snapshot.pickup[wizard.group];
            var ids = ['roll','pitch','rudder','steering','leftBrake','rightBrake','elevatorTrim','aileronTrim','rudderTrim'];
            for (var engine = 1; engine <= 4; ++engine) ['throttle','propeller','mixture'].forEach(type => ids.push(type + engine));
            var failedAxis = pickup.axes.find(axis => ids.indexOf(axis.id) === wizard.axis);
            var message = wizard.message ? wizard.message + (active ? ' ' + instruction : '') : instruction;
            if (wizard.phase === 7) message += (check && check.group === wizard.group && check.restored ? ' Original cockpit positions restored.' : '') +
                (snapshot[wizard.group + 'Blocked'] ? ' This group is held.' : ' This group is on Me.') + (snapshot.autoMask ? '' : ' Auto remains paused.');
            text('[data-wizard-instruction]', (failedAxis && [5,7].includes(wizard.phase) ? failedAxis.label + ': ' : '') + message);
            var list = element.querySelector('[data-wizard-axes]'); list.hidden = !active;
            var present = new Set();
            pickup.axes.forEach(function (axis) {
                present.add(axis.id);
                var row = list.querySelector('[data-wizard-axis="' + axis.id + '"]');
                if (!row) {
                    row = element.ownerDocument.createElement('div'); row.className = 'fsc-axis'; row.setAttribute('data-wizard-axis', axis.id);
                    row.innerHTML = '<strong data-label></strong><span data-state></span><small data-values></small>' +
                        '<div class="fsc-wizard-track" aria-hidden="true"><i data-range></i><b data-cockpit></b><b data-hardware></b></div>';
                    list.appendChild(row);
                }
                setText(row.querySelector('[data-label]'), axis.label);
                var needed = wizard.needed & (1 << ids.indexOf(axis.id));
                row.className = 'fsc-axis' + ((wizard.phase === 2 ? !needed : aligned(axis, state.online)) ? ' fsc-axis-aligned' : '');
                setText(row.querySelector('[data-state]'), !requiresAlignment(axis) ? 'Automatic · simulator state, no matching' : wizard.phase === 2 ? needed ? !axis.calibrated ? 'Pause at two positions' : requiresAlignment(axis) && wizard.group === 'steering' ? 'Move, then center yoke and hold' : 'Move once, then hold still' : 'Ready' :
                    reason(axis, state.online, snapshot[wizard.group + 'Blocked']));
                setText(row.querySelector('[data-values]'), 'Hardware ' + (axis.input === null ? 'unknown' : axis.input.toFixed(1) + '%') + ' · Cockpit ' +
                    (axis.target === null ? 'unknown' : axis.target.toFixed(1) + '%') + (requiresAlignment(axis) ? ' · Tolerance ' + pickup.tolerance + '%' : ' · Transfers with Steering'));
                var range = row.querySelector('[data-range]'); range.hidden = axis.target === null || !requiresAlignment(axis);
                range.style.left = Math.max(0, (axis.target || 0) - pickup.tolerance) + '%';
                range.style.width = (Math.min(100, (axis.target || 0) + pickup.tolerance) - Math.max(0, (axis.target || 0) - pickup.tolerance)) + '%';
                ['cockpit','hardware'].forEach(function (kind) {
                    var dot = row.querySelector('[data-' + kind + ']'), value = axis[kind === 'cockpit' ? 'target' : 'input'];
                    dot.hidden = value === null || kind === 'hardware' && (!axis.fresh || axis.relative);
                    dot.style.left = (value || 0) + '%';
                });
            });
            Array.from(list.children).forEach(row => { if (!present.has(row.getAttribute('data-wizard-axis'))) row.remove(); });
            var cancel = element.querySelector('[data-wizard-cancel]'); cancel.hidden = !active; cancel.disabled = !state.online || !!state.pending;
            var force = element.querySelector('[data-wizard-force]'); force.hidden = ![1,5,7].includes(wizard.phase) || !snapshot[wizard.group + 'Blocked'];
            force.disabled = !state.online || snapshot.applying || !!state.pending;
        }
        function render(state) {
            instrumentMonitor.update(state.snapshot, state);
            var surface = element.querySelector('.fsc-controls');
            if (selected) surface.setAttribute('data-assignment-owner', selected); else surface.removeAttribute('data-assignment-owner');
            var navigation=state.navigation;
            element.querySelector('[data-navigation]').hidden=!navigation || navigation.phase==='unavailable';
            element.querySelector('[data-read-avionics-route]').disabled=!state.online || !navigation || navigation.phase==='reading';
            text('[data-navigation-status]',navigation?navigation.message:'');
            text('[data-navigation-summary]',navigation?navigation.summary:'');
            var snapshot = state.snapshot;
            var known = !!(state.online && snapshot && snapshot.ready);
            var shared = root.FscControls.sharedActive(snapshot);
            var otherName = shared ? 'Other pilot' : 'Mock copilot';
            element.querySelector('[data-auto-recovery]').hidden = !known || snapshot.autoMask === 14 || !!(snapshot.wizard && [1,2,3,4,5,7].includes(snapshot.wizard.phase));
            element.querySelector('[data-resume-auto]').disabled = !state.usable;
            var sessionButton = element.querySelector('[data-shared-session]');
            sessionButton.hidden = !(snapshot && snapshot.shared); sessionButton.disabled = !state.usable;
            var phase = snapshot && snapshot.shared ? snapshot.shared.phase : 0;
            renderConnection(snapshot, state);
            if (snapshot && snapshot.connection && ![1, 2, 3].includes(phase)) sessionButton.disabled = !state.usable ||
                snapshot.shared && snapshot.shared.needsRecovery || connectionDirty || !snapshot.connection.revision || ![2, 6].includes(snapshot.connection.backend);
            setText(sessionButton, phase === 1 ? 'Cancel join' : shared ? 'Leave shared session' : 'Join shared session');
            var sessionStatus = element.querySelector('[data-shared-status]'); sessionStatus.hidden = sessionButton.hidden || !!(snapshot && snapshot.connection && phase === 0);
            setText(sessionStatus, phase === 1 ? 'Waiting for the shared connection. Local controls are unchanged.' :
                shared ? snapshot.connection && snapshot.connection.code === 5 ? 'Updates delayed. Shared session retained; control changes are waiting.' : 'Shared controls: ownership changes wait for both simulators.' : phase === 4 ?
                'Shared session ended. Use Me or Force takeover on each control to recover.' : 'Connect FS Copilot with the other pilot, then join shared controls here.');
            text('[data-seat-choice="mock"] .fsc-seat-name', shared ? 'OTHER' : 'MOCK');
            text('[data-seat-choice="me"] .fsc-seat-sub', shared ? 'YOU' : 'LEFT PILOT');
            text('[data-seat-choice="mock"] .fsc-seat-sub', shared ? 'OTHER PILOT' : 'COPILOT');
            var seatState = snapshot && snapshot.shared;
            var liveSeat = shared && seatState.seatRevision ? seatState.seat : 0;
            var rightSeat = liveSeat ? liveSeat === 2 : !!(snapshot && snapshot.connection && snapshot.connection.seat === 2);
            var swapAvailable = !!(shared && seatState.phase === 3 && seatState.seatRevision);
            element.querySelector('[data-seat-swap]').hidden = !swapAvailable;
            var camera = snapshot && snapshot.camera;
            var baro = snapshot && snapshot.altimeters;
            element.querySelector('[data-altimeters]').hidden = !baro;
            var baroFlags = baro && Number.isInteger(baro.nativeFlags) ? baro.nativeFlags : 0;
            var baroProof = !!(baroFlags & 512), baroBlocked = (baroFlags >> 4) & 3;
            var baroLocal = !!((baroFlags & 64) && !(baroFlags & 384) && baroProof && !baroBlocked);
            var baroShared = !!(known && baro && !baro.issue && baro.reported && baro.sharedPhase === 2 && liveSeat &&
                (baroFlags & 13) === 13 && !(baroFlags & 448) && baroProof && baroBlocked === (liveSeat === 1 ? 2 : 1));
            var baroState = !known ? 'unconfirmed' : baroFlags & 128 ? 'releasing inputs' : baroLocal ? 'local' :
                baroFlags & 256 ? 'inputs held' : baroShared ? 'shared' : baro && baro.issue ? 'sync stopped' : baro && baro.reported && [1, 3, 7].includes(baro.sharedPhase) ? 'synchronizing' :
                baro && baro.reported && [4, 5, 6, 8].includes(baro.sharedPhase) ? 'sync stopped' :
                !(baroFlags & 4) || baroProof && !baroBlocked ? 'local' : 'awaiting confirmation';
            text('[data-altimeter-title]', 'Altimeters · ' + baroState);
            text('[data-altimeter-status]', !known ? 'Waiting for fresh simulator status.' : baroFlags & 128 ? 'Waiting for the simulator to release your instrument inputs.' + (shared ? ' Shared flight controls continue.' : '') :
                baroFlags & 256 ? (baroFlags & 64 ? 'Input release was not confirmed. Retry local recovery.' : 'Instrument inputs are held. Use local altimeters to release your inputs.') :
                baroLocal ? 'Your instrument inputs are local. Rejoin the shared session to synchronize instruments again.' :
                baro && baro.issue ? 'Synchronization stopped. Use local altimeters to release your instrument inputs.' :
                baroShared ? 'Pilot controls the left altimeter; copilot controls the right. Each keeps its own QNH and STD.' :
                baro && baro.reported && [1, 3, 7].includes(baro.sharedPhase) ? 'Waiting for the simulator to confirm instrument inputs and pressures.' :
                !(baroFlags & 4) ? 'Synchronization unavailable for this aircraft. Instrument readings are shown below.' : 'Waiting for confirmed instrument ownership.');
            var localButton = element.querySelector('[data-instruments-local]');
            localButton.hidden = !baro || !(baroFlags & 2) || !(baroFlags & 304) && !(baro.reported && [1, 2, 3, 4, 7].includes(baro.sharedPhase));
            localButton.disabled = !controller.canUseLocalInstruments();
            localButton.textContent = baroFlags & 128 ? 'Releasing inputs…' : baroFlags & 64 ? 'Retry local recovery' : 'Use local altimeters';
            element.querySelectorAll('[data-altimeter]').forEach(function (row) {
                var index = Number(row.getAttribute('data-altimeter'));
                var name = baroFlags & 4 ? (index ? 'Copilot · right' : 'Pilot · left') : 'Altimeter ' + (index + 1);
                var owner = baroShared ? (index + 1 === liveSeat ? ' · Me' : ' · Other pilot') : '';
                var input = Number.isInteger(baro && baro.nativeFlags) ? !known || !baroProof ? ' · Input unconfirmed' :
                    baroBlocked & (1 << index) ? ' · Input held' : ' · Input local' : '';
                row.setAttribute('data-instrument-owner', baroShared ? (index + 1 === liveSeat ? 'me' : 'other') : 'local');
                setText(row, name + owner + ': ' + (!known || !baro || !baro.fresh ? 'Waiting for readings' :
                    'Tuned ' + (baro.pressure16[index] / 16).toFixed(2) + ' hPa · STD ' + (baro.standard[index] ? 'on' : 'off')) + input);
            });
            var ordinary = snapshot && snapshot.cockpit;
            var recovery = ordinary && ordinary.recovery;
            element.querySelector('[data-cockpit-recovery]').hidden = !ordinary || !ordinary.available;
            element.querySelector('[data-resync-cockpit]').disabled = !state.usable || !ordinary || !ordinary.sharing || recovery && [1,2].includes(recovery.phase);
            text('[data-recovery-status]', !ordinary || !ordinary.sharing ? 'Available during a shared session. The host provides the recovery baseline.' :
                !recovery || !recovery.phase ? 'Compare shared instruments with the host and repair differences. Ownership and local GTN pages stay unchanged.' :
                (['','Requesting fresh host readings…','Resyncing…','Resync complete.','Resync needs attention.'][recovery.phase]) + ' Checked ' + recovery.checked +
                ' · corrected ' + recovery.corrected + ' · pending ' + recovery.pending + ' · failed ' + recovery.failed + ' · unavailable ' + recovery.unavailable +
                (recovery.issues ? '. ' + recovery.issues : ''));
            var binding = snapshot && snapshot.forceButton;
            element.querySelector('[data-force-button]').hidden = !binding;
            if (binding) {
                var deviceSelect = element.querySelector('[data-force-device]'), savedChoice = deviceSelect.value;
                var listKey = JSON.stringify(binding.devices);
                if (deviceSelect.getAttribute('data-devices') !== listKey) {
                    deviceSelect.innerHTML = '';
                    binding.devices.forEach(function(device) { var option = element.ownerDocument.createElement('option'); option.value = String(device.id); option.textContent = device.name + ' (' + device.id + ')'; deviceSelect.appendChild(option); });
                    if (binding.devices.some(d => String(d.id) === savedChoice)) deviceSelect.value = savedChoice;
                    deviceSelect.setAttribute('data-devices', listKey);
                }
                deviceSelect.disabled = !state.usable || binding.listening;
                element.querySelectorAll('[data-force-binding]').forEach(function(button) {
                    var action = button.getAttribute('data-force-binding');
                    button.hidden = action === 'cancel' ? !binding.listening : binding.listening;
                    button.disabled = !state.usable || action === 'listen' && !binding.devices.length || action === 'clear' && !binding.saved;
                });
                text('[data-force-binding-status]', binding.error || (binding.listening ? binding.captured ? 'Release the button to save it.' : 'Press and release the button to assign it. Assignment expires in 30 seconds.' :
                    !binding.saved ? 'No button assigned.' : binding.name + ' · button ' + binding.button + '. ' + (binding.armed ? 'Ready.' : binding.ready ? 'Press and release once to arm after loading or reconnecting.' : 'Waiting for the controller.')));
            }
            if (ordinary && ordinary.available) {
                text('[data-altimeter-title]', 'Altimeters · ' + (!known ? 'unconfirmed' : ordinary.sharing ? 'shared definitions' : 'local'));
                text('[data-altimeter-status]', !known ? 'Waiting for fresh simulator status.' : ordinary.result === 4 ?
                    'An instrument update was not confirmed. Shared flight controls continue; collect the helper logs for details.' :
                    ordinary.sharing ? 'Both altimeters are shared normally. Either pilot can adjust either instrument; each retains its own setting.' :
                    'Normal instrument definitions are ready. Synchronization starts with the shared session.');
                localButton.hidden = true;
                element.querySelectorAll('[data-altimeter]').forEach(function(row) {
                    var index=Number(row.getAttribute('data-altimeter'));
                    row.setAttribute('data-instrument-owner',ordinary.sharing?'shared':'local');
                    setText(row,(index?'Copilot · right':'Pilot · left') + ': ' + (!known || !baro || !baro.fresh ? 'Waiting for readings' :
                        'Tuned ' + (baro.pressure16[index]/16).toFixed(2) + ' hPa · STD ' + (baro.standard[index]?'on':'off')));
                });
            }
            element.querySelector('[data-camera-status]').hidden = !camera || !camera.seat || !known;
            text('[data-camera-status]', !camera ? '' : camera.phase === 3 ? (camera.seat === 1 ? 'Pilot view selected.' : 'Copilot view selected.') :
                camera.phase === 4 ? 'Seat view not confirmed. Select Pilot or Copilot in the simulator camera panel.' : 'Waiting to apply your cockpit seat view.');
            text('[data-seat-status]', !swapAvailable ? '' : seatState.seatPending >= 3 ? 'Waiting for simulator confirmation before swapping seats.' : seatState.seatPending === 1 ? 'Seat swap requested. Waiting for the other pilot.' :
                seatState.seatPending === 2 ? 'The other pilot requests a seat swap.' : rightSeat ? 'Your seat: Copilot · right' : 'Your seat: Pilot · left');
            element.querySelectorAll('[data-seat-action]').forEach(function (button) {
                var action = button.getAttribute('data-seat-action');
                button.hidden = !swapAvailable || (action === 'request' ? seatState.seatPending !== 0 : action === 'cancel' ? ![1, 3].includes(seatState.seatPending) :
                    action === 'decline' ? ![2, 4].includes(seatState.seatPending) : seatState.seatPending !== 2);
                button.disabled = !state.usable;
            });
            element.querySelector('[data-seat-choice="me"]').setAttribute('transform', rightSeat ? 'translate(440 0)' : 'translate(0 0)');
            element.querySelector('[data-seat-choice="mock"]').setAttribute('transform', rightSeat ? 'translate(-440 0)' : 'translate(0 0)');
            if (snapshot && snapshot.connection && snapshot.connection.revision) {
                text('[data-seat-choice="me"] .fsc-seat-sub', rightSeat ? 'YOU · RIGHT' : 'YOU · LEFT');
                text('[data-seat-choice="mock"] .fsc-seat-sub', rightSeat ? 'OTHER · LEFT' : 'OTHER · RIGHT');
            }
            var preset = root.FscControls.commonTolerance(snapshot);
            var toleranceButton = element.querySelector('[data-cycle-tolerance]');
            setText(toleranceButton, known ? 'Tolerance: ' + toleranceName(preset) : 'Tolerance');
            toleranceButton.disabled = !state.usable;
            toleranceButton.setAttribute('aria-label', known ? 'Tolerance ' + toleranceName(preset) + '. Click for ' + toleranceName(preset === 10 ? 20 : preset === 20 ? 30 : 10) + '. Applies to all controls.' : 'Tolerance unavailable until connected.');
            text('[data-connection]', !state.online ? 'Waiting for bridge' : snapshot.applying || state.pending ? 'Applying…' : snapshot.ready ? 'Connected' : snapshot.error ? 'Bridge error' : 'Waiting for simulator');
            element.querySelector('[data-connection]').className = 'fsc-connection' + (known ? ' fsc-connected' : '');
            text('[data-assignment]', selected ? 'Assign to ' + (selected === 'me' ? 'Me' : shared ? 'Other' : 'Mock') + ' · tap controls' : 'Select a seat, then tap controls');
            element.querySelectorAll('[data-seat-choice]').forEach(function (seat) { seat.setAttribute('aria-pressed', String(seat.getAttribute('data-seat-choice') === selected)); });
            text('[data-learning-storage]', !state.online || !snapshot ? 'Waiting for saved control mappings.' :
                snapshot.learningStorage === 'unavailable' ? 'Saving unavailable. Learning works for this session.' :
                snapshot.learningStorage === 'waiting' ? 'Waiting for aircraft identity.' : snapshot.learningStorage === 'saving' ? 'Saving learned controls…' :
                snapshot.learningStorage === 'ready' ? 'Control mappings save automatically for this aircraft between sessions. Move saved controls once to read their current position; no relearning needed.' : 'Mappings remain for this aircraft session.');
            text('[data-mixture-reference]', known && snapshot.mixtureReference !== 'waiting' ? 'Mixture alignment reference: ' + (snapshot.mixtureReference === 'cockpit' ? 'cockpit levers' : 'standard simulator levers') : 'Waiting for cockpit lever references.');
            var allAxes = {};
            var advanced = true;
            var sharedQuadrant = root.FscControls.sharedGroup(snapshot, 'quadrant');
            var pairDisplay = !!(advanced || sharedQuadrant && sharedQuadrant.mixed || snapshot && (snapshot.autoMask || snapshot.quadrantMixed || snapshot.quadrantToleranceMixed));
            quadrantGraphics(snapshot ? snapshot.pickup.quadrant.axes : []);
            groups.forEach(function (group) {
                var pickup = snapshot ? snapshot.pickup[group] : { waiting: false, autoEnabled: false, tolerance: 10, axes: [] };
                var blocked = !!(snapshot && snapshot[group + 'Blocked']);
                var ownership = root.FscControls.sharedGroup(snapshot, group);
                var ownerBlocked = ownership ? ownership.owner === 'mock' : blocked;
                var ownerKnown = known && (!ownership || ownership.owner !== null || ownership.mixed);
                var mixed = ownership ? ownership.mixed : !!(group === 'quadrant' && snapshot && snapshot.quadrantMixed);
                var waiting = known && pickup.waiting;
                var control = controlElement(group);
                var isPair = pairs.includes(group);
                var interactive = !isPair || advanced;
                var alignmentAxes = pickup.axes.filter(requiresAlignment);
                var count = alignmentAxes.filter(axis => aligned(axis, known)).length;
                control.setAttribute('class', (isPair ? 'fsc-pair ' + (pairDisplay ? 'fsc-pair-visible ' : '') : '') + (interactive ? 'fsc-control ' : '') + (!ownerKnown ? 'fsc-unconfirmed' : mixed ? 'fsc-owner-mixed' : ownerBlocked ? 'fsc-owner-mock' : 'fsc-owner-me') + (waiting || ownership && ownership.transferring ? ' fsc-waiting' : ''));
                if (isPair) {
                    if (advanced) control.setAttribute('data-control', group); else control.removeAttribute('data-control');
                    control.querySelector('.fsc-pair-hit').setAttribute('visibility', pairDisplay ? 'visible' : 'hidden');
                    text('[data-pair-owner="' + group + '"]', !ownerKnown ? '—' : waiting ? 'WAIT' : ownerBlocked ? shared ? 'Other' : 'Mock' : 'Me');
                    text('[data-pair-mode="' + group + '"]', group === 'propeller' ? 'RPM' : group === 'throttle' ? 'THR' : 'MIX');
                    control.querySelectorAll('.fsc-pair-owner,.fsc-pair-mode').forEach(node => node.setAttribute('visibility', pairDisplay ? 'visible' : 'hidden'));
                }
                control.setAttribute('role', !interactive || group === 'quadrant' && advanced ? 'group' : 'button');
                control.setAttribute('aria-disabled', String(!state.usable));
                if (waiting || ownership && ownership.transferring) control.setAttribute('data-pending-owner', waiting ? 'me' : assignmentRequests.has(group) && selected ? selected : ownerBlocked ? 'mock' : 'me');
                else control.removeAttribute('data-pending-owner');
                control.setAttribute('tabindex', state.usable && interactive && !(group === 'quadrant' && advanced) ? '0' : '-1');
                control.setAttribute('aria-haspopup', 'menu');
                control.setAttribute('aria-expanded', String(menuGroup === group));
                var actions = element.querySelector('[data-menu-for="' + group + '"]');
                if (actions) {
                actions.setAttribute('aria-disabled', String(!state.usable));
                actions.setAttribute('tabindex', '-1'); actions.setAttribute('aria-hidden','true');
                actions.setAttribute('aria-expanded', String(menuGroup === group));
                }
                control.setAttribute('aria-label', titles[group] + ': ' + (!ownerKnown ? 'unconfirmed' : mixed ? 'mixed owners' : ownerBlocked ? otherName : 'Me') + (selected ? '. Assign to ' + (selected === 'me' ? 'Me' : otherName) : '. Select a seat to assign.'));
                if (!isPair) {
                text('[data-owner="' + group + '"]', !ownerKnown ? 'Unconfirmed' : waiting ? 'Me requested' : ownerBlocked ? otherName : 'Me');
                if (mixed) text('[data-owner="quadrant"]', 'Mixed owners');
                text('[data-summary="' + group + '"]', !known ? 'Waiting for simulator' : waiting && pickup.automation ? 'Autopilot on' : waiting && snapshot.applying ? 'Confirming takeover…' : waiting ? count + '/' + alignmentAxes.length + ' aligned · ±' + pickup.tolerance + '%' : '±' + pickup.tolerance + '% · ' + (blocked ? 'Hardware blocked' : 'Hardware active'));
                var unlearned = alignmentAxes.some(axis => !axis.calibrated);
                var stale = alignmentAxes.some(axis => axis.calibrated && !axis.fresh);
                text('[data-hint="' + group + '"]', !known ? '' : pickup.automation ? 'AP off for steering pickup' : alignmentAxes.some(axis => axis.saved) ? 'Saved: move controls once' : unlearned ? blocked ? 'Actions menu: Force takeover' : 'Move + pause twice to learn' : stale ? 'Move controls to refresh' : waiting ? group === 'steering' ? 'Match yoke roll and pitch only' : 'Match dots to white marks' : count + '/' + alignmentAxes.length + ' aligned');
                if (advanced && group === 'quadrant') {
                    text('[data-summary="quadrant"]', 'Auto ' + (snapshot && snapshot.autoMask ? 'on' : 'off'));
                    text('[data-hint="quadrant"]', 'Tap engine pairs · ⋯ for Auto');
                } else if (known && group === 'quadrant' && pairDisplay) {
                    text('[data-summary="quadrant"]', snapshot.autoMask ? 'Auto · independent pairs' : 'Manual · mixed controls');
                    text('[data-hint="quadrant"]', 'Tap quadrant to claim all');
                }
                }
                var unlearned = pickup.axes.some(axis => !axis.calibrated);
                element.querySelector('[data-settings-group="' + group + '"]').hidden = isPair ? !advanced : group === 'quadrant' && advanced;
                var cancel = element.querySelector('[data-cancel="' + group + '"]'); cancel.hidden = !waiting || isPair && snapshot.pickup.quadrant.waiting; cancel.disabled = !state.usable;
                element.querySelectorAll('[data-group="' + group + '"]').forEach(function (button) {
                    var owner = button.getAttribute('data-owner-choice');
                    button.disabled = !state.usable;
                    button.className = owner === 'me' && waiting ? 'fsc-pickup-waiting' : '';
                    button.textContent = owner === 'me' ? waiting ? 'Me — waiting' : 'Me' : waiting ? 'Cancel takeover' : otherName;
                    button.setAttribute('aria-pressed', String(ownerKnown && !mixed && owner === (ownerBlocked ? 'mock' : 'me')));
                });
                var select = element.querySelector('[data-tolerance="' + group + '"]');
                var mixedTolerance = group === 'quadrant' && snapshot && snapshot.quadrantToleranceMixed;
                Array.from(select.querySelectorAll('option')).forEach(option => {
                    var chosen = mixedTolerance ? option.value === 'mixed' : Number(option.value) === pickup.tolerance;
                    if (option.value === 'mixed') option.hidden = !mixedTolerance;
                    else if (Number(option.value) < 10) option.hidden = !chosen;
                    if (option.selected !== chosen) option.selected = chosen;
                }); select.disabled = !state.usable;
                element.querySelector('[data-learn="' + group + '"]').disabled = !state.usable || blocked || mixed || shared;
                if (snapshot) {
                    text('[data-seen="' + group + '"]', snapshot[group].seen); text('[data-blocked="' + group + '"]', snapshot[group].blocked);
                    text('[data-last="' + group + '"]', snapshot[group].last ? snapshot[group].last + ' = ' + snapshot[group].value : 'No control events yet');
                }
                text('[data-pickup-hint="' + group + '"]', pickup.axes.some(axis => axis.saved) ? 'Mappings loaded. Move controls once to read their current position.' : blocked && unlearned ? 'Open this control\'s three-dot menu and choose Force takeover to return to Me before learning.' : 'New mappings: pause at two separated positions. White marks show cockpit position.');
                renderDiagnostics(group, pickup, known, blocked);
                if (!isPair || advanced) pickup.axes.forEach(axis => { allAxes[axis.id] = axis; });
            });
            element.querySelectorAll('[data-axis-graphic]').forEach(function (row) {
                var id = row.getAttribute('data-axis-graphic'); var group = /[1-4]$/.test(id) ? id.slice(0, -1) : 'steering';
                var pickup = snapshot && snapshot.pickup[group];
                renderGraphic(row, allAxes[id], pickup ? pickup.tolerance : 10, known, snapshot && snapshot[group + 'Blocked']);
            });
            extraAxes.forEach(function (axis) { element.querySelector('[data-extra="' + axis[0] + '"]').setAttribute('visibility', allAxes[axis[0]] ? 'visible' : 'hidden'); });
            ['input', 'target'].forEach(function (kind) {
                var roll = allAxes.roll, pitch = allAxes.pitch;
                var visible = known && roll && pitch && roll[kind] !== null && pitch[kind] !== null && (kind === 'target' || roll.calibrated && pitch.calibrated && roll.fresh && pitch.fresh);
                element.querySelectorAll('[data-yoke-' + kind + ']').forEach(function (yoke) {
                    yoke.setAttribute('visibility', visible ? 'visible' : 'hidden');
                    yoke.setAttribute('transform', visible ? 'translate(0 ' + ((pitch[kind] - 50) * 0.28) + ') rotate(' + ((roll[kind] - 50) * 0.85) + ')' : '');
                });
            });
            renderPedals(allAxes.rudder, known, snapshot && snapshot.steeringBlocked);
            if (menuGroup && (!state.usable || snapshot.session !== menuSession)) closeMenu(false);
            var check = snapshot && snapshot.outputCheck;
            renderWizard(snapshot, state);
            var checkText = '';
            if (check && check.available) {
                var passedChecks = [1, 2, 4, 8].filter(bit => check.passed & bit).length;
                var remaining = ['steering', 'throttle', 'propeller', 'mixture'].filter((group, index) => !(check.passed & (1 << index))).map(group => titles[group]);
                var progress = 'Shared output checks: ' + passedChecks + '/4.';
                var needed = remaining.length ? ' Still needed: ' + remaining.join(', ') + '.' : '';
                var checking = [1, 2, 3].includes(check.phase);
                checkText = checking ? 'Checking ' + titles[check.group] + ' output. Keep the hardware still.' :
                    check.writable === false ? 'Simulator output unavailable. Recover held controls with Force takeover, then restart MSFS before retrying.' :
                    check.phase === 5 ? (
                        check.restored ? 'Output check failed; original cockpit position restored.' : 'Output check stopped. Use this control\'s Force takeover if needed.') :
                    !remaining.length ? progress + (check.saved === 15 ? ' Complete and saved for this aircraft.' : ' Complete for this session.') :
                    progress + needed + ' Park with brake set and AP off. Assign a missing group to Mock → its ⋯ menu → Check shared output. Auto pauses while the check moves 1% and restores.';
                if (checking || check.phase === 5 || check.writable === false) checkText += needed;
                if (check.storage === 'unavailable') checkText += ' Could not save validation; results will not survive a restart.';
            }
            var checkStatus = element.querySelector('[data-output-check]'); checkStatus.hidden = !checkText; checkStatus.textContent = checkText;
            if (snapshot && snapshot.wizard && check && check.available && check.passed !== 15)
                setText(checkStatus, progress + needed + (snapshot.wizard.phase >= 1 && snapshot.wizard.phase <= 5 ? ' Follow the Setup wizard below.' : ' Use Setup wizard to complete the missing checks automatically.'));
            var notice = state.error || (check && check.phase === 5 ? checkText : '') || (snapshot && snapshot.shared && snapshot.shared.phase === 4 && snapshot.shared.holding ?
                'Shared link ended. Use Me or Force takeover on a control to recover it.' : '');
            var error = element.querySelector('[data-error]'); error.hidden = !notice; error.textContent = notice;
            if(snapshot)assignmentRequests.forEach(function(owner,group) {
                var ownership=root.FscControls.sharedGroup(snapshot,group);
                var inFlight=state.pending && state.pending.operation.indexOf('set|'+group+'|')===0;
                var completed=ownership ? ownership.owner===owner && !ownership.transferring && (owner!=='me'||ownership.attached) : snapshot[group+'Blocked']===(owner==='mock');
                if(!inFlight && completed && !snapshot.pickup[group].waiting)assignmentRequests.delete(group);
            });
            if (!flushForcedAssignment(state)) flushCancelledAssignments(state);
        }
        function renderPedals(axis, known, blocked) {
            var targetKnown=!!(known && axis && axis.target!==null);
            var inputKnown=!!(known && axis && axis.calibrated && axis.fresh && axis.input!==null);
            element.querySelectorAll('[data-rudder-pair]').forEach(function(pair) {
                pair.setAttribute('class',aligned(axis,known)?'fsc-rudder-aligned':'');
                ['left','right'].forEach(function(side,index) {
                    ['target','input'].forEach(function(kind) {
                        var rect=pair.querySelector('[data-pedal-'+kind+'="'+side+'"]');
                        var valid=kind==='target'?targetKnown:inputKnown;
                        rect.setAttribute('visibility',valid?'visible':'hidden');
                        if(valid)rect.setAttribute('transform','translate(0 '+((axis[kind]-50)/50*(index?12:-12))+')');
                    });
                });
                pair.querySelector('[data-rudder-title]').textContent='Rudder: '+reason(axis,known,blocked);
            });
        }
        function flushForcedAssignment(state) {
            if (!queuedForce) return false;
            if (selected!=='me' || !state.online || !state.snapshot || queuedForce.scope!==forceScope(state.snapshot) || Date.now()-queuedForce.at>3000) {queuedForce=null;return false;}
            if (!state.usable) return false;
            var group=queuedForce.group;queuedForce=null;assignmentRequests.delete(group);cancelledRequests.delete(group);
            return controller.forceTakeover(group);
        }
        function forceScope(snapshot) { return snapshot.session+'|'+(snapshot.shared && snapshot.shared.session || '')+'|'+(snapshot.shared && snapshot.shared.phase || 0); }
        function doubleclick(event) {
            var target=eventTarget(event);
            if(selected!=='me' || !target || !target.hasAttribute('data-control'))return;
            var group=target.getAttribute('data-control'),state=controller.state();
            if(group==='quadrant' || !state.online || !state.snapshot || !state.snapshot.ready)return;
            event.preventDefault();event.stopPropagation();
            queuedForce={group:group,scope:forceScope(state.snapshot),at:Date.now()};
            flushForcedAssignment(state);
        }
        function cancelAssignment() {
            selected = null;
            queuedForce=null;
            assignmentRequests.forEach((owner,group) => cancelledRequests.add(group));
            assignmentRequests.clear();
        }
        function flushCancelledAssignments(state) {
            if (flushingCancellation || !state.usable || !state.snapshot) return;
            for (const group of cancelledRequests) {
                cancelledRequests.delete(group);
                var ownership = root.FscControls.sharedGroup(state.snapshot, group);
                if (state.snapshot.pickup[group].waiting || ownership && ownership.transferring) {
                    flushingCancellation = true;
                    try { controller.cancelTakeover(group); } finally { flushingCancellation = false; }
                    break; // The next fresh acknowledgement drains the next group.
                }
            }
        }
        function outsideAssignment(event) {
            if (!selected) return;
            var target = event.target;
            if (target && target.closest && target.closest('[data-cockpit-body]')) return;
            var resolved = eventTarget(event);
            if (resolved && (resolved.hasAttribute('data-control') || resolved.hasAttribute('data-seat-choice'))) return;
            cancelAssignment(); render(controller.state());
        }
        function setup(open) {
            resetConfirm = false;
            element.querySelector('[data-setup-reset-confirm]').hidden = true;
            cancelPress(); setHover(null);
            closeMenu(false);
            element.querySelector('[data-setup]').hidden = !open;
            element.querySelector('[data-settings]').setAttribute('aria-expanded', String(open));
            var focus = element.querySelector(open ? '[data-close-settings]' : '[data-settings]');
            if (focus.focus) focus.focus();
        }
        function actionTarget(target) {
            while (target && target !== element) {
                if (target.hasAttribute && (target.hasAttribute('data-monitor-action') || target.hasAttribute('data-force-binding') || target.hasAttribute('data-resync-cockpit') || target.hasAttribute('data-resume-auto'))) return target;
                if (['data-setup-reset', 'data-setup-reset-yes', 'data-setup-reset-no', 'data-read-avionics-route', 'data-wizard-start', 'data-wizard-cancel', 'data-wizard-force', 'data-seat-choice', 'data-seat-action', 'data-instruments-local', 'data-control', 'data-menu-for', 'data-menu-pair', 'data-advanced', 'data-auto', 'data-cycle-tolerance', 'data-shared-session', 'data-save-connection', 'data-connection-choice', 'data-settings', 'data-close-settings', 'data-force', 'data-check-output', 'data-learn', 'data-owner-choice', 'data-cancel'].some(attribute => target.hasAttribute && target.hasAttribute(attribute))) return target;
                target = target.parentNode;
            }
            return null;
        }
        function click(event) {
            var target = eventTarget(event);
            if (suppressedClick && Date.now() <= suppressedClick.until && target === suppressedClick.control) {
                suppressedClick = null; event.preventDefault(); event.stopPropagation(); return;
            }
            if (secondaryButton(event)) { mouseMenu(event); return; }
            if (!target || !['data-force', 'data-check-output', 'data-menu-pair', 'data-advanced', 'data-auto'].some(key => target.hasAttribute(key))) closeMenu(false);
            if (!target || target.disabled) return;
            if (instrumentMonitor.click(target)) return;
            if (target.hasAttribute('data-seat-choice')) {
                var choice = target.getAttribute('data-seat-choice');
                var wasSelected = selected === choice;
                cancelAssignment(); selected = wasSelected ? null : choice;
                render(controller.state()); return;
            }
            if (target.hasAttribute('data-settings')) { setup(true); return; }
            if (target.hasAttribute('data-close-settings')) { setup(false); return; }
            if (target.hasAttribute('data-read-avionics-route')) { controller.inspectAvionicsRoute(); return; }
            if (target.hasAttribute('data-wizard-start')) { controller.startWizard(); return; }
            if (target.hasAttribute('data-setup-reset')) {
                if (target.disabled) return;
                resetConfirm = true; element.querySelector('[data-setup-reset-confirm]').hidden = false; return;
            }
            if (target.hasAttribute('data-setup-reset-no')) { resetConfirm = false; element.querySelector('[data-setup-reset-confirm]').hidden = true; return; }
            if (target.hasAttribute('data-setup-reset-yes')) {
                if (controller.resetSetup()) { resetConfirm = false; element.querySelector('[data-setup-reset-confirm]').hidden = true; }
                return;
            }
            if (target.hasAttribute('data-resync-cockpit')) { controller.resyncCockpit(); return; }
            if (target.hasAttribute('data-resume-auto')) { controller.setAuto(true); return; }
            if (target.hasAttribute('data-force-binding')) { controller.configureForceButton(target.getAttribute('data-force-binding'), Number(element.querySelector('[data-force-device]').value)); return; }
            if (target.hasAttribute('data-wizard-cancel')) { controller.cancelWizard(); return; }
            if (target.hasAttribute('data-wizard-force')) { controller.forceWizardGroup(); return; }
            if (target.hasAttribute('data-instruments-local')) { controller.useLocalInstruments(); return; }
            if (!controller.state().usable) return;
            if (target.hasAttribute('data-connection-choice')) {
                var field = element.querySelector('[data-connection-field="' + target.getAttribute('data-connection-choice') + '"]');
                if (field.disabled) return;
                field.value = target.getAttribute('data-choice-value');
                render(controller.state()); return;
            }
            if (target.hasAttribute('data-shared-session')) { controller.sharedSession(); return; }
            if (target.hasAttribute('data-seat-action')) { controller.changeSeat(target.getAttribute('data-seat-action')); return; }
            if (target.hasAttribute('data-save-connection')) { controller.setConnection(connectionDraft()); return; }
            if (target.hasAttribute('data-cycle-tolerance')) { controller.cycleTolerance(); return; }
            if (target.hasAttribute('data-check-output')) { var checkGroup = menuGroup; closeMenu(true); controller.checkOutput(checkGroup); return; }
            if (target.hasAttribute('data-menu-for')) { openMenu(target, null); return; }
            if (target.hasAttribute('data-menu-pair')) { openMenu(controlElement(target.getAttribute('data-menu-pair')), null); return; }
            if (target.hasAttribute('data-advanced')) { closeMenu(true); controller.setAdvanced(!controller.state().snapshot.advanced); return; }
            if (target.hasAttribute('data-auto')) {
                closeMenu(true); controller.setAuto(!controller.state().snapshot.autoMask); return;
            }
            if (target.hasAttribute('data-control')) {
                var group = target.getAttribute('data-control');
                var snapshot = controller.state().snapshot;
                if (group === 'quadrant' || !selected) return;
                var ownership = root.FscControls.sharedGroup(snapshot, group);
                if(selected==='me' && snapshot.pickup[group].waiting){assignmentRequests.set(group,'me');return;}
                if (selected === 'me' && (ownership ? ownership.owner === 'me' && ownership.attached : !snapshot[group + 'Blocked'] && !(group === 'quadrant' && snapshot.quadrantMixed))) return;
                assignmentRequests.set(group,selected);
                if (!controller.setOwner(group, selected)) assignmentRequests.delete(group);
            } else if (target.hasAttribute('data-cancel')) controller.cancelTakeover(target.getAttribute('data-cancel'));
            else if (target.hasAttribute('data-force')) {
                suppressedClick = null;
                var forcedGroup = menuGroup; closeMenu(true);
                if (forcedGroup) controller.forceTakeover(forcedGroup);
            }
            else if (target.hasAttribute('data-learn')) controller.relearn(target.getAttribute('data-learn'));
            else controller.setOwner(target.getAttribute('data-group'), target.getAttribute('data-owner-choice'));
        }
        function keydown(event) {
            if (event.key === 'Escape') {
                if (menuGroup) { event.preventDefault(); closeMenu(true); }
                else if (!element.querySelector('[data-setup]').hidden) setup(false);
                else { cancelAssignment(); render(controller.state()); }
                return;
            }
            if (menuGroup && event.key === 'Tab') { closeMenu(true); return; }
            if (!element.querySelector('[data-setup]').hidden && event.key === 'Tab') {
                var fields = Array.from(element.querySelector('[data-setup]').querySelectorAll('button, select, input')).filter(field => !field.disabled && !field.closest('[hidden]') && !field.closest('details:not([open])'));
                var current = fields.indexOf(element.ownerDocument.activeElement);
                var next = (current + (event.shiftKey ? -1 : 1) + fields.length) % fields.length;
                event.preventDefault(); if (fields[next].focus) fields[next].focus(); return;
            }
            var target = actionTarget(event.target);
            if (target && (target.hasAttribute('data-control') || target.hasAttribute('data-menu-for')) && (event.key === 'ContextMenu' || event.shiftKey && event.key === 'F10')) {
                event.preventDefault(); openMenu(target, null); return;
            }
            if (target && (target.hasAttribute('data-control') || target.hasAttribute('data-menu-for') || target.hasAttribute('data-seat-choice')) && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); click(event); }
        }
        function closeMenu(restoreFocus) {
            var previous = menuGroup;
            var anchor = menuAnchor;
            menuGroup = null; menuSession = null; menuAnchor = null;
            element.querySelector('[data-control-menu]').hidden = true;
            if (previous) {
                var control = controlElement(previous);
                control.setAttribute('aria-expanded', 'false');
                var actions = element.querySelector('[data-menu-for="' + previous + '"]'); if (actions) actions.setAttribute('aria-expanded', 'false');
                if (restoreFocus && anchor && anchor.focus) anchor.focus();
            }
        }
        function openMenu(control, event) {
            closeMenu(false);
            if (!controller.state().usable || !element.querySelector('[data-setup]').hidden) return;
            menuGroup = control.getAttribute('data-control') || control.getAttribute('data-menu-for');
            menuSession = controller.state().snapshot.session; menuAnchor = control;
            var menu = element.querySelector('[data-control-menu]');
            var snapshot = controller.state().snapshot;
            var title = titles[menuGroup];
            text('[data-menu-title]', title); menu.setAttribute('aria-label', title + ' actions');
            var advancedButton = element.querySelector('[data-advanced]'); advancedButton.hidden = true;
            setText(advancedButton, snapshot.advanced ? 'Simple engine controls' : 'Advanced engine controls');
            advancedButton.disabled = false;
            element.querySelectorAll('[data-menu-pair]').forEach(button => { button.hidden = true; });
            var autoButton = element.querySelector('[data-auto]'); autoButton.hidden = true;
            var outputButton = element.querySelector('[data-check-output]');
            outputButton.hidden = true;
            outputButton.disabled = !snapshot[menuGroup + 'Blocked'] || snapshot.outputCheck && snapshot.outputCheck.writable === false || snapshot.shared && snapshot.shared.phase !== 0;
            var autoEnabled = !!snapshot.autoMask;
            autoButton.setAttribute('aria-checked', String(autoEnabled)); setText(autoButton, 'Auto takeover: ' + (autoEnabled ? 'on' : 'off'));
            var help = element.querySelector('[data-menu-help]');
            help.hidden = true;
            setText(help, 'One Auto switch for throttle, RPM and mixture. Movement over 2% requests only that pair; all its levers must align.');
            menu.hidden = false;
            controlElement(menuGroup).setAttribute('aria-expanded', 'true');
            var actions = element.querySelector('[data-menu-for="' + menuGroup + '"]'); if (actions) actions.setAttribute('aria-expanded', 'true');
            var container = element.querySelector('.fsc-controls');
            if (container.getBoundingClientRect && menu.getBoundingClientRect) {
                var box = container.getBoundingClientRect(), size = menu.getBoundingClientRect();
                var anchor = control.getBoundingClientRect();
                var x = event ? event.clientX : anchor.left + anchor.width / 2;
                var y = event ? event.clientY : anchor.bottom;
                menu.style.left = Math.max(4, Math.min(box.width - size.width - 4, x - box.left)) + 'px';
                menu.style.top = Math.max(4, Math.min(box.height - size.height - 4, y - box.top)) + 'px';
            }
            var button = element.querySelector('[data-force]'); if (button.focus) button.focus();
        }
        function contextmenu(event) {
            var control = eventTarget(event);
            if (!control || !(control.hasAttribute('data-control') || control.hasAttribute('data-menu-for'))) return;
            if (control.getAttribute('data-control') === 'quadrant') return;
            cancelPress(); suppressedClick = {control:control, until:Date.now()+800};
            event.preventDefault(); event.stopPropagation(); openMenu(control, event);
        }
        function secondaryButton(event) { return Number(event.button) === 2 || Number(event.which) === 3 || (Number(event.buttons) & 2) !== 0; }
        function mouseMenu(event) {
            // The simulator's embedded UI may deliver mouse buttons without
            // generating the browser contextmenu event. Support both down/up.
            if (secondaryButton(event)) contextmenu(event);
        }
        // Some embedded views target the entire SVG instead of its <g>/<rect>.
        // Resolve against the same rendered rectangles, including letterboxing
        // and seat swaps, without relying on SVG getScreenCTM/getBBox support.
        function eventPoint(event) {
            var point = event.touches && event.touches.length ? event.touches[0] : event;
            return Number.isFinite(point.clientX) && Number.isFinite(point.clientY) ? {clientX:point.clientX,clientY:point.clientY} : null;
        }
        function eventTarget(event) {
            var direct = actionTarget(event.target);
            if (direct && direct.getAttribute('data-control') !== 'quadrant') return direct;
            if (!element.querySelector('[data-setup]').hidden) return direct;
            var graphic = element.querySelector('[data-cockpit]'), point = eventPoint(event);
            if (!point || !graphic.getBoundingClientRect || !(graphic.contains(event.target) || event.target === element || event.target === graphic.parentNode)) return direct;
            var box = graphic.getBoundingClientRect(), scale = Math.min(box.width/900,box.height/680);
            if (!(scale > 0)) return direct;
            var x = (point.clientX-box.left-(box.width-900*scale)/2)/scale;
            var y = (point.clientY-box.top-(box.height-680*scale)/2)/scale;
            var rectangles = graphic.querySelectorAll('[data-seat-choice] > rect, [data-control] > .fsc-hit');
            for (var i=0;i<rectangles.length;i++) {
                var rect=rectangles[i], control=rect.parentNode;
                var translation=/^translate\(\s*(-?[\d.]+)[ ,]+(-?[\d.]+)\s*\)$/.exec(control.getAttribute('transform') || '');
                var left=Number(rect.getAttribute('x'))+(translation?Number(translation[1]):0);
                var top=Number(rect.getAttribute('y'))+(translation?Number(translation[2]):0);
                if(x>=left && x<=left+Number(rect.getAttribute('width')) && y>=top && y<=top+Number(rect.getAttribute('height')))return control;
            }
            return direct;
        }
        function setHover(control) {
            if (control && !(control.hasAttribute('data-seat-choice') || control.hasAttribute('data-control') && control.getAttribute('data-control') !== 'quadrant' && control.getAttribute('aria-disabled') !== 'true')) control=null;
            if (hovered===control) return;
            if (hovered) hovered.removeAttribute('data-hovered');
            hovered=control;
            if(hovered)hovered.setAttribute('data-hovered','true');
        }
        function movePointer(event) {
            var control=eventTarget(event);setHover(control);
            var point=eventPoint(event);
            if(heldPress && (control!==heldPress.control || !point || Math.abs(point.clientX-heldPress.point.clientX)>8 || Math.abs(point.clientY-heldPress.point.clientY)>8))cancelPress();
        }
        function cancelPress() {
            if(holdTimer!==null && root.clearTimeout)root.clearTimeout(holdTimer);
            holdTimer=null;heldPress=null;
        }
        function pressStart(event) {
            if(secondaryButton(event)){mouseMenu(event);return;}
            if(event.isPrimary===false || event.touches && event.touches.length!==1 || event.button!==undefined && Number(event.button)!==0)return;
            var control=eventTarget(event),state=controller.state(),point=eventPoint(event);
            if(!state.usable || !point || !control || !control.hasAttribute('data-control') || control.getAttribute('data-control')==='quadrant' || !root.setTimeout)return;
            if(heldPress && heldPress.control===control)return; // pointer + mouse compatibility events share one hold.
            cancelPress();heldPress={control:control,point:point,scope:forceScope(state.snapshot)};
            holdTimer=root.setTimeout(function(){
                var press=heldPress;holdTimer=null;
                if(!press || !controller.state().usable || forceScope(controller.state().snapshot)!==press.scope){cancelPress();return;}
                suppressedClick={control:press.control,until:Infinity};heldPress=null;
                openMenu(press.control,press.point);
            },650);
        }
        function pressEnd(event) {
            if(suppressedClick && suppressedClick.until===Infinity)suppressedClick.until=Date.now()+800;
            cancelPress();if(secondaryButton(event))mouseMenu(event);
        }
        function leavePointer(){
            if(suppressedClick && suppressedClick.until===Infinity)suppressedClick.until=Date.now()+800;
            cancelPress();setHover(null);
        }
        function dismissMenu(event) {
            if (menuGroup && !element.querySelector('[data-control-menu]').contains(event.target)) closeMenu(false);
        }
        function input(event) {
            if (event.target.hasAttribute('data-monitor-filter')) instrumentMonitor.paint();
            if (event.target.hasAttribute('data-connection-field')) render(controller.state());
        }
        function change(event) { if (event.target.hasAttribute('data-tolerance')) controller.setTolerance(event.target.getAttribute('data-tolerance'), Number(event.target.value)); else input(event); }
        var scrollBody = element.querySelector('[data-setup-scroll]');
        function scrollBy(amount) {
            var limit = Math.max(0, scrollBody.scrollHeight - scrollBody.clientHeight);
            scrollBody.scrollTop = Math.max(0, Math.min(limit, scrollBody.scrollTop + amount));
        }
        function wheel(event) {
            if (element.querySelector('[data-setup]').hidden || !event.deltaY) return;
            var unit = event.deltaMode === 1 ? 20 : event.deltaMode === 2 ? scrollBody.clientHeight : 1;
            scrollBy(event.deltaY * unit);
            event.preventDefault(); event.stopPropagation();
        }
        var touchY = null;
        function touchStart(event) { touchY = event.touches.length === 1 ? event.touches[0].clientY : null; }
        function touchMove(event) {
            if (touchY === null || event.touches.length !== 1) return;
            var next = event.touches[0].clientY;
            scrollBy(touchY - next); touchY = next;
            event.preventDefault(); event.stopPropagation();
        }
        function touchEnd() { touchY = null; }
        scrollBody.addEventListener('wheel', wheel, { passive: false });
        scrollBody.addEventListener('touchstart', touchStart, { passive: true });
        scrollBody.addEventListener('touchmove', touchMove, { passive: false });
        scrollBody.addEventListener('touchend', touchEnd);
        element.addEventListener('click', click); element.addEventListener('keydown', keydown); element.addEventListener('change', change);
        element.addEventListener('input', input);
        element.addEventListener('contextmenu', contextmenu);
        element.addEventListener('dblclick', doubleclick);
        ['mousemove','pointermove','mouseover','pointerover'].forEach(type=>element.addEventListener(type,movePointer));
        ['mousedown','pointerdown','touchstart'].forEach(type=>element.addEventListener(type,pressStart,true));
        ['mouseup','pointerup','touchend','pointercancel','touchcancel'].forEach(type=>element.addEventListener(type,pressEnd,true));
        element.addEventListener('mouseleave',leavePointer);element.addEventListener('pointerleave',leavePointer);
        element.ownerDocument.addEventListener('pointerdown', dismissMenu);
        element.ownerDocument.addEventListener('mousedown', dismissMenu);
        element.ownerDocument.addEventListener('click', outsideAssignment);
        var unsubscribe = controller.subscribe(render);
        return function () {
            cancelPress();setHover(null);
            unsubscribe(); element.removeEventListener('click', click); element.removeEventListener('keydown', keydown); element.removeEventListener('change', change);
            element.removeEventListener('input', input);
            element.removeEventListener('contextmenu', contextmenu); element.ownerDocument.removeEventListener('pointerdown', dismissMenu);
            element.removeEventListener('dblclick', doubleclick);
            ['mousemove','pointermove','mouseover','pointerover'].forEach(type=>element.removeEventListener(type,movePointer));
            ['mousedown','pointerdown','touchstart'].forEach(type=>element.removeEventListener(type,pressStart,true));
            ['mouseup','pointerup','touchend','pointercancel','touchcancel'].forEach(type=>element.removeEventListener(type,pressEnd,true));
            element.removeEventListener('mouseleave',leavePointer);element.removeEventListener('pointerleave',leavePointer);
            element.ownerDocument.removeEventListener('mousedown', dismissMenu);
            element.ownerDocument.removeEventListener('click', outsideAssignment);
            scrollBody.removeEventListener('wheel', wheel); scrollBody.removeEventListener('touchstart', touchStart);
            scrollBody.removeEventListener('touchmove', touchMove); scrollBody.removeEventListener('touchend', touchEnd);
            element.innerHTML = '';
        };
    }
    root.FscControls = Object.assign(root.FscControls || {}, { mount: mount });
}(typeof window !== 'undefined' ? window : globalThis));
