// Optional DOM integration test: set FSC_DOM_MODULE to an installed linkedom.
// It is a test-only dependency; the shipped EFB package has no dependencies.
const assert = require('assert').strict;
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { parseHTML } = require(process.env.FSC_DOM_MODULE || 'linkedom');
const { document } = parseHTML('<html><head></head><body><main id="app"></main></body></html>');
const window = { document, console, Map, Set, Date, Math, FSC_CONTROLS_PREVIEW: true };
window.window = window;
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../PackageSources/FsCopilotControls/FsCopilotControls.js'), 'utf8'), window);
let now = 0;
const sent = [];
const controller = new window.FscControls.Controller({ send(packet) { sent.push(packet); } }, { client: 'dom-test', now: () => now });
const app = document.getElementById('app');
const unmount = window.FscControls.mount(app, controller);
const buttons = () => [...app.querySelectorAll('button')];
function status(changes) {
    return Object.assign({ version: 1, session: 'abc', revision: 1, client: '', request: 0,
        ready: true, applying: false, steeringBlocked: false, quadrantBlocked: false, error: '',
        steering: { seen: 0, blocked: 0, last: '', value: 0 }, quadrant: { seen: 0, blocked: 0, last: '', value: 0 } }, changes);
}
assert(buttons().every(button => button.disabled));
assert(app.querySelector('svg'));
controller.receive(status());
assert(buttons().every(button => !button.disabled));
assert.equal(app.querySelector('[data-owner="quadrant"]').textContent, 'Me');
app.querySelector('[data-group="quadrant"][data-owner-choice="mock"]').click();
assert.equal(sent[0], '1|dom-test|1|set|quadrant|mock');
assert.equal(app.querySelector('[data-owner="quadrant"]').textContent, 'Me');
assert(buttons().every(button => button.disabled));
controller.receive(status({ client: 'dom-test', request: 1, revision: 2, quadrantBlocked: true,
    quadrant: { seen: 100, blocked: 88, last: 'AXIS_THROTTLE1_SET', value: -500 } }));
assert.equal(app.querySelector('[data-owner="quadrant"]').textContent, 'Mock copilot');
assert.equal(app.querySelector('[data-owner="steering"]').textContent, 'Me');
assert.equal(app.querySelector('[data-blocked="quadrant"]').textContent, '88');
assert(app.querySelector('[data-diagram="quadrant"]').getAttribute('class').includes('fsc-mock'));
app.querySelector('[data-restore]').click();
assert.equal(sent[1], '1|dom-test|2|restore');
controller.receive(status({ client: 'dom-test', request: 2, revision: 3 }));
assert.equal(app.querySelector('[data-owner="quadrant"]').textContent, 'Me');
now = 4000;
controller.notify();
assert(buttons().every(button => button.disabled));
assert.equal(app.querySelector('[data-owner="quadrant"]').textContent, 'Unconfirmed');
controller.receive(status({ ready: false, revision: 4, error: 'SimConnect configuration failed' }));
assert.equal(app.querySelector('[data-error]').hidden, false);
assert.equal(app.querySelector('[data-error]').textContent, 'SimConnect configuration failed');
unmount();
assert.equal(app.innerHTML, '');
assert.equal(controller.listeners.size, 0);
console.log('DOM integration passed: offline state, group buttons, confirmation, counters, restore and cleanup.');
