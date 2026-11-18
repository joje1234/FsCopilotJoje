const assert = require('assert').strict;
const withPairs = require('./status_fixture.cjs');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { parseHTML } = require(process.env.FSC_DOM_MODULE || 'linkedom');
const browser = parseHTML('<html><head></head><body><main id="app"></main></body></html>');
const document = browser.document;
const window = { document, console, Map, Set, Date, Math, FSC_CONTROLS_PREVIEW: true };
window.window = window;
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../PackageSources/FsCopilotControls/FsCopilotControls.js'), 'utf8'), window);
let now = 0;
const sent = [];
const controller = new window.FscControls.Controller({ send(packet) { sent.push(packet); } }, { client: 'dom-test', now: () => now });
const app = document.getElementById('app');
const unmount = window.FscControls.mount(app, controller);
const find = selector => app.querySelector(selector);
const click = selector => find(selector).dispatchEvent(new browser.Event('click', { bubbles: true }));
const commandButtons = () => [...app.querySelectorAll('[data-owner-choice], [data-learn]')];
const context = selector => find(selector).dispatchEvent(new browser.Event('contextmenu', { bubbles: true, cancelable: true }));
const mouse = (selector, type, fields = {}) => {
    const event = new browser.Event(type, { bubbles: true, cancelable: true });
    Object.assign(event, fields); find(selector).dispatchEvent(event);
};
function axis(id, changes = {}) {
    return Object.assign({ id, label: id, input: 50, target: 50, calibrated: true, fresh: true, aligned: true }, changes);
}
function status(changes = {}) {
    return withPairs(Object.assign({ version: 5, session: 'abc', revision: 1, client: '', request: 0,
        ready: true, advanced: true, applying: false, steeringBlocked: false, quadrantBlocked: false, error: '', learningStorage: 'ready',
        steering: { seen: 0, blocked: 0, last: '', value: 0 }, quadrant: { seen: 0, blocked: 0, last: '', value: 0 },
        pickup: {
            steering: { waiting: false, tolerance: 10, axes: [axis('roll'), axis('pitch')] },
            quadrant: { waiting: false, tolerance: 10, axes: [axis('throttle1'), axis('propeller1'), axis('mixture1'), axis('throttle2'), axis('propeller2'), axis('mixture2')] }
        } }, changes));
}
assert(find('svg'));
assert(!find('.fsc-intro') && !find('[data-restore]'));
assert(commandButtons().every(button => button.disabled));
assert.equal(find('[data-menu-for="quadrant"]').getAttribute('aria-disabled'), 'true');
click('[data-menu-for="quadrant"]'); assert(find('[data-control-menu]').hidden);
let nativeRevision = 0;
function receive(changes = {}) { controller.receive(status({revision: ++nativeRevision, ...changes})); }
function ack(changes = {}) { receive({client: controller.client, request: controller.nextRequest, ...changes}); }
receive();
for (const id of ['roll', 'pitch', 'rudder', 'leftBrake', 'rightBrake']) assert(find('[data-axis="' + id + '"]'));
assert.deepEqual([...app.querySelector('[data-axis-list="steering"]').children].map(row => row.getAttribute('data-axis')), ['roll', 'pitch', 'rudder', 'leftBrake', 'rightBrake']);
assert(find('[data-axis="rudder"] [data-axis-state]').textContent.includes('Input not detected'));
assert.equal(controller.snapshot.pickup.steering.axes.length, 2); // Placeholder rows do not invent measurements.
for (const group of ['throttle', 'propeller', 'mixture']) assert(find('[data-control="' + group + '"]'));
assert.equal(find('[data-control="quadrant"]').getAttribute('role'), 'group');
click('[data-seat-choice="mock"] .fsc-seat-back');
click('[data-axis-graphic="throttle1"] .fsc-graphic-track');
assert(sent.at(-1).endsWith('|set|throttle|mock'));
assert(find('[data-control="throttle"]').getAttribute('class').includes('fsc-owner-me'));
ack({throttleBlocked: true});
assert(find('[data-control="throttle"]').getAttribute('class').includes('fsc-owner-mock'));
assert(find('[data-control="mixture"]').getAttribute('class').includes('fsc-owner-me'));
click('[data-seat-choice="me"]');
const enter = new browser.Event('keydown', {bubbles: true, cancelable: true}); enter.key = 'Enter';
find('[data-control="throttle"]').dispatchEvent(enter);
assert(sent.at(-1).endsWith('|set|throttle|me'));
const waiting = status({revision: ++nativeRevision, client: controller.client, request: controller.nextRequest, throttleBlocked: true});
waiting.pickup.throttle.waiting = true;
waiting.pickup.throttle.axes[0] = axis('throttle1', {input: 20, target: 70, aligned: false});
controller.receive(waiting);
assert(!find('[data-cancel="throttle"]').hidden);
assert.equal(find('[data-axis-graphic="throttle1"] [data-graphic-state]').textContent, '↑');
click('[data-cancel="throttle"]'); assert(sent.at(-1).endsWith('|set|throttle|mock'));
ack({throttleBlocked: true});
const count = sent.length;
mouse('[data-axis-graphic="mixture1"] .fsc-graphic-track', 'mousedown', {button: 2});
assert(!find('[data-control-menu]').hidden && find('[data-menu-title]').textContent === 'Mixture');
assert(find('[data-auto]').hidden && sent.length === count);
click('[data-force]'); assert(sent.at(-1).endsWith('|force|mixture|me'));
ack({throttleBlocked: true});
click('[data-menu-for="quadrant"]');
assert(find('[data-advanced]').hidden); // Simple view has been removed.
assert(find('[data-auto]').hidden);
controller.setAuto(true); assert(sent.at(-1).endsWith('|auto|quadrant|on')); // Internal policy/wizard API remains available.
ack({throttleBlocked: true, autoMask: 14});
for (const group of ['throttle', 'propeller', 'mixture']) assert(find('[data-control="' + group + '"]'));
context('[data-control="mixture"]'); assert(find('[data-auto]').hidden && find('[data-menu-pair="mixture"]').hidden);
controller.setAuto(false); assert(sent.at(-1).endsWith('|auto|quadrant|off'));
ack({throttleBlocked: true});
assert(find('[data-control="throttle"]').getAttribute('class').includes('fsc-owner-mock')); // Disabling Auto does not seize controls.
const relative = status({revision: ++nativeRevision, steeringBlocked: true});
relative.pickup.steering.waiting = true;
relative.pickup.steering.axes.push(axis('rudder', {input: null, fixed: true, relative: true}),
    axis('leftBrake', {input: null, target: 50, fixed: true, relative: true, aligned: false}),
    axis('rightBrake', {input: null, target: 0, fixed: true, relative: true}));
controller.receive(relative);
assert(find('[data-summary="steering"]').textContent.includes('2/2 aligned'));
assert(find('[data-axis="rudder"] [data-axis-state]').textContent.includes('no alignment needed'));
assert(find('[data-rudder-pair]').getAttribute('class').includes('fsc-rudder-aligned'));
assert.equal(find('[data-pedal-input="left"]').getAttribute('visibility'), 'hidden');
assert(find('[data-axis="leftBrake"] [data-axis-state]').textContent.includes('no alignment needed'));
assert(find('[data-axis="rightBrake"] [data-axis-values]').textContent.includes('Local demand ?'));
assert.equal(controller.snapshot.pickup.steering.axes[2].input, null); // No blocked hardware position invented.
relative.revision = ++nativeRevision; relative.steeringBlocked = false;
controller.receive(relative);
assert(find('[data-axis="leftBrake"] [data-axis-state]').textContent.includes('Transfers with Steering'));
relative.revision = ++nativeRevision; relative.pickup.steering.axes[2].relative = false;
controller.receive(relative);
assert(find('[data-axis="rudder"] [data-axis-state]').textContent.includes('no alignment needed'));
const learning = status({revision: ++nativeRevision});
learning.pickup.steering.axes[0] = axis('roll', {input: null, calibrated: false});
controller.receive(learning); assert(find('[data-axis="roll"] [data-axis-state]').textContent.includes('Learning'));
assert.equal(find('[data-axis-graphic="roll"] [data-input]').getAttribute('visibility'), 'hidden');
learning.revision = ++nativeRevision; learning.pickup.steering.axes[0].saved = true; learning.pickup.steering.axes[0].calibrated = true;
controller.receive(learning); assert(find('[data-axis="roll"] [data-axis-state]').textContent.includes('Saved'));
const four = status({revision: ++nativeRevision}); four.pickup.quadrant.axes = [];
for (let engine = 1; engine <= 4; engine++) for (const type of ['throttle', 'propeller', 'mixture']) four.pickup.quadrant.axes.push(axis(type + engine));
controller.receive(four); assert.equal(app.querySelectorAll('[data-quadrant-axes] [data-axis-graphic]').length, 12);
receive(); assert.equal(app.querySelectorAll('[data-quadrant-axes] [data-axis-graphic]').length, 6);
context('[data-control="steering"]'); assert(!find('[data-control-menu]').hidden && find('[data-auto]').hidden);
const escape = new browser.Event('keydown', {bubbles: true, cancelable: true}); escape.key = 'Escape';
find('[data-force]').dispatchEvent(escape); assert(find('[data-control-menu]').hidden);
now = 4000; controller.notify(); assert(commandButtons().every(button => button.disabled));
assert.equal(find('[data-axis-graphic="throttle1"] [data-target]').getAttribute('visibility'), 'hidden');
unmount();
const presetPackets = [];
const presetController = new window.FscControls.Controller({ send(packet) { presetPackets.push(packet); } }, { client: 'preset-dom', now: () => 0 });
const presetUnmount = window.FscControls.mount(app, presetController);
function presetStatus(percent, request = 0) {
    const packet = status({ revision: request + 1, client: 'preset-dom', request, throttleBlocked: true });
    Object.values(packet.pickup).forEach(group => group.tolerance = percent); return packet;
}
assert(find('[data-cycle-tolerance]').disabled);
presetController.receive(presetStatus(10));
assert.equal(find('[data-cycle-tolerance]').textContent, 'Tolerance: Tight');
for (const [index, [percent, name]] of [[20, 'Medium'], [30, 'Loose'], [10, 'Tight']].entries()) {
    click('[data-cycle-tolerance]');
    assert.equal(presetPackets.at(-1), '5|preset-dom|' + (index + 1) + '|tolerance|all|' + percent);
    assert(find('[data-cycle-tolerance]').disabled);
    presetController.receive(presetStatus(percent, index + 1));
    assert.equal(find('[data-cycle-tolerance]').textContent, 'Tolerance: ' + name);
    assert.equal(find('[data-tolerance="steering"] option[selected]').value, String(percent));
    assert(Math.abs(Number(find('[data-axis-graphic="throttle1"] [data-band]').getAttribute('height')) - 134 * percent * 2 / 100) < 0.001);
    assert(find('[data-pair-group="throttle"]').getAttribute('class').includes('fsc-owner-mock'));
}
presetUnmount();
const sharedPackets = [];
const sharedController = new window.FscControls.Controller({ send(packet) { sharedPackets.push(packet); } }, { client: 'shared-dom', now: () => 0 });
const sharedUnmount = window.FscControls.mount(app, sharedController);
const shared = { phase: 0, owners: [0, 0, 0, 0], attached: 0, pending: 0, waiting: 0, other: 0 };
sharedController.receive(status({ shared }));
click('[data-settings]'); click('[data-shared-session]');
assert.equal(sharedPackets.at(-1), '5|shared-dom|1|join');
sharedController.receive(status({ shared: { ...shared, phase: 1 }, client: 'shared-dom', request: 1 }));
assert.equal(find('[data-shared-session]').textContent, 'Cancel join');
const pendingShared = status({ shared: { ...shared, phase: 3, owners: [1, 1, 2, 1], attached: 13, pending: 2, other: 2 },
    advanced: true, throttleBlocked: true, client: 'shared-dom', request: 1 });
sharedController.receive(pendingShared); click('[data-close-settings]');
assert.equal(find('[data-seat-choice="mock"] .fsc-seat-name').textContent, 'OTHER');
assert(find('[data-pair-group="throttle"]').getAttribute('class').includes('fsc-owner-me')); // Suppression is not peer ownership.
assert(find('[data-pair-group="throttle"]').getAttribute('class').includes('fsc-waiting'));
assert(find('[data-pair-group="propeller"]').getAttribute('class').includes('fsc-owner-mock'));
assert(find('[data-learn="throttle"]').disabled);
context('[data-control="throttle"]'); assert.equal(find('[data-auto]').hidden, true);
sharedController.setAuto(true); assert.equal(sharedPackets.at(-1), '5|shared-dom|2|auto|quadrant|on');
assert.equal(sharedController.snapshot.autoMask, 0);
sharedController.receive(status({ shared: pendingShared.shared, advanced: true, throttleBlocked: true, autoMask: 14, client: 'shared-dom', request: 2 }));
assert.equal(sharedController.snapshot.autoMask, 14);
sharedController.receive(status({shared:pendingShared.shared, advanced:true, throttleBlocked:true, autoMask:14,
    connection:{api:1,revision:1,relay:'server.local',port:3600,local:'pilot001',peer:'copil001',role:1,seat:1,backend:5,code:5}}));
assert(find('[data-shared-status]').textContent.includes('Shared session retained'));
assert.equal(find('[data-shared-session]').textContent, 'Leave shared session');
click('[data-settings]'); click('[data-shared-session]');
assert.equal(sharedPackets.at(-1), '5|shared-dom|3|leave');
sharedController.receive(status({ shared: { ...shared, phase: 4, holding: true }, advanced: true,
    connection:{...sharedController.snapshot.connection,backend:6,code:4}, throttleBlocked: true, client: 'shared-dom', request: 3 }));
assert.equal(find('[data-error]').hidden, false);
assert(find('[data-error]').textContent.includes('Force takeover on a control'));
click('[data-close-settings]'); context('[data-control="throttle"]'); click('[data-force]');
assert.equal(sharedPackets.at(-1), '5|shared-dom|4|force|throttle|me');
assert.equal(sharedController.snapshot.throttleBlocked, true);
sharedUnmount();
const configPackets = [];
const configController = new window.FscControls.Controller({ send(packet) { configPackets.push(packet); } }, { client: 'config-dom', now: () => now });
const configUnmount = window.FscControls.mount(app, configController);
const connection = { api: 1, revision: 0, relay: '', port: 0, local: '', peer: '', role: 0, seat: 0, backend: 1, code: 0 };
configController.receive(status({ connection, shared })); click('[data-settings]');
assert.equal(find('[data-connection-settings]').hidden, false);
assert(find('[data-shared-session]').disabled && find('[data-save-connection]').disabled);
find('[data-connection-field="local"]').value = 'pilot001'; find('[data-connection-field="peer"]').value = 'copil001';
find('[data-connection-field="relay"]').value = 'server.local';
assert.equal(find('select[data-connection-field="role"]'), null);
assert.equal(find('select[data-connection-field="seat"]'), null);
click('[data-connection-choice="role"][data-choice-value="2"]');
assert.equal(find('[data-connection-field="role"]').value, '2');
assert.equal(find('[data-connection-choice="role"][data-choice-value="2"]').getAttribute('aria-pressed'), 'true');
assert.equal(find('[data-connection-choice="role"][data-choice-value="1"]').getAttribute('aria-pressed'), 'false');
configController.receive(status({ connection, shared }));
assert.equal(find('[data-connection-field="role"]').value, '2'); // Periodic status must preserve a clicked draft.
click('[data-connection-choice="role"][data-choice-value="1"]');
click('[data-connection-choice="seat"][data-choice-value="2"]');
assert.equal(find('[data-connection-choice="seat"][data-choice-value="2"]').getAttribute('aria-pressed'), 'true');
assert.equal(configPackets.length, 0); // Choosing host/seat never saves, joins or changes controls.
find('[data-connection-field="peer"]').dispatchEvent(new browser.Event('input', { bubbles: true }));
assert.equal(find('[data-save-connection]').disabled, false);
configController.receive(status({ connection, shared }));
assert.equal(find('[data-connection-field="relay"]').value, 'server.local'); // Native display refresh preserves a draft.
click('[data-save-connection]');
assert.equal(configPackets.at(-1), '5|config-dom|1|connect|server.local|3600|pilot001|copil001|host|copilot');
assert.equal(configController.snapshot.connection.revision, 0); assert(find('[data-connection-field="peer"]').disabled);
assert([...app.querySelectorAll('[data-connection-choice]')].every(button => button.disabled));
click('[data-connection-choice="seat"][data-choice-value="1"]');
assert.equal(find('[data-connection-field="seat"]').value, '2');
const saved = { ...connection, revision: 1, relay: 'server.local', port: 3600, local: 'pilot001', peer: 'copil001', role: 1, seat: 2, backend: 2 };
configController.receive(status({ connection: saved, shared, client: 'config-dom', request: 1 }));
assert.equal(find('[data-seat-choice="me"]').getAttribute('transform'), 'translate(440 0)');
assert.equal(find('[data-shared-session]').disabled, false);
click('[data-connection-choice="role"][data-choice-value="2"]');
assert(find('[data-shared-session]').disabled); // Do not join with the previously saved role.
click('[data-connection-choice="role"][data-choice-value="1"]');
assert.equal(find('[data-shared-session]').disabled, false);
find('[data-connection-field="peer"]').value = 'changed1'; find('[data-connection-field="peer"]').dispatchEvent(new browser.Event('input', { bubbles: true }));
assert(find('[data-shared-session]').disabled); // Never join the previous code while displaying unsaved edits.
find('[data-connection-field="peer"]').value = 'copil001'; find('[data-connection-field="peer"]').dispatchEvent(new browser.Event('input', { bubbles: true }));
click('[data-shared-session]'); assert.equal(configPackets.at(-1), '5|config-dom|2|join');
configController.receive(status({ connection: saved, shared: { ...shared, phase: 1 }, client: 'config-dom', request: 2 }));
assert(find('[data-save-connection]').disabled && find('[data-connection-field="role"]').disabled);
assert([...app.querySelectorAll('[data-connection-choice]')].every(button => button.disabled));
click('[data-connection-choice="role"][data-choice-value="2"]');
assert.equal(find('[data-connection-field="role"]').value, '1');
assert.equal(find('[data-shared-session]').textContent, 'Cancel join');
const seats = { ...shared, phase: 3, seatRevision: 1, seat: 2, seatPending: 0, seatRequest: 0 };
configController.receive(status({ connection: saved, shared: seats }));
assert.equal(find('[data-seat-swap]').hidden, false);
assert.equal(find('[data-seat-action="request"]').disabled, false);
click('[data-seat-action="request"]'); assert.equal(configPackets.at(-1), '5|config-dom|3|seat|request|1|0');
configController.receive(status({ connection: saved, shared: { ...seats, seatRevision: 2, seatPending: 1, seatRequest: 4 }, client: 'config-dom', request: 3 }));
assert(find('[data-seat-action="accept"]').hidden); assert(!find('[data-seat-action="cancel"]').hidden);
configController.receive(status({ connection: saved, shared: { ...seats, seatRevision: 4, seatPending: 2, seatRequest: 5 } }));
assert(!find('[data-seat-action="accept"]').hidden && !find('[data-seat-action="decline"]').hidden);
click('[data-seat-action="accept"]'); assert.equal(configPackets.at(-1), '5|config-dom|4|seat|accept|4|5');
configController.receive(status({ connection: saved, shared: { ...seats, seatRevision: 5, seat: 1 }, client: 'config-dom', request: 4 }));
assert.equal(find('[data-seat-choice="me"]').getAttribute('transform'), 'translate(0 0)'); // Confirmed live seat overrides the saved next-join preference.
assert(find('[data-seat-status]').textContent.includes('Pilot'));
const beforeReadings = configPackets.length;
configController.receive(status({ connection: saved, shared: { ...seats, seatRevision: 5, seat: 1 },
    altimeters: { serial: 1, fresh: true, pressure16: [15986, 16200], standard: [true, false] } }));
assert(!find('[data-altimeters]').hidden);
assert(find('[data-altimeter="0"]').textContent.includes('Tuned 999.13 hPa · STD on'));
assert(find('[data-altimeter="1"]').textContent.includes('Tuned 1012.50 hPa · STD off'));
assert.equal(configPackets.length, beforeReadings);
configController.receive(status({ connection: saved, shared: { ...seats, seatRevision: 6, seat: 1, seatPending: 4, seatRequest: 6 } }));
assert(find('[data-seat-action="accept"]').hidden && !find('[data-seat-action="decline"]').hidden);
assert(find('[data-seat-status]').textContent.includes('simulator confirmation'));
assert.equal(find('[data-seat-choice="me"]').getAttribute('transform'), 'translate(0 0)');
const instrumentShared = { ...seats, seatRevision: 7, seat: 1, seatPending: 0, seatRequest: 0, session: 'c'.repeat(32), serial: 100 };
const instrumentValues = { serial: 20, fresh: true, pressure16: [15986, 16200], standard: [true, false],
    nativeFlags: 559, reported: true, sharedPhase: 2, pending: 0 };
configController.receive(status({ connection: saved, shared: instrumentShared, altimeters: instrumentValues }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · shared');
assert(find('[data-altimeter="0"]').textContent.includes('Pilot · left · Me'));
assert(find('[data-altimeter="1"]').textContent.includes('Copilot · right · Other pilot'));
assert(find('[data-altimeter="1"]').textContent.includes('Input held'));
assert.equal(find('[data-instruments-local]').disabled, false);
configController.receive(status({ connection: saved, shared: { ...instrumentShared, serial: 101 },
    altimeters: { ...instrumentValues, nativeFlags: 819, sharedPhase: 4 } }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · inputs held');
const beforeLocal = configPackets.length; click('[data-instruments-local]');
assert.equal(configPackets.length, beforeLocal + 1);
assert(configPackets.at(-1).endsWith('|instruments|local|' + 'c'.repeat(32)));
configController.receive(status({ connection: saved, shared: { ...instrumentShared, serial: 102 },
    altimeters: { ...instrumentValues, nativeFlags: 503, sharedPhase: 8 }, client: 'config-dom', request: configController.nextRequest }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · releasing inputs');
assert.equal(find('[data-instruments-local]').disabled, true);
configController.receive(status({ connection: saved, shared: { ...instrumentShared, serial: 103 },
    altimeters: { ...instrumentValues, nativeFlags: 579, sharedPhase: 8 } }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · local');
assert(find('[data-altimeter="0"]').textContent.includes('Input local'));
assert.equal(find('[data-instruments-local]').hidden, true);
configController.receive(status({ connection: saved, shared: { ...instrumentShared, serial: 104 },
    altimeters: { ...instrumentValues, nativeFlags: 515, sharedPhase: 2 } }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · local'); // Backend-only Active cannot invent a verified native assignment.
configController.receive(status({ connection: saved, shared: { ...instrumentShared, serial: 105 },
    altimeters: { ...instrumentValues, issue: 1 } }));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · sync stopped');
assert.equal(find('[data-altimeter="0"]').getAttribute('data-instrument-owner'), 'local');
configUnmount();
const checkPackets = [];
const checkController = new window.FscControls.Controller({send(packet) { checkPackets.push(packet); }}, {client: 'check-dom', now: () => now});
const checkUnmount = window.FscControls.mount(app, checkController);
const outputCheck = {available: true, phase: 0, group: 'mixture', passed: 0, restored: false};
checkController.receive(status({advanced: true, mixtureBlocked: true, outputCheck}));
assert(find('[data-output-check]').textContent.includes('0/4'));
assert(find('[data-output-check]').textContent.includes('Still needed: Steering, Throttle, RPM, Mixture.'));
// Every partial combination names exactly the missing groups, including during
// a running/failed diagnostic; a saved complete result needs no instructions.
for (let mask = 0; mask < 16; mask++) {
    checkController.receive(status({advanced: true, mixtureBlocked: true, outputCheck: {...outputCheck, passed: mask, saved: mask, storage: 'ready'}}));
    const text = find('[data-output-check]').textContent;
    const missing = ['Steering', 'Throttle', 'RPM', 'Mixture'].filter((name, index) => !(mask & (1 << index)));
    if (missing.length) assert(text.includes('Still needed: ' + missing.join(', ') + '.'));
    else { assert(text.includes('Complete and saved for this aircraft.')); assert(!text.includes('Mock')); assert(!text.includes('Still needed')); }
}
checkController.receive(status({advanced: true, mixtureBlocked: true, outputCheck: {...outputCheck, storage: 'unavailable'}}));
assert(find('[data-output-check]').textContent.includes('will not survive a restart'));
context('[data-control="mixture"]');
assert.equal(find('[data-check-output]').hidden, true);
assert.equal(find('[data-check-output]').disabled, false);
checkController.checkOutput('mixture'); // Output checks are orchestrated by the Setup wizard.
assert(checkPackets.at(-1).endsWith('|check-output|mixture|start'));
checkController.receive(status({advanced: true, mixtureBlocked: true, outputCheck: {...outputCheck, phase: 5, restored: true},
    client: 'check-dom', request: checkController.nextRequest}));
assert(find('[data-error]').textContent.includes('original cockpit position restored'));
assert(find('[data-output-check]').textContent.includes('Still needed: Steering, Throttle, RPM, Mixture.'));
checkUnmount();
const wizardPackets = [];
const wizardController = new window.FscControls.Controller({send(packet) {wizardPackets.push(packet);}}, {client:'wizard-dom', now:() => now});
let wizardUnmount = window.FscControls.mount(app, wizardController);
const wizard = {phase:0, group:'steering', needed:0, axis:21, ready:true, message:'', prerequisite:''};
function wizardReply(changes = {}) { wizardController.receive(status({wizard, outputCheck:{...outputCheck, saved:0}, ...changes})); }
wizardReply(); click('[data-settings]'); assert(!find('[data-wizard]').hidden);
click('[data-wizard-start]'); assert(wizardPackets.at(-1).endsWith('|wizard-start'));
wizardReply({wizard:{...wizard, phase:2, needed:3}, client:'wizard-dom', request:wizardController.nextRequest});
assert(find('[data-wizard-instruction]').textContent.includes('fixed simulator mappings; no manual learning or matching'));
assert(find('[data-owner-choice="me"]').disabled);
assert.equal(find('[data-wizard-axis="roll"] [data-state]').textContent, 'Move, then center yoke and hold');
click('[data-close-settings]'); click('[data-settings]'); assert(!find('[data-wizard-cancel]').hidden);
const countBeforeReload = wizardPackets.length;
wizardUnmount(); wizardUnmount = window.FscControls.mount(app, wizardController);
assert.equal(wizardPackets.length, countBeforeReload);
click('[data-settings]'); click('[data-wizard-cancel]'); assert(wizardPackets.at(-1).endsWith('|wizard-cancel'));
wizardReply({wizard:{...wizard, phase:7, axis:5, message:'The requested cockpit movement was not confirmed.'},
    outputCheck:{...outputCheck, group:'steering', phase:5, restored:true},
    pickup:{...status().pickup, steering:{waiting:false, autoEnabled:false, tolerance:10, axes:[axis('rightBrake',{label:'Right brake',input:0,target:0})]}},
    client:'wizard-dom', request:wizardController.nextRequest});
assert(find('[data-wizard-instruction]').textContent.includes('Right brake:'));
assert(find('[data-wizard-instruction]').textContent.includes('Original cockpit positions restored'));
assert(find('[data-wizard-instruction]').textContent.includes('Auto remains paused'));
wizardReply({wizard:{...wizard, phase:6}, outputCheck:{...outputCheck, phase:4, passed:15, saved:15, storage:'ready'}});
assert(find('[data-wizard-results]').textContent.includes('Steering: Saved'));
assert(find('[data-wizard-instruction]').textContent.includes('Setup complete'));
wizardReply({wizard:{...wizard,resetReady:true,resetReason:''},client:'wizard-dom',request:wizardController.nextRequest});
assert(!find('[data-setup-reset]').hidden && !find('[data-setup-reset]').disabled);
const beforeReset=wizardPackets.length;
click('[data-setup-reset]');assert(!find('[data-setup-reset-confirm]').hidden && wizardPackets.length===beforeReset);
click('[data-setup-reset-no]');assert(find('[data-setup-reset-confirm]').hidden && wizardPackets.length===beforeReset);
click('[data-setup-reset]');click('[data-setup-reset-yes]');assert(wizardPackets.at(-1).endsWith('|reset-setup'));
assert(find('[data-setup-reset-confirm]').hidden);
wizardReply({wizard:{...wizard,ready:false,prerequisite:'Stop the engines before guided hardware setup.',resetReady:true,resetReason:''},client:'wizard-dom',request:wizardController.nextRequest});
assert(find('[data-wizard-start]').disabled && find('[data-wizard-ready]').textContent.includes('Stop the engines'));
assert(!find('[data-setup-reset]').disabled);
wizardReply({wizard:{...wizard,resetReady:false,resetReason:'Return all controls to Me before resetting setup.'}});
assert(find('[data-setup-reset]').disabled && find('[data-setup-reset-reason]').textContent.includes('Return all controls to Me'));
wizardUnmount();
const ordinaryController = new window.FscControls.Controller({send() {}}, {client:'ordinary-dom', now:() => now});
const ordinaryUnmount = window.FscControls.mount(app, ordinaryController);
const ordinary = {available:true, sharing:true, count:200, known:180, result:0};
ordinaryController.receive(status({cockpit:ordinary, altimeters:instrumentValues}));
assert.equal(find('[data-altimeter-title]').textContent, 'Altimeters · shared definitions');
assert(find('[data-altimeter-status]').textContent.includes('Either pilot can adjust either instrument'));
assert(find('[data-instruments-local]').hidden);
assert.equal(find('[data-altimeter="0"]').getAttribute('data-instrument-owner'),'shared');
assert.equal(find('[data-altimeter="1"]').getAttribute('data-instrument-owner'),'shared');
ordinaryController.receive(status({cockpit:{...ordinary,result:4}, altimeters:instrumentValues}));
assert(find('[data-altimeter-status]').textContent.includes('not confirmed'));
ordinaryController.receive(status({cockpit:{...ordinary,sharing:false}, altimeters:instrumentValues}));
assert.equal(find('[data-altimeter-title]').textContent,'Altimeters · local');
let routeCallback,routeReads=0;
ordinaryController.configureRouteInspector({subscribe(callback){routeCallback=callback;callback({phase:'idle',message:'Ready to read.',summary:''});},inspect(){routeReads++;}});
click('[data-settings]');assert(!find('[data-navigation]').hidden);click('[data-read-avionics-route]');assert.equal(routeReads,1);
routeCallback({phase:'reading',message:'Reading avionics.',summary:''});assert(find('[data-read-avionics-route]').disabled);
routeCallback({phase:'observed',message:'Compare with the GTN.',summary:'ENBR → ENGM · 1 enroute entries'});
assert.equal(find('[data-navigation-summary]').textContent,'ENBR → ENGM · 1 enroute entries');
assert(!find('[data-read-avionics-route]').disabled);
now+=5000;ordinaryController.tick();
assert.equal(find('[data-altimeter-title]').textContent,'Altimeters · unconfirmed');
assert(find('[data-read-avionics-route]').disabled);
ordinaryUnmount();
// Dismissing assignment cancels only unfinished requests, including a click
// before the native set acknowledgement and multiple outstanding groups.
const cancelPackets=[];
const cancelController=new window.FscControls.Controller({send(packet){cancelPackets.push(packet);}}, {client:'cancel-dom',now:()=>now});
const cancelUnmount=window.FscControls.mount(app,cancelController);
let cancelRevision=0;
function cancelReply(changes={}) {cancelController.receive(status({revision:++cancelRevision,throttleBlocked:true,propellerBlocked:true,
    client:'cancel-dom',request:cancelController.nextRequest,...changes}));}
function waitingReply(throttle,rpm) {
    const value=status({throttleBlocked:true,propellerBlocked:true});
    value.pickup.throttle.waiting=throttle;value.pickup.propeller.waiting=rpm;
    cancelReply({pickup:value.pickup});
}
function outside(){document.body.dispatchEvent(new browser.Event('click',{bubbles:true}));}
cancelReply();
click('[data-control="throttle"]');assert.equal(cancelPackets.length,0); // A seat must be selected first.
click('[data-seat-choice="me"]');click('[data-seat-choice="me"]');
assert.equal(find('[data-seat-choice="me"]').getAttribute('aria-pressed'),'false');
click('[data-seat-choice="me"]');click('[data-control="throttle"]');
assert(cancelPackets.at(-1).endsWith('|set|throttle|me'));
outside();assert.equal(find('[data-seat-choice="me"]').getAttribute('aria-pressed'),'false');
assert.equal(cancelPackets.length,1); // Wait for the first native response before sending cancellation.
waitingReply(true,false);assert(cancelPackets.at(-1).endsWith('|set|throttle|mock'));
cancelReply();assert.equal(cancelPackets.length,2);
click('[data-seat-choice="me"]');click('[data-control="throttle"]');waitingReply(true,false);
click('[data-control="propeller"]');waitingReply(true,true);
outside();assert(cancelPackets.at(-1).endsWith('|set|throttle|mock'));
waitingReply(false,true);assert(cancelPackets.at(-1).endsWith('|set|propeller|mock'));
cancelReply();
click('[data-seat-choice="me"]');click('[data-control="throttle"]');cancelReply({throttleBlocked:false});
const confirmedCount=cancelPackets.length;outside();
assert.equal(cancelPackets.length,confirmedCount); // A completed transfer is never reversed by dismissal.
assert(find('[data-control="throttle"]').getAttribute('class').includes('fsc-owner-me'));
cancelReply();click('[data-seat-choice="me"]');click('[data-control="throttle"]');
find('[data-control="throttle"]').dispatchEvent(new browser.Event('dblclick',{bubbles:true,cancelable:true}));
const queuedForceCount=cancelPackets.length;
cancelReply({shared:{phase:4,owners:[0,0,0,0],attached:0,pending:0,waiting:0,other:0}});
assert.equal(cancelPackets.length,queuedForceCount); // A queued Force cannot cross a changed shared-flight context.
const forceBinding={saved:false,ready:false,armed:false,listening:false,captured:false,button:1,name:'',error:'',devices:[{id:2,name:'Test <Yoke>'}]};
cancelReply({forceButton:forceBinding});
click('[data-settings]');assert(!find('[data-force-button]').hidden);
assert.equal(find('[data-force-device] option').textContent,'Test <Yoke> (2)');
find('[data-force-device] option').setAttribute('selected','');
click('[data-force-binding="listen"]');assert(cancelPackets.at(-1).endsWith('|force-button|listen|2'));
cancelReply({forceButton:{...forceBinding,listening:true,captured:true}});
assert(find('[data-force-binding-status]').textContent.includes('Release the button'));
click('[data-force-binding="cancel"]');assert(cancelPackets.at(-1).endsWith('|force-button|cancel|0'));
cancelReply({cockpit:{available:true,sharing:true,count:218,known:209,result:0,recovery:{phase:4,checked:208,corrected:1,pending:0,failed:1,unavailable:9,issues:'Needs attention: Left altimeter'}}});
assert(find('[data-recovery-status]').textContent.includes('Left altimeter'));
click('[data-resync-cockpit]');assert(cancelPackets.at(-1).endsWith('|resync-cockpit'));
const monitored={profile:'abc',sequence:1,catalog:[{index:0,name:'A:AUTOPILOT HEADING LOCK DIR',units:'degrees',shared:true,value:170},{index:1,name:'L:HOST_SWITCH',units:'Number',shared:false,value:null}],changes:[{index:0,sequence:1,before:100,value:170}]};
const cockpitMonitor=monitor=>({available:true,sharing:false,count:2,known:1,result:0,monitor});
cancelReply({cockpit:cockpitMonitor(monitored)});
assert(!find('[data-instrument-monitor]').hidden);
const monitorPackets=cancelPackets.length;
click('[data-monitor-action="open"]');
assert(!find('[data-monitor-body]').hidden&&find('[data-monitor-status]').textContent.includes('Local coverage check'));
assert(find('[data-monitor-changes]').textContent.includes('100 → 170')&&find('[data-monitor-changes]').textContent.includes('Covered · Both pilots'));
find('[data-monitor-filter]').value='HOST';find('[data-monitor-filter]').dispatchEvent(new browser.Event('input',{bubbles:true}));
assert(find('[data-monitor-matches]').textContent.includes('Host → copilot')&&find('[data-monitor-matches]').textContent.includes('Readout unavailable'));
click('[data-monitor-action="pause"]');
cancelReply({cockpit:cockpitMonitor({...monitored,sequence:2,changes:[{index:0,sequence:2,before:170,value:190}]})});
assert(find('[data-monitor-changes]').textContent.includes('100 → 170'));
click('[data-monitor-action="clear"]');assert(!find('[data-monitor-changes]').textContent.includes('100 → 170'));
click('[data-monitor-action="pause"]');
cancelReply({cockpit:cockpitMonitor({...monitored,sequence:3,changes:[{index:0,sequence:3,before:190,value:200}]})});
assert(find('[data-monitor-changes]').textContent.includes('190 → 200'));
cancelReply({cockpit:cockpitMonitor({...monitored,catalog:[...monitored.catalog,monitored.catalog[0]]})});
assert(cancelController.state().usable&&find('[data-monitor-status]').textContent.includes('data is unavailable'));
assert.equal(cancelPackets.length,monitorPackets); // Monitor buttons/search never send a native or network mutation.
cancelReply({cockpit:cockpitMonitor({...monitored,profile:'def',catalog:[],changes:[]})});
assert(!find('[data-monitor-matches]').textContent.includes('Host → copilot'));
cancelUnmount();
console.log('Cockpit DOM passed: seat/graphic assignment, keyboard pickup, confirmed owner colors, inline alignment, cancellation, stale state, engine layout and setup.');
