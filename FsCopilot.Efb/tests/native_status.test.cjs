// Cross-language contract: validate the actual module's largest serialized
// packet with the production JavaScript parser, including duplicate aggregate
// and per-pair diagnostics for a four-engine aircraft.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { validStatus } = require('../src/controller.js');
require('../src/instrument-monitor.js');
const log = process.argv[2] || path.resolve(__dirname, '../../artifacts/efb-control-test/tests/native_test.log');
const bytes = fs.readFileSync(log);
const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf16le' : 'utf8';
const line = bytes.toString(encoding).split(/\r?\n/).find(line => line.startsWith('STATUS '));
assert(line, 'Run native_test and capture its output first');
const packet = JSON.parse(line.slice(7));
assert(validStatus(packet), 'Native maximum status rejected by production UI');
assert(globalThis.FscControls.validInstrumentMonitor(packet.cockpit.monitor, packet.cockpit.count), 'Native monitor status rejected by production UI');
assert.equal(packet.cockpit.monitor.catalog.length,16);
assert.equal(packet.cockpit.monitor.changes.length,12);
assert.equal(packet.version, 5);
assert(packet.guidance.fresh && packet.guidance.serial > 0);
assert.equal(packet.guidance.switches, 8191);
assert.equal(packet.guidance.heading, 359.995);
assert.equal(packet.guidance.outputAvailable, false);
assert.deepEqual(packet.guidance.slots, [4, 4, 4]);
assert.deepEqual(packet.guidance.targetSlots, [1, 1, 1]);
assert.equal(packet.pickup.steering.axes.length, 6);
assert.equal(packet.pickup.quadrant.axes.length, 12);
for (const group of ['throttle', 'propeller', 'mixture']) {
    assert.equal(packet.pickup[group].axes.length, 4);
    assert(packet.pickup[group].axes.every(axis => axis.id.startsWith(group)));
}
assert(Buffer.byteLength(line.slice(7)) < 16384);
console.log('Actual native protocol v5 maximum status accepted by production UI: 6 steering and 12 engine axes; trims use ordinary instrument sync.');
