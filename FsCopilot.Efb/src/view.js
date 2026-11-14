(function (root) {
    'use strict';

    var cockpit = '<svg viewBox="0 0 460 460" aria-label="Cockpit viewed from above" role="img">' +
        '<path class="fsc-shell" d="M75 412V145C75 72 140 30 230 30S385 72 385 145V412"/>' +
        '<path class="fsc-panel" d="M94 140Q230 60 366 140L354 173H106Z"/>' +
        '<path class="fsc-window" d="M113 112Q155 57 217 53V104L160 125Z M243 53Q305 57 347 112L300 125L243 104Z"/>' +
        '<g class="fsc-seat" data-seat="me"><rect x="108" y="258" width="104" height="119" rx="25"/>' +
        '<rect x="99" y="340" width="122" height="39" rx="15"/><text x="160" y="306">ME</text><text x="160" y="329">LEFT PILOT</text></g>' +
        '<g class="fsc-seat" data-seat="mock"><rect x="248" y="258" width="104" height="119" rx="25"/>' +
        '<rect x="239" y="340" width="122" height="39" rx="15"/><text x="300" y="306">MOCK</text><text x="300" y="329">COPILOT</text></g>' +
        '<g class="fsc-steering" data-diagram="steering"><path d="M124 197v19q36 25 72 0v-19M160 218v28M264 197v19q36 25 72 0v-19M300 218v28"/>' +
        '<rect x="122" y="151" width="27" height="13" rx="3"/><rect x="172" y="151" width="27" height="13" rx="3"/>' +
        '<rect x="262" y="151" width="27" height="13" rx="3"/><rect x="312" y="151" width="27" height="13" rx="3"/></g>' +
        '<g class="fsc-quadrant" data-diagram="quadrant"><rect x="210" y="179" width="40" height="86" rx="7"/>' +
        '<path d="M217 189v59M222 189v59M228 189v59M233 189v59M239 189v59M244 189v59"/>' +
        '<path class="fsc-lever-throttle" d="M217 205h5"/><path class="fsc-lever-prop" d="M228 218h5"/>' +
        '<path class="fsc-lever-mixture" d="M239 198h5"/></g>' +
        '<text class="fsc-diagram-label" x="230" y="421">LOCAL CONTROL TEST</text></svg>';

    function card(group, title, description) {
        return '<section class="fsc-card" data-card="' + group + '">' +
            '<div class="fsc-card-heading"><h2>' + title + '</h2><span class="fsc-owner" data-owner="' + group + '">Waiting</span></div>' +
            '<p class="fsc-description">' + description + '</p>' +
            '<div class="fsc-selector" role="group" aria-label="' + title + ' owner">' +
            '<button type="button" data-group="' + group + '" data-owner-choice="me" aria-pressed="false" disabled>Me</button>' +
            '<button type="button" data-group="' + group + '" data-owner-choice="mock" aria-pressed="false" disabled>Mock copilot</button></div>' +
            '<p class="fsc-result" data-result="' + group + '">Waiting for simulator confirmation.</p>' +
            '<div class="fsc-activity"><span>Events seen <strong data-seen="' + group + '">0</strong></span>' +
            '<span>Blocked <strong data-blocked="' + group + '">0</strong></span></div>' +
            '<p class="fsc-last" data-last="' + group + '">Move a control to identify its event.</p></section>';
    }

    function mount(element, controller, options) {
        options = options || {};
        element.innerHTML = '<div class="fsc-controls">' +
            '<header class="fsc-header"><div><p class="fsc-eyebrow">FS COPILOT</p><h1>Cockpit controls</h1></div>' +
            '<span class="fsc-connection" data-connection>Waiting for bridge</span></header>' +
            (options.preview ? '<p class="fsc-preview">Browser preview. These buttons do not affect simulator hardware.</p>' : '') +
            '<p class="fsc-intro">Assign a group to the mock copilot, then move your hardware to test that the aircraft no longer responds.</p>' +
            '<div class="fsc-main"><div class="fsc-overview">' + cockpit +
            '<p class="fsc-caption">One owner for steering.<br>One owner for the complete engine quadrant.</p></div>' +
            '<div class="fsc-cards">' +
            card('steering', 'Steering', 'Yoke, rudder, nosewheel, brakes and trim') +
            card('quadrant', 'Engine quadrant', 'Both throttles, propellers and mixtures') +
            '</div></div>' +
            '<p class="fsc-error" data-error role="alert" hidden></p>' +
            '<footer class="fsc-footer"><button class="fsc-restore" type="button" data-restore disabled>Restore both to Me</button>' +
            '<p>Restoring Me reconnects controls immediately at the hardware\'s current position.</p></footer>' +
            '<p class="fsc-note">This test blocks standard simulator control events, including keyboard or cockpit actions using the same events. ' +
            'If a control still moves, note its event below and report it. The mock copilot does not fly the aircraft.</p></div>';

        function text(selector, value) { element.querySelector(selector).textContent = value; }
        function render(state) {
            var snapshot = state.snapshot;
            var connection = !state.online ? 'Waiting for bridge' :
                snapshot.applying || state.pending ? 'Applying change...' : snapshot.ready ? 'Simulator connected' : 'Bridge error';
            text('[data-connection]', connection);
            element.querySelector('[data-connection]').className = 'fsc-connection' + (state.usable ? ' fsc-connected' : '');
            ['steering', 'quadrant'].forEach(function (group) {
                var known = state.online && snapshot && snapshot.ready;
                var blocked = snapshot && snapshot[group + 'Blocked'];
                text('[data-owner="' + group + '"]', known ? (blocked ? 'Mock copilot' : 'Me') : 'Unconfirmed');
                text('[data-result="' + group + '"]', !known ? 'Waiting for simulator confirmation.' :
                    blocked ? 'Input mask active. Move hardware and check the aircraft.' : 'Your controls are enabled.');
                element.querySelector('[data-card="' + group + '"]').className = 'fsc-card' + (known && blocked ? ' fsc-mock' : '');
                element.querySelector('[data-diagram="' + group + '"]').setAttribute('class', 'fsc-' + group + (known && blocked ? ' fsc-mock' : ''));
                var choices = element.querySelectorAll('[data-group="' + group + '"]');
                for (var i = 0; i < choices.length; i++) {
                    var button = choices[i];
                    button.disabled = !state.usable;
                    button.setAttribute('aria-pressed', known && button.getAttribute('data-owner-choice') === (blocked ? 'mock' : 'me') ? 'true' : 'false');
                }
                if (snapshot) {
                    var activity = snapshot[group];
                    text('[data-seen="' + group + '"]', activity.seen);
                    text('[data-blocked="' + group + '"]', activity.blocked);
                    text('[data-last="' + group + '"]', activity.last ? activity.last + ' = ' + activity.value : 'Move a control to identify its event.');
                }
            });
            element.querySelector('[data-restore]').disabled = !state.usable;
            var error = element.querySelector('[data-error]');
            error.hidden = !state.error;
            error.textContent = state.error;
        }

        function click(event) {
            var button = event.target;
            while (button && button !== element && button.tagName !== 'BUTTON') button = button.parentNode;
            if (!button || button === element || button.disabled) return;
            if (button.hasAttribute('data-restore')) controller.restore();
            else controller.setOwner(button.getAttribute('data-group'), button.getAttribute('data-owner-choice'));
        }
        element.addEventListener('click', click);
        var unsubscribe = controller.subscribe(render);
        return function () {
            unsubscribe();
            element.removeEventListener('click', click);
            element.innerHTML = '';
        };
    }

    root.FscControls.mount = mount;
})(typeof window !== 'undefined' ? window : globalThis);
