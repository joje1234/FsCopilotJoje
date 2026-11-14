const assert = require('assert').strict;
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const scheduled = [];
let registrations = 0;
const context = { console, Map, Set, setTimeout(callback) { scheduled.push(callback); } };
context.window = context;
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../PackageSources/FsCopilotControls/FsCopilotControls.js'), 'utf8'), context);
assert.equal(scheduled.length, 1);
for (let i = 0; i < 200; i++) scheduled.shift()();
assert.equal(scheduled.length, 1); // Slow startup keeps retrying with one timer.
context.msfssdk = {
    DisplayComponent: class {},
    Subject: { create() {} },
    FSComponent: { createRef() {} }
};
let first = true;
context.EFB_API = { use(App) {
    if (first) { first = false; throw new Error('Bus has not been initialized yet'); }
    assert.equal(App.name, 'FsCopilotControls');
    registrations++;
} };
scheduled.shift()();
assert.equal(registrations, 0);
assert.equal(scheduled.length, 1);
scheduled.shift()();
assert.equal(registrations, 1);
assert.equal(scheduled.length, 0);
console.log('Delayed EFB registration passed: late globals, transient bus failure and exactly one registration.');
