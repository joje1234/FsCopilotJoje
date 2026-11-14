const assert = require('assert').strict;
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
    return Object.assign({ version: 1, session: 'abc', revision: 1, client: '', request: 0,
        ready: true, applying: false, steeringBlocked: false, quadrantBlocked: false, error: '',
        steering: { seen: 0, blocked: 0, last: '', value: 0 },
        quadrant: { seen: 0, blocked: 0, last: '', value: 0 } }, changes);
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
    ['garbage', '{}', status({ version: 2 }), status({ ready: 'true' }), status({ session: 123 }), status({ quadrant: {} })]
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
    assert.equal(sent[0], '1|test-client|1|set|quadrant|mock');
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
    assert.equal(sent[1], '1|test-client|2|read');
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
    assert.equal(controller.snapshot.quadrantBlocked, true);
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
    assert.equal(intervalCount, 1);
    assert.equal(typeof app.render().type.prototype.onResume, 'function');
});

Promise.all(pendingTests).then(() => console.log(`${tests} JavaScript control/registration tests passed.`))
    .catch(error => { console.error(error); process.exitCode = 1; });
