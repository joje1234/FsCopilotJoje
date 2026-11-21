const assert = require('assert').strict;
const withPairs = require('./status_fixture.cjs');
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const { Controller, validStatus } = require('../src/controller.js');

let tests = 0;
const pendingTests = [];
function test(name, run) {
    const result = run();
    tests++;
    if (result && typeof result.then === 'function') pendingTests.push(result.then(() => console.log('PASS ' + name)));
    else console.log('PASS ' + name);
}
function status(changes = {}) {
    return withPairs(Object.assign({ version: 5, session: 'abc', revision: 1, client: '', request: 0,
        ready: true, applying: false, steeringBlocked: false, quadrantBlocked: false, error: '',
        steering: { seen: 0, blocked: 0, last: '', value: 0 },
        quadrant: { seen: 0, blocked: 0, last: '', value: 0 },
        pickup: { steering: { waiting: false, tolerance: 3, axes: [] }, quadrant: { waiting: false, tolerance: 3, axes: [] } } }, changes));
}
function setup() {
    let now = 0;
    const sent = [];
    const transport = { send(packet) { sent.push(packet); }, subscribe() { return () => {}; } };
    const controller = new Controller(transport, { client: 'test-client', now: () => now });
    return { controller, transport, sent, advance(ms) { now += ms; } };
}

test('ownership commands are unavailable before a live simulator reply', () => {
    const { controller, sent } = setup();
    assert.equal(controller.state().usable, false);
    assert.equal(controller.setOwner('quadrant', 'mock'), false);
    assert.equal(sent.length, 0);
});
test('malformed/unsupported status never enables controls', () => {
    const { controller } = setup();
    ['garbage', '{}', status({ version: 6 }), status({ ready: 'true' }), status({ session: 123 }), status({ quadrant: {} })]
        .forEach(value => controller.receive(value));
    assert.equal(controller.snapshot, null);
});
test('native reply with terminating NUL is accepted', () => {
    const { controller } = setup();
    controller.receive(JSON.stringify(status()) + '\0');
    assert.equal(controller.state().usable, true);
});
test('selecting mock sends the whole group and waits for acknowledgement', () => {
    const { controller, sent } = setup();
    controller.receive(status());
    assert.equal(controller.setOwner('quadrant', 'mock'), true);
    assert.equal(sent[0], '5|test-client|1|set|quadrant|mock');
    assert.equal(controller.snapshot.quadrantBlocked, false);
    assert.equal(controller.state().usable, false);
    controller.receive(status({ client: 'test-client', request: 1, applying: true }));
    assert(controller.pending);
    controller.receive(status({ client: 'test-client', request: 1, revision: 2, quadrantBlocked: true }));
    assert.equal(controller.pending, null);
    assert.equal(controller.snapshot.quadrantBlocked, true);
    assert.equal(controller.snapshot.steeringBlocked, false);
});
test('periodic status and other clients cannot acknowledge our pending request', () => {
    const { controller } = setup();
    controller.receive(status());
    controller.setOwner('steering', 'mock');
    controller.receive(status({ revision: 2, steeringBlocked: true }));
    assert(controller.pending);
    controller.receive(status({ revision: 2, client: 'different', request: 1, steeringBlocked: true }));
    assert(controller.pending);
});
test('a newer read request does not acknowledge a mutation', () => {
    const { controller, sent } = setup();
    controller.receive(status());
    controller.restore();
    controller.tick();
    assert.equal(sent[1], '5|test-client|2|read');
    controller.receive(status({ client: 'test-client', request: 2 }));
    assert(controller.pending);
});
test('older revisions cannot undo a confirmed owner change', () => {
    const { controller } = setup();
    controller.receive(status({ revision: 3, quadrantBlocked: true }));
    controller.receive(status({ revision: 2 }));
    assert.equal(controller.snapshot.quadrantBlocked, true);
});
test('bridge restart clears pending commands and rejects retired sessions', () => {
    const { controller } = setup();
    controller.receive(status());
    controller.setOwner('quadrant', 'mock');
    controller.receive(status({ session: 'def', revision: 0 }));
    assert.equal(controller.pending, null);
    controller.receive(status({ session: 'abc', revision: 20, quadrantBlocked: true }));
    assert.equal(controller.snapshot.session, 'def');
    assert.equal(controller.snapshot.quadrantBlocked, false);
});
test('lost bridge disables mutations without inventing a restored owner', () => {
    const { controller, advance } = setup();
    controller.receive(status({ quadrantBlocked: true }));
    advance(3001);
    assert.equal(controller.state().online, false);
    assert.equal(controller.restore(), false);
    assert.equal(controller.forceTakeover('quadrant'), false);
    assert.equal(controller.snapshot.quadrantBlocked, true);
});

test('force takeover is scoped, validates the group, and waits for confirmation', () => {
    const { controller, sent } = setup();
    controller.receive(status({ steeringBlocked: true, quadrantBlocked: true }));
    assert.equal(controller.forceTakeover('all'), false);
    assert.equal(controller.forceTakeover('steering'), true);
    assert.equal(sent[0], '5|test-client|1|force|steering|me');
    assert.equal(controller.snapshot.steeringBlocked, true);
    assert.equal(controller.forceTakeover('quadrant'), false); // Busy.
    controller.receive(status({ revision: 2, client: 'test-client', request: 1, quadrantBlocked: true }));
    assert.equal(controller.snapshot.quadrantBlocked, true);
    assert.equal(controller.forceTakeover('quadrant'), true);
    assert.equal(sent[1], '5|test-client|2|force|quadrant|me');
});

test('aligned pickup request is acknowledged while hardware remains blocked, and cancellation uses mock', () => {
    const { controller, advance, sent } = setup();
    controller.receive(status({ quadrantBlocked: true }));
    controller.setOwner('quadrant', 'me');
    const waiting = status({ client: 'test-client', request: 1, revision: 2, quadrantBlocked: true });
    waiting.pickup.quadrant.waiting = true;
    controller.receive(waiting);
    assert.equal(controller.pending, null);
    assert.equal(controller.snapshot.quadrantBlocked, true);
    assert.equal(controller.snapshot.pickup.quadrant.waiting, true);
    advance(7000);
    controller.receive(waiting);
    controller.tick();
    assert.equal(controller.error, '');
    controller.setOwner('quadrant', 'mock');
    assert(sent.includes('5|test-client|3|set|quadrant|mock'));
});

test('alignment status rejects old bridges, invalid positions and unsafe tolerance values', () => {
    assert.equal(validStatus(status({ version: 1 })), false);
    assert.equal(validStatus(status({ pickup: null })), false);
    const packet = status();
    packet.pickup.steering.axes = [{ id: 'roll', label: 'Roll', input: 50, target: 50, calibrated: true, fresh: true, aligned: true }];
    assert(validStatus(packet));
    packet.pickup.steering.axes[0].target = NaN;
    assert.equal(validStatus(packet), false);
    const { controller, sent } = setup();
    controller.receive(status());
    assert.equal(controller.setTolerance('steering', 100), false);
    assert.equal(controller.setTolerance('steering', 5), true);
    assert.equal(sent[0], '5|test-client|1|tolerance|steering|5');
});
test('one unavailable control keeps the EFB live and other ownership commands usable', () => {
    for (const group of ['steering', 'throttle', 'propeller', 'mixture']) {
        const packet = status();
        packet.pickup[group].axes = [{ id: group === 'steering' ? 'roll' : group + '1', label: 'Test control',
            input: null, target: null, calibrated: true, fresh: false, aligned: false }];
        assert(validStatus(packet));
        const { controller } = setup(); controller.receive(packet);
        assert(controller.state().online && controller.state().usable);
        assert(controller.setOwner(group === 'steering' ? 'mixture' : 'steering', 'mock'));
    }
});
test('timeout reports uncertainty and never automatically retries a mutation', () => {
    const { controller, advance, sent } = setup();
    controller.receive(status());
    controller.setOwner('quadrant', 'mock');
    advance(6001);
    controller.tick();
    assert.equal(controller.pending, null);
    assert.match(controller.error, /No confirmation/);
    assert.equal(sent.filter(packet => packet.includes('|set|')).length, 1);
});
test('backend configuration failure is displayed and prevents further masking', () => {
    const { controller } = setup();
    controller.receive(status());
    controller.setOwner('quadrant', 'mock');
    controller.receive(status({ client: 'test-client', request: 1, ready: false, revision: 2, error: 'SimConnect error 3' }));
    assert.equal(controller.pending, null);
    assert.equal(controller.state().usable, false);
    assert.equal(controller.state().error, 'SimConnect error 3');
});

test('a command rejected during automatic attachment clears its pending acknowledgement', () => {
    const { controller } = setup();
    controller.receive(status({ quadrantBlocked: true }));
    controller.setTolerance('quadrant', 5);
    controller.receive(status({ client: 'test-client', request: 1, applying: true, quadrantBlocked: true, error: 'Bridge is busy; wait for the current change.' }));
    assert.equal(controller.pending, null);
    assert.equal(controller.snapshot.quadrantBlocked, true);
});
test('invalid groups/owners never produce simulator commands', () => {
    const { controller, sent } = setup();
    controller.receive(status());
    assert.equal(controller.setOwner('engine1', 'mock'), false);
    assert.equal(controller.setOwner('quadrant', 'anyone'), false);
    assert.equal(sent.length, 0);
});
test('a synchronous acknowledgement cannot be lost', () => {
    const { controller, transport } = setup();
    controller.receive(status());
    transport.send = () => controller.receive(status({ client: 'test-client', request: 1, revision: 2, quadrantBlocked: true }));
    controller.setOwner('quadrant', 'mock');
    assert.equal(controller.pending, null);
    assert.equal(controller.snapshot.quadrantBlocked, true);
});
test('transport failure leaves the confirmed state intact', () => {
    const { controller, transport } = setup();
    controller.receive(status());
    transport.send = () => { throw new Error('offline'); };
    assert.equal(controller.setOwner('quadrant', 'mock'), false);
    assert.equal(controller.pending, null);
    assert.equal(controller.snapshot.quadrantBlocked, false);
});
test('the generated EFB script registers through the runtime App contract', async () => {
    let App;
    let intervalCount = 0;
    const context = {
        console, Map, Set, Date, Math,
        setTimeout() {}, setInterval() { intervalCount++; return intervalCount; }, clearInterval() {},
        document: { getElementById() { return null; }, createElement() { return {}; }, head: { appendChild() {} } },
        msfssdk: {
            DisplayComponent: class {},
            FSComponent: { createRef() { return {}; }, buildComponent(type, props) { return { type, props }; } },
            Subject: { create(value) { return { value }; } }
        },
        EFB_API: { use(type) { App = type; } },
        Coherent: { trigger() {} }
    };
    context.window = context;
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../PackageSources/FsCopilotControls/FsCopilotControls.js'), 'utf8'), context);
    assert(App);
    const app = new App();
    assert.equal(app.internalName, 'FsCopilotControls');
    assert.equal(app.isReady, false);
    await app._install({ bus: {} });
    assert.equal(app.isReady, true);
    assert.equal(intervalCount, 0); // App installation/preloading must not start the native watchdog.
    const view = new (app.render().type)({});
    view.onOpen();
    assert.equal(intervalCount, 1);
    view.onResume();
    view.onOpen();
    assert.equal(intervalCount, 1); // Reopening does not add duplicate polling loops.
});

test('separate engine pairs share one quadrant Auto switch with native confirmation', () => {
    const { controller, sent } = setup();
    controller.receive(status());
    assert.equal(controller.availableGroup('mixture'), true);
    assert.equal(controller.setAuto('throttle', true), false);
    assert.equal(controller.setAuto(true), true);
    assert.equal(sent[0], '5|test-client|1|auto|quadrant|on');
    assert.equal(controller.snapshot.advanced, false);
    assert.equal(controller.snapshot.autoMask, 0);
    controller.receive(status({ autoMask: 14, revision: 2, client: 'test-client', request: 1 }));
    assert.equal(controller.setAuto('steering', true), false);
    assert.equal(controller.setAuto('quadrant', true), false);
    assert.equal(controller.setAuto('mixture', true), false);
    assert.equal(controller.setAdvanced(true), true);
    assert.equal(sent[1], '5|test-client|2|advanced|quadrant|on');
    controller.receive(status({ advanced: true, autoMask: 14, revision: 3, client: 'test-client', request: 2 }));
    assert.equal(controller.snapshot.pickup.mixture.autoEnabled, true);
    assert.equal(controller.setOwner('throttle', 'mock'), true);
    assert.equal(sent[2], '5|test-client|3|set|throttle|mock');
    controller.receive(status({ advanced: true, autoMask: 14, throttleBlocked: true, revision: 4, client: 'test-client', request: 3 }));
    assert.equal(controller.setAuto(false), true);
    assert.equal(sent[3], '5|test-client|4|auto|quadrant|off');
});
test('inconsistent group masks and Auto status cannot enable controls', () => {
    for (const packet of [status({ autoMask: 1 }), status({ autoMask: 8 }), status({ autoMask: 2 }), status({ quadrantMixed: true }),
        status({ quadrantBlocked: true, mixtureBlocked: false })]) assert.equal(validStatus(packet), false);
    const packet = status({ advanced: true, autoMask: 14 });
    packet.pickup.mixture.autoEnabled = false;
    assert.equal(validStatus(packet), false);
    assert(validStatus(status({ advanced: true, throttleBlocked: true })));
    assert(validStatus(status({ autoMask: 14, throttleBlocked: true })));
});
test('a pending automatic pair can be cancelled in Simple view without a per-pair mode', () => {
    const { controller, sent } = setup();
    const packet = status({ autoMask: 14, throttleBlocked: true }); packet.pickup.throttle.waiting = true;
    controller.receive(packet);
    assert.equal(controller.setOwner('throttle', 'mock'), true);
    assert.equal(sent[0], '5|test-client|1|set|throttle|mock');
    assert.equal(controller.snapshot.autoMask, 14);
});

test('global tolerance presets cycle 10, 20, 30 with acknowledgement and ownership preserved', () => {
    const { controller, sent } = setup();
    function preset(percent, revision, request = 0) {
        const packet = status({ revision, request, client: request ? 'test-client' : '', throttleBlocked: true });
        Object.values(packet.pickup).forEach(group => group.tolerance = percent); return packet;
    }
    controller.receive(preset(10, 1));
    for (const [index, percent] of [20, 30, 10].entries()) {
        assert.equal(controller.cycleTolerance(), true);
        assert.equal(sent.at(-1), '5|test-client|' + (index + 1) + '|tolerance|all|' + percent);
        assert.equal(controller.cycleTolerance(), false);
        assert.notEqual(controller.snapshot.pickup.steering.tolerance, percent); // No optimistic update.
        controller.receive(preset(percent, index + 2, index + 1));
        assert.equal(controller.snapshot.pickup.steering.tolerance, percent);
        assert.equal(controller.snapshot.throttleBlocked, true);
        assert.equal(controller.snapshot.propellerBlocked, false);
    }
    assert.equal(controller.setOwner('all', 'me'), false);
});

test('shared join and scoped cancellation await native status without changing confirmed masks', () => {
    const { controller, sent } = setup();
    const shared = { phase: 0, owners: [0, 0, 0, 0], attached: 0, pending: 0, waiting: 0, other: 0 };
    controller.receive(status({ shared }));
    assert(controller.sharedSession()); assert.equal(sent.at(-1), '5|test-client|1|join');
    assert.equal(controller.snapshot.shared.phase, 0);
    controller.receive(status({ shared: { ...shared, phase: 1 }, client: 'test-client', request: 1 }));
    assert(controller.sharedSession()); assert.equal(sent.at(-1), '5|test-client|2|leave');
    const active = status({ shared: { ...shared, phase: 3, owners: [2, 2, 2, 2], waiting: 2 }, advanced: true,
        throttleBlocked: true, client: 'test-client', request: 2 }); active.pickup.throttle.waiting = true;
    controller.receive(active);
    assert(controller.cancelTakeover('throttle')); assert.equal(sent.at(-1), '5|test-client|3|cancel|throttle|pending');
    assert.equal(controller.snapshot.throttleBlocked, true);
    assert.equal(controller.relearn('throttle'), false);
    controller.receive(status({ ...active, client: 'test-client', request: 3 }));
    assert(controller.setAuto(true)); assert.equal(sent.at(-1), '5|test-client|4|auto|quadrant|on');
    assert.equal(controller.snapshot.autoMask, 0);
    for (const malformed of [{ ...shared, owners: [1] }, { ...shared, waiting: 16 }, { ...shared, phase: 9 }, { ...shared, waiting: 2, other: 2 }])
        assert.equal(validStatus(status({ shared: malformed })), false);
});

test('connection setup validates, saves with native acknowledgement and never joins implicitly', () => {
    const { controller, sent } = setup();
    const connection = { api: 1, revision: 0, relay: '', port: 0, local: '', peer: '', role: 0, seat: 0, backend: 1, code: 0 };
    const shared = { phase: 0, owners: [0, 0, 0, 0], attached: 0, pending: 0, waiting: 0, other: 0 };
    controller.receive(status({ connection, shared }));
    assert.equal(controller.sharedSession(), false);
    const draft = { relay: '127.0.0.1', port: 3600, local: 'pilot001', peer: 'copil001', role: 1, seat: 2 };
    for (const invalid of [{ ...draft, local: 'PILOT001' }, { ...draft, peer: draft.local }, { ...draft, relay: 'bad/host' },
        { ...draft, port: 65536 }, { ...draft, seat: 3 }]) assert.equal(controller.setConnection(invalid), false);
    assert(controller.setConnection(draft)); assert.equal(sent.at(-1), '5|test-client|1|connect|127.0.0.1|3600|pilot001|copil001|host|copilot');
    assert.equal(controller.snapshot.connection.revision, 0); assert.equal(controller.snapshot.shared.phase, 0);
    const saved = { ...connection, ...draft, revision: 1, backend: 2 };
    controller.receive(status({ connection: saved, shared, client: 'test-client', request: 1 }));
    assert.equal(controller.snapshot.connection.seat, 2);
    controller.receive(status({ connection, shared })); assert.equal(controller.snapshot.connection.revision, 1);
    controller.receive(status({ shared })); assert.equal(controller.snapshot.connection.revision, 1);
    assert(controller.sharedSession());
    assert.equal(sent.at(-1), '5|test-client|2|join');
    controller.receive(status({ connection: saved, shared: { ...shared, phase: 1 }, client: 'test-client', request: 2 }));
    assert.equal(controller.setConnection(draft), false); assert(controller.sharedSession());
    assert.equal(sent.at(-1), '5|test-client|3|leave');
    controller.receive(status({ connection: saved, shared: { ...shared, phase: 4, holding: true, needsRecovery: true }, client: 'test-client', request: 3 }));
    assert.equal(controller.setConnection(draft), false); assert.equal(controller.sharedSession(), false);
    for (const invalid of [{ ...saved, api: 2 }, { ...saved, backend: 9 }, { ...saved, revision: -1 }, { ...saved, code: 8 },
        { ...connection, local: 'pilot001' }]) assert.equal(validStatus(status({ connection: invalid })), false);
});

test('seat swaps use confirmed revision and ticket, with scoped request/accept/decline/cancel', () => {
    const { controller, sent } = setup();
    const shared = { phase: 3, owners: [1, 1, 1, 1], attached: 15, pending: 0, waiting: 0, other: 0,
        seatRevision: 1, seat: 1, seatPending: 0, seatRequest: 0 };
    controller.receive(status({ shared }));
    assert(controller.changeSeat('request')); assert.equal(sent.at(-1), '5|test-client|1|seat|request|1|0');
    controller.receive(status({ shared: { ...shared, seatRevision: 2, seatPending: 1, seatRequest: 9 }, client: 'test-client', request: 1 }));
    assert.equal(controller.changeSeat('accept'), false); assert(controller.changeSeat('cancel'));
    assert.equal(sent.at(-1), '5|test-client|2|seat|cancel|2|9');
    controller.receive(status({ shared: { ...shared, seatRevision: 4, seatPending: 2, seatRequest: 10 }, client: 'test-client', request: 2 }));
    assert.equal(controller.changeSeat('cancel'), false); assert.equal(controller.changeSeat('request'), false);
    assert(controller.changeSeat('accept')); assert.equal(sent.at(-1), '5|test-client|3|seat|accept|4|10');
    controller.receive(status({ shared: { ...shared, seatRevision: 5, seat: 2 }, client: 'test-client', request: 3 }));
    assert.equal(controller.snapshot.shared.seat, 2); assert.equal(controller.changeSeat('decline'), false);
    for (const change of [{ seat: 0 }, { seatRequest: 1 }, { seatPending: 5 }, { seatRevision: -1 }, { seatRevision: 4294967296 }])
        assert.equal(validStatus(status({ shared: { ...shared, ...change } })), false);
});

test('altimeter observations preserve distinct tuned pressures and STD without issuing commands', () => {
    const { controller, sent } = setup();
    const altimeters = { serial: 2, fresh: true, pressure16: [15986, 16200], standard: [true, false] };
    controller.receive(status({ altimeters }));
    assert.deepEqual(controller.snapshot.altimeters, altimeters); assert.equal(sent.length, 0);
    for (const invalid of [{ ...altimeters, serial: 0 }, { ...altimeters, pressure16: [15986, 0] }, { ...altimeters, standard: [true] },
        { ...altimeters, standard: [1, false] }, { ...altimeters, fresh: 1 }]) assert.equal(validStatus(status({ altimeters: invalid })), false);
    assert(validStatus(status({ altimeters: { ...altimeters, fresh: false } })));
});

test('accepted seat swaps wait for simulator confirmation and retain scoped cancellation', () => {
    const { controller, sent } = setup();
    const shared = { phase: 3, owners: [1, 1, 1, 1], attached: 15, pending: 0, waiting: 0, other: 0,
        seatRevision: 3, seat: 1, seatPending: 3, seatRequest: 9 };
    controller.receive(status({ shared }));
    assert.equal(controller.changeSeat('accept'), false); assert(controller.changeSeat('cancel'));
    assert.equal(sent.at(-1), '5|test-client|1|seat|cancel|3|9');
    controller.receive(status({ shared: { ...shared, seatRevision: 4, seatPending: 4 }, client: 'test-client', request: 1 }));
    assert.equal(controller.changeSeat('accept'), false); assert.equal(controller.changeSeat('cancel'), false);
    assert(controller.changeSeat('decline')); assert.equal(sent.at(-1), '5|test-client|2|seat|decline|4|9');
});

test('shared session identity and monotonic native status reject delayed seat displays across rejoin', () => {
    const { controller } = setup();
    const shared = { phase: 3, owners: [1, 1, 1, 1], attached: 15, pending: 0, waiting: 0, other: 0,
        seatRevision: 5, seat: 2, seatPending: 0, seatRequest: 0, session: 'a'.repeat(32), serial: 100 };
    controller.receive(status({ shared }));
    controller.receive(status({ shared: { ...shared, serial: 101, seatRevision: 4, seat: 1 } }));
    assert.equal(controller.snapshot.shared.seat, 2);
    controller.receive(status({ shared: { ...shared, serial: 102, session: 'b'.repeat(32), seatRevision: 1, seat: 1 } }));
    assert.equal(controller.snapshot.shared.session, 'b'.repeat(32));
    controller.receive(status({ revision: 99, shared: { ...shared, serial: 101, phase: 4, seatRevision: 0, seat: 0 } }));
    assert.equal(controller.snapshot.shared.phase, 3); assert.equal(controller.snapshot.shared.seat, 1);
    const legacy = { ...shared }; delete legacy.session; delete legacy.serial;
    controller.receive(status({ shared: legacy })); assert.equal(controller.snapshot.shared.serial, 102);
    for (const invalid of [{ ...shared, session: '' }, { ...shared, serial: 0 }, { ...shared, serial: -1 }, { ...shared, session: 'invalid' }])
        assert.equal(validStatus(status({ shared: invalid })), false);
});

test('instrument recovery is scoped to the displayed session and remains independent of a physical mask operation', () => {
    const { controller, sent, advance } = setup();
    const shared = { phase: 3, owners: [1, 1, 1, 1], attached: 15, pending: 0, waiting: 0, other: 0,
        seatRevision: 1, seat: 1, seatPending: 0, seatRequest: 0, session: 'a'.repeat(32), serial: 1 };
    const altimeters = { serial: 10, fresh: true, pressure16: [15986, 16200], standard: [true, false],
        nativeFlags: 819, reported: true, sharedPhase: 4, pending: 0 };
    controller.receive(status({ applying: true, shared, altimeters }));
    assert.equal(controller.state().usable, false); assert(controller.canUseLocalInstruments());
    assert(controller.useLocalInstruments());
    assert.equal(sent.at(-1), '5|test-client|1|instruments|local|' + 'a'.repeat(32));
    assert.equal(controller.useLocalInstruments(), false);
    controller.receive(status({ applying: true, shared: { ...shared, serial: 2 }, altimeters: { ...altimeters, nativeFlags: 503 }, client: 'test-client', request: 1 }));
    assert.equal(controller.pending, null); assert.equal(controller.canUseLocalInstruments(), false);
    controller.receive(status({ shared: { ...shared, serial: 3 }, altimeters: { ...altimeters, nativeFlags: 579, reported: true, sharedPhase: 8 } }));
    assert.equal(controller.canUseLocalInstruments(), false);
    advance(3100); assert.equal(controller.useLocalInstruments(), false);
});
test('instrument UI metadata rejects contradictory release flags and accepts local input proof without a supported sync profile', () => {
    const altimeters = { serial: 10, fresh: true, pressure16: [15986, 16200], standard: [true, false],
        nativeFlags: 579, reported: false, sharedPhase: 0, pending: 0 };
    assert(validStatus(status({ altimeters })));
    for (const changes of [{ nativeFlags: 128 }, { nativeFlags: 1024 }, { nativeFlags: 67, fresh: false },
        { nativeFlags: 587 }, { reported: false, sharedPhase: 2 }, { pending: 4 }, { sharedPhase: 9 }])
        assert.equal(validStatus(status({ altimeters: { ...altimeters, ...changes } })), false);
});
test('AP observations retain actual values and target slots without claiming command support or sending events', () => {
    const { controller, sent } = setup();
    const guidance = { serial: 2, fresh: true, switches: 8191, heading: 359.995, altitude: 5000, verticalSpeed: -1500,
        gps: true, nav: 2, slots: [1, 1, 1], targetSlots: [1, 1, 1], outputAvailable: false };
    controller.receive(status({ guidance })); assert.deepEqual(controller.snapshot.guidance, guidance); assert.equal(sent.length, 0);
    for (const invalid of [{ serial: 0 }, { serial: -1 }, { switches: 8192 }, { heading: 360 }, { heading: NaN },
        { altitude: 100001 }, { verticalSpeed: -12001 }, { gps: 1 }, { nav: 3 }, { slots: [1] }, { slots: [1.5, 1, 1] },
        { targetSlots: [4, 1, 1] }, { fresh: 1 }, { outputAvailable: true }])
        assert.equal(validStatus(status({ guidance: { ...guidance, ...invalid } })), false);
    assert(validStatus(status({ guidance: { ...guidance, fresh: false, slots: [0, 2, 4] } })));
    assert(validStatus(status())); // Installed packets do not yet include these observations.
});
test('parked output checks are explicit, scoped and allow native Auto pause while refusing concurrent checks', () => {
    const { controller, sent } = setup();
    const outputCheck = { available: true, phase: 0, group: 'mixture', passed: 0, restored: false };
    controller.receive(status({ advanced: true, outputCheck }));
    assert.equal(controller.checkOutput('mixture'), false);
    controller.receive(status({ advanced: true, mixtureBlocked: true, outputCheck }));
    assert(controller.checkOutput('mixture'));
    assert(sent.at(-1).endsWith('|check-output|mixture|start'));
    controller.receive(status({ advanced: true, mixtureBlocked: true, client: controller.client, request: controller.nextRequest,
        outputCheck: { ...outputCheck, phase: 1 } }));
    assert.equal(controller.state().usable, false);
    assert.equal(controller.checkOutput('throttle'), false);
    controller.receive(status({ advanced: true, mixtureBlocked: true, autoMask: 14, outputCheck: { ...outputCheck, phase: 4, passed: 8, restored: true } }));
    assert.equal(controller.checkOutput('mixture'), true);
    assert(validStatus(status({outputCheck: {...outputCheck, passed: 9, saved: 9, storage: 'ready'}})));
    for (const bad of [{phase: 6}, {passed: 16}, {group: 'all'}, {available: 1}, {restored: 1}, {writable: 1},
        {storage: 'wrong'}, {saved: 1}, {saved: 16}, {saved: 0.5}, {saved: -1}])
        assert.equal(validStatus(status({outputCheck: {...outputCheck, ...bad}})), false);
});
test('setup reset needs native readiness, prevents duplicate pending requests and validates its status', () => {
    const {controller, sent} = setup();
    const wizard = {phase:0,group:'steering',needed:0,axis:21,ready:false,message:'',prerequisite:'Stop the engines before guided hardware setup.',resetReady:true,resetReason:''};
    controller.receive(status({wizard}));
    assert(!controller.startWizard()); assert(controller.resetSetup()); assert(sent.at(-1).endsWith('|reset-setup'));
    assert(!controller.resetSetup());
    controller.receive(status({wizard:{...wizard,resetReady:false,resetReason:'Return all controls to Me.'},client:controller.client,request:controller.nextRequest}));
    assert(!controller.resetSetup());
    for(const patch of [{resetReady:1},{resetReason:4},{resetReason:'x'.repeat(201)}])assert(!validStatus(status({wizard:{...wizard,...patch}})));
});
test('native wizard locks competing commands while permitting cancellation and explicit scoped recovery', () => {
    const {controller, sent} = setup();
    const wizard = {phase: 0, group: 'steering', needed: 0, axis: 21, ready: true, message: '', prerequisite: ''};
    controller.receive(status({wizard})); assert(controller.startWizard()); assert(sent.at(-1).endsWith('|wizard-start'));
    controller.receive(status({wizard: {...wizard, phase: 4}, steeringBlocked: true, client: controller.client, request: controller.nextRequest}));
    assert(!controller.state().usable); assert(!controller.setOwner('steering','me')); assert(!controller.setAuto(true));
    assert(!controller.relearn('steering')); assert(!controller.startWizard()); assert(controller.cancelWizard());
    assert(sent.at(-1).endsWith('|wizard-cancel'));
    controller.receive(status({wizard: {...wizard, phase: 5}, steeringBlocked: true, client: controller.client, request: controller.nextRequest}));
    assert(controller.forceWizardGroup()); assert(sent.at(-1).endsWith('|force|steering|me'));
    controller.receive(status({wizard: {...wizard, ready: false, prerequisite: 'Set the parking brake.'}, client: controller.client, request: controller.nextRequest}));
    assert(!controller.startWizard());
    for (const invalid of [{phase: 9}, {needed: 2097152}, {axis: 22}, {ready: 1}, {message: 'x'.repeat(201)}, {group:'quadrant'}])
        assert(!validStatus(status({wizard: {...wizard, ...invalid}})));
});
test('recovery and hardware binding use scoped native commands and reject unavailable actions', () => {
    const {controller,sent}=setup();
    controller.receive(status());assert(!controller.resyncCockpit());assert(!controller.configureForceButton('listen',2));
    const binding={saved:false,ready:false,armed:false,listening:false,captured:false,button:1,name:'',error:'',devices:[{id:2,name:'Test yoke'}]};
    const cockpit={available:true,sharing:true,count:218,known:209,result:0,recovery:{phase:0,checked:0,corrected:0,pending:0,failed:0,unavailable:0,issues:''}};
    const reply=(changes={})=>controller.receive(status({forceButton:binding,cockpit,client:controller.client,request:controller.nextRequest,...changes}));
    reply();assert(!controller.configureForceButton('listen',99));assert(controller.configureForceButton('listen',2));assert(sent.at(-1).endsWith('|force-button|listen|2'));
    reply();assert(controller.configureForceButton('cancel'));assert(sent.at(-1).endsWith('|force-button|cancel|0'));
    reply();assert(controller.resyncCockpit());assert(sent.at(-1).endsWith('|resync-cockpit'));
    reply({cockpit:{...cockpit,recovery:{...cockpit.recovery,phase:2}}});assert(!controller.resyncCockpit());
    assert(!validStatus(status({forceButton:{...binding,devices:[{id:-1,name:'bad'}]}})));
    assert(!validStatus(status({cockpit:{...cockpit,recovery:{...cockpit.recovery,corrected:1}}})));
});
Promise.all(pendingTests).then(() => console.log(`${tests} JavaScript control/registration tests passed.`))
    .catch(error => { console.error(error); process.exitCode = 1; });
