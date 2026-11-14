import { readFileSync, writeFileSync } from 'fs';
import { fileURLToPath } from 'url';

const root = new URL('../', import.meta.url);
const config = JSON.parse(readFileSync(new URL('events.json', root), 'utf8'));
export const events = [];
for (const group of ['steering', 'quadrant']) {
    const names = [...config[group]];
    for (let engine = 1; engine <= config.perEngine.count; engine++) {
        for (const template of config.perEngine[group] || []) names.push(template.replace('{n}', engine));
    }
    for (const name of names) {
        if (!/^[A-Z0-9_]+$/.test(name) || events.some(event => event.name === name)) throw new Error(`Invalid/duplicate event: ${name}`);
        events.push({ group, name });
    }
}
export const header = '#pragma once\n\n// Generated from events.json by scripts/generate-events.mjs.\n' +
    'namespace fsc_controls {\nstruct event_binding { group target; const char* name; };\n' +
    'constexpr event_binding events[] = {\n' +
    events.map(event => `    {group::${event.group}, "${event.name}"},`).join('\n') +
    '\n};\nconstexpr size_t event_count = sizeof(events) / sizeof(events[0]);\n}\n';
if (process.argv[1] === fileURLToPath(import.meta.url)) {
    writeFileSync(new URL('Wasm/control_events.h', root), header);
    console.log(`Generated ${events.length} control event bindings.`);
}
