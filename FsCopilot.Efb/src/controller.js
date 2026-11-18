(function (root) {
    'use strict';

    var COMMAND_EVENT = 'FSC_CONTROLS_TEST_COMMAND';
    var STATUS_EVENT = 'FSC_CONTROLS_TEST_STATUS';
    var groups = ['steering', 'quadrant', 'throttle', 'propeller', 'mixture'];
    var pairs = ['throttle', 'propeller', 'mixture'];
    function sharedActive(snapshot) { return !!(snapshot && snapshot.shared && [2, 3].includes(snapshot.shared.phase)); }
    function sharedGroup(snapshot, group) {
        if (!sharedActive(snapshot)) return null;
        var shared = snapshot.shared, mask = group === 'steering' ? 1 : group === 'quadrant' ? 14 : 2 << pairs.indexOf(group);
        var owners = shared.owners.filter((owner, index) => mask & (1 << index));
        return { owner: owners.every(owner => owner === 1) ? 'me' : owners.every(owner => owner === 2) ? 'mock' : null,
            mixed: new Set(owners).size > 1, attached: (shared.attached & mask) === mask,
            transferring: !!((shared.pending | shared.waiting | shared.other) & mask) };
    }
    function commonTolerance(snapshot) {
        if (!snapshot) return null;
        var values = ['steering'].concat(pairs).map(group => snapshot.pickup[group].tolerance);
        return values.every(value => value === values[0]) ? values[0] : null;
    }

    function validActivity(value) {
        return value && Number.isSafeInteger(value.seen) && value.seen >= 0 &&
            Number.isSafeInteger(value.blocked) && value.blocked >= 0 &&
            typeof value.last === 'string' && typeof value.value === 'number';
    }

    function validConnectionSettings(value) {
        return !!(value && typeof value.relay === 'string' && value.relay.length > 0 && value.relay.length <= 127 &&
            value.relay.split('.').every(label => label.length > 0 && label.length <= 63 && /^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$/.test(label)) &&
            Number.isInteger(value.port) && value.port >= 1 && value.port <= 65535 &&
            typeof value.local === 'string' && /^[a-z0-9]{8}$/.test(value.local) && typeof value.peer === 'string' && /^[a-z0-9]{8}$/.test(value.peer) &&
            value.local !== value.peer && [1, 2].includes(value.role) && [1, 2].includes(value.seat));
    }
    function validStatus(value) {
        var connection = value && value.connection;
        if (connection !== undefined && (!connection || connection.api !== 1 || !Number.isInteger(connection.revision) || connection.revision < 0 || connection.revision > 4294967295 ||
            !Number.isInteger(connection.backend) || connection.backend < 0 || connection.backend > 7 || !Number.isInteger(connection.code) || connection.code < 0 || connection.code > 6 ||
            (connection.revision ? !validConnectionSettings(connection) : connection.relay !== '' || connection.local !== '' || connection.peer !== '' || connection.port !== 0 || connection.role !== 0 || connection.seat !== 0))) return false;
        function validPickup(value) {
            return value && typeof value.waiting === 'boolean' && [1, 2, 3, 5, 10, 20, 30].includes(value.tolerance) &&
                (value.automation === undefined || typeof value.automation === 'boolean') &&
                typeof value.autoEnabled === 'boolean' &&
                Array.isArray(value.axes) && value.axes.length <= 21 && new Set(value.axes.map(axis => axis && axis.id)).size === value.axes.length &&
                value.axes.every(axis => axis && typeof axis.id === 'string' && /^[a-zA-Z0-9]{1,24}$/.test(axis.id) &&
                    typeof axis.label === 'string' && axis.label.length <= 32 &&
                    (axis.saved === undefined || typeof axis.saved === 'boolean') &&
                    (axis.fixed === undefined || typeof axis.fixed === 'boolean') &&
                    (axis.relative === undefined || typeof axis.relative === 'boolean') &&
                    [axis.input, axis.target].every(position => position === null || Number.isFinite(position) && position >= 0 && position <= 100) &&
                    typeof axis.calibrated === 'boolean' && typeof axis.fresh === 'boolean' && typeof axis.aligned === 'boolean');
        }
        var shared = value && value.shared;
        var guidance = value && value.guidance;
        var cockpit = value && value.cockpit;
        var recovery = cockpit && cockpit.recovery;
        if (recovery !== undefined && (!recovery || !Number.isInteger(recovery.phase) || recovery.phase < 0 || recovery.phase > 4 ||
            !['checked','corrected','pending','failed','unavailable'].every(key => Number.isInteger(recovery[key]) && recovery[key] >= 0 && recovery[key] <= 512) ||
            recovery.corrected > recovery.checked || typeof recovery.issues !== 'string' || recovery.issues.length > 1023)) return false;
        var binding = value && value.forceButton;
        if (binding !== undefined && (!binding || !['saved','ready','armed','listening','captured'].every(key => typeof binding[key] === 'boolean') ||
            !Number.isInteger(binding.button) || binding.button < 1 || binding.button > 128 || typeof binding.name !== 'string' || binding.name.length > 255 ||
            typeof binding.error !== 'string' || binding.error.length > 200 || !Array.isArray(binding.devices) || binding.devices.length > 16 ||
            !binding.devices.every(d => d && Number.isInteger(d.id) && d.id >= 0 && d.id <= 4294967295 && typeof d.name === 'string' && d.name.length <= 255))) return false;
        if (cockpit !== undefined && (!cockpit || typeof cockpit.available !== 'boolean' || typeof cockpit.sharing !== 'boolean' ||
            !Number.isInteger(cockpit.count) || cockpit.count < 0 || cockpit.count > 512 || !Number.isInteger(cockpit.known) || cockpit.known < 0 || cockpit.known > cockpit.count ||
            !Number.isInteger(cockpit.result) || cockpit.result < 0 || cockpit.result > 4 || cockpit.sharing && !cockpit.available)) return false;
        var outputCheck = value && value.outputCheck;
        var wizard = value && value.wizard;
        if (wizard !== undefined && (!wizard || !Number.isInteger(wizard.phase) || wizard.phase < 0 || wizard.phase > 8 ||
            !['steering', 'throttle', 'propeller', 'mixture'].includes(wizard.group) || !Number.isInteger(wizard.needed) || wizard.needed < 0 || wizard.needed >= 2097152 ||
            !Number.isInteger(wizard.axis) || wizard.axis < 0 || wizard.axis > 21 || typeof wizard.ready !== 'boolean' ||
            !['message', 'prerequisite'].every(key => typeof wizard[key] === 'string' && wizard[key].length <= 200) ||
            (wizard.resetReady !== undefined || wizard.resetReason !== undefined) && (typeof wizard.resetReady !== 'boolean' || typeof wizard.resetReason !== 'string' || wizard.resetReason.length > 200))) return false;
        if (outputCheck !== undefined && (!outputCheck || typeof outputCheck.available !== 'boolean' ||
            outputCheck.writable !== undefined && typeof outputCheck.writable !== 'boolean' ||
            outputCheck.storage !== undefined && !['waiting', 'ready', 'unavailable'].includes(outputCheck.storage) ||
            outputCheck.saved !== undefined && (!Number.isInteger(outputCheck.saved) || outputCheck.saved < 0 || outputCheck.saved > 15 || (outputCheck.saved & ~outputCheck.passed)) ||
            !Number.isInteger(outputCheck.phase) || outputCheck.phase < 0 || outputCheck.phase > 5 ||
            !['steering', 'throttle', 'propeller', 'mixture'].includes(outputCheck.group) ||
            !Number.isInteger(outputCheck.passed) || outputCheck.passed < 0 || outputCheck.passed > 15 || typeof outputCheck.restored !== 'boolean')) return false;
        if (guidance !== undefined && (!guidance || !Number.isSafeInteger(guidance.serial) || guidance.serial < 0 ||
            typeof guidance.fresh !== 'boolean' || !Number.isInteger(guidance.switches) || guidance.switches < 0 || guidance.switches > 8191 ||
            !Number.isFinite(guidance.heading) || guidance.heading < 0 || guidance.heading >= 360 ||
            !Number.isFinite(guidance.altitude) || guidance.altitude < -2000 || guidance.altitude > 100000 ||
            !Number.isFinite(guidance.verticalSpeed) || Math.abs(guidance.verticalSpeed) > 12000 ||
            typeof guidance.gps !== 'boolean' || ![0, 1, 2].includes(guidance.nav) || guidance.outputAvailable !== false ||
            !Array.isArray(guidance.slots) || guidance.slots.length !== 3 || !guidance.slots.every(slot => Number.isInteger(slot) && slot >= 0 && slot <= 4) ||
            !Array.isArray(guidance.targetSlots) || guidance.targetSlots.length !== 3 || !guidance.targetSlots.every(slot => Number.isInteger(slot) && slot >= 0 && slot <= 3) ||
            guidance.serial === 0 && (guidance.fresh || guidance.switches || guidance.heading || guidance.altitude || guidance.verticalSpeed || guidance.gps || guidance.nav || guidance.slots.some(Boolean)))) return false;
        var altimeters = value && value.altimeters;
        if (altimeters !== undefined && (!altimeters || !Number.isSafeInteger(altimeters.serial) || altimeters.serial < 0 || typeof altimeters.fresh !== 'boolean' ||
            !Array.isArray(altimeters.pressure16) || altimeters.pressure16.length !== 2 || !Array.isArray(altimeters.standard) || altimeters.standard.length !== 2 ||
            !altimeters.standard.every(flag => typeof flag === 'boolean') ||
            (altimeters.serial === 0 ? altimeters.fresh || altimeters.pressure16.some(value => value !== 0) || altimeters.standard.some(Boolean) :
                !altimeters.pressure16.every(value => Number.isInteger(value) && value >= 8000 && value <= 19200)))) return false;
        if (altimeters && ['nativeFlags', 'reported', 'sharedPhase', 'pending'].some(key => altimeters[key] !== undefined)) {
            var flags = altimeters.nativeFlags;
            if (!Number.isInteger(flags) || flags < 0 || flags > 1023 || typeof altimeters.reported !== 'boolean' ||
                !Number.isInteger(altimeters.sharedPhase) || altimeters.sharedPhase < 0 || altimeters.sharedPhase > 8 ||
                !Number.isInteger(altimeters.pending) || altimeters.pending < 0 || altimeters.pending > 3 ||
                (altimeters.issue !== undefined && ![0, 1].includes(altimeters.issue)) ||
                !!(flags & 1) !== altimeters.fresh || ((flags & 4) && !(flags & 2)) || ((flags & 8) && (flags & 5) !== 5) ||
                ((flags & 1008) && !(flags & 2)) || ((flags & 128) && (flags & 320) !== 320) || ((flags & 64) && (flags & 8)) ||
                (!altimeters.reported && (altimeters.sharedPhase !== 0 || altimeters.pending !== 0 || altimeters.issue))) return false;
        }
        if (value && value.camera !== undefined && (!value.camera || ![0, 1, 2].includes(value.camera.seat) || ![0, 1, 2, 3, 4].includes(value.camera.phase))) return false;
        if (shared && shared.seatRevision !== undefined && (!Number.isInteger(shared.seatRevision) || shared.seatRevision < 0 || shared.seatRevision > 4294967295 ||
            ![0, 1, 2].includes(shared.seat) || ![0, 1, 2, 3, 4].includes(shared.seatPending) ||
            !Number.isInteger(shared.seatRequest) || shared.seatRequest < 0 || shared.seatRequest > 4294967295 ||
            (shared.seatRevision === 0 ? shared.seat !== 0 || shared.seatPending !== 0 || shared.seatRequest !== 0 :
                shared.seat === 0 || (shared.seatPending === 0) !== (shared.seatRequest === 0)))) return false;
        if (shared !== undefined && (!shared || ![0, 1, 2, 3, 4].includes(shared.phase) ||
            (shared.session === undefined) !== (shared.serial === undefined) ||
            (shared.session !== undefined && (typeof shared.session !== 'string' || !/^(?:[a-f0-9]{32})?$/.test(shared.session) ||
                !Number.isSafeInteger(shared.serial) || shared.serial <= 0 || [2, 3].includes(shared.phase) && shared.session === '')) ||
            (shared.holding !== undefined && typeof shared.holding !== 'boolean') ||
            (shared.needsRecovery !== undefined && (typeof shared.needsRecovery !== 'boolean' || shared.needsRecovery && !shared.holding)) ||
            !Array.isArray(shared.owners) || shared.owners.length !== 4 || !shared.owners.every(owner => [0, 1, 2].includes(owner)) ||
            !['attached', 'pending', 'waiting', 'other'].every(key => Number.isInteger(shared[key]) && shared[key] >= 0 && shared[key] <= 15) ||
            (shared.waiting & shared.other))) return false;
        return !!(value && value.version === 5 && typeof value.session === 'string' && /^[a-f0-9]{1,23}$/.test(value.session) &&
            (value.learningStorage === undefined || ['waiting', 'saving', 'ready', 'unavailable'].includes(value.learningStorage)) &&
            (value.mixtureReference === undefined || ['cockpit', 'standard', 'waiting'].includes(value.mixtureReference)) &&
            Number.isSafeInteger(value.revision) && value.revision >= 0 &&
            typeof value.client === 'string' && Number.isSafeInteger(value.request) && value.request >= 0 &&
            typeof value.ready === 'boolean' && typeof value.applying === 'boolean' &&
            typeof value.steeringBlocked === 'boolean' && typeof value.quadrantBlocked === 'boolean' &&
            typeof value.advanced === 'boolean' && [0, 14].includes(value.autoMask) &&
            typeof value.quadrantMixed === 'boolean' && typeof value.quadrantToleranceMixed === 'boolean' &&
            pairs.every(group => typeof value[group + 'Blocked'] === 'boolean') &&
            value.quadrantBlocked === pairs.every(group => value[group + 'Blocked']) &&
            value.quadrantMixed === (pairs.some(group => value[group + 'Blocked']) && !value.quadrantBlocked) &&
            typeof value.error === 'string' && value.pickup && groups.every(group => validActivity(value[group]) && validPickup(value.pickup[group])) &&
            !value.pickup.steering.autoEnabled && value.pickup.quadrant.autoEnabled === !!value.autoMask &&
            pairs.every((group, index) => value.pickup[group].autoEnabled === !!(value.autoMask & (2 << index))));
    }

    class Controller {
        constructor(transport, options) {
            options = options || {};
            this.transport = transport;
            this.now = options.now || Date.now;
            this.client = options.client || Math.random().toString(16).slice(2, 14);
            this.nextRequest = 0;
            this.snapshot = null;
            this.receivedAt = -Infinity;
            this.pending = null;
            this.error = '';
            this.listeners = new Set();
            this.unsubscribe = null;
            this.timer = null;
            this.retiredSessions = new Set();
            this.routeInspector = null;
            this.navigation = { phase: 'unavailable', message: 'Avionics route interface is unavailable.', summary: '', responses: 0 };
        }

        start() {
            if (this.timer !== null) return;
            this.unsubscribe = this.transport.subscribe(data => this.receive(data));
            this.tick();
            this.timer = setInterval(() => this.tick(), 500);
        }

        dispose() {
            if (this.timer !== null) clearInterval(this.timer);
            if (this.unsubscribe) this.unsubscribe();
            this.timer = null;
            this.unsubscribe = null;
            this.listeners.clear();
            if (this.routeInspector) this.routeInspector.dispose();
        }

        subscribe(listener) {
            this.listeners.add(listener);
            listener(this.state());
            return () => this.listeners.delete(listener);
        }

        state() {
            var online = this.now() - this.receivedAt < 3000;
            return {
                online: online,
                usable: !!(online && this.snapshot && this.snapshot.ready && !this.snapshot.applying && !this.pending &&
                    !(this.snapshot.wizard && this.snapshot.wizard.phase >= 1 && this.snapshot.wizard.phase <= 5) &&
                    !(this.snapshot.outputCheck && [1, 2, 3].includes(this.snapshot.outputCheck.phase))),
                snapshot: this.snapshot,
                pending: this.pending,
                navigation: this.navigation,
                error: this.error || (this.snapshot ? this.snapshot.error : '')
            };
        }

        notify() {
            var state = this.state();
            this.listeners.forEach(listener => listener(state));
        }
        configureRouteInspector(inspector) {
            if (this.routeInspector) return;
            this.routeInspector = inspector;
            inspector.subscribe(state => { this.navigation = state; this.notify(); });
        }
        inspectAvionicsRoute() { return this.routeInspector ? this.routeInspector.inspect() : Promise.resolve(false); }

        receive(raw) {
            var status;
            try { status = typeof raw === 'string' ? JSON.parse(raw.replace(/\0+$/, '')) : raw; }
            catch (_) { return; }
            if (!validStatus(status) || this.retiredSessions.has(status.session)) return;
            if (this.snapshot && status.session === this.snapshot.session && status.revision < this.snapshot.revision) return;
            if (this.snapshot && status.session === this.snapshot.session && this.snapshot.connection &&
                (!status.connection || status.connection.revision < this.snapshot.connection.revision)) return;
            var beforeShared = this.snapshot && this.snapshot.shared;
            if (this.snapshot && status.session === this.snapshot.session && beforeShared && beforeShared.serial !== undefined &&
                (!status.shared || status.shared.serial === undefined || status.shared.serial < beforeShared.serial ||
                    status.shared.serial === beforeShared.serial && status.shared.session !== beforeShared.session ||
                    status.shared.session === beforeShared.session && status.shared.seatRevision < beforeShared.seatRevision && [2, 3].includes(status.shared.phase))) return;
            if (this.snapshot && status.session !== this.snapshot.session) {
                this.retiredSessions.add(this.snapshot.session);
                this.pending = null;
                this.error = 'Control bridge restarted. Check the ownership shown below.';
            }
            this.snapshot = status;
            this.receivedAt = this.now();
            if (this.pending && status.client === this.client && status.request === this.pending.id) {
                // A read reply or periodic update must never acknowledge a mutation.
                if (!status.applying || status.error || this.pending.operation.indexOf('instruments|') === 0) {
                    this.error = status.error;
                    this.pending = null;
                }
            }
            if (!status.ready && !status.applying && status.error) this.pending = null;
            this.notify();
        }

        send(operation) {
            var id = ++this.nextRequest;
            var packet = '5|' + this.client + '|' + id + '|' + operation;
            this.transport.send(packet);
            return id;
        }

        setOwner(group, owner) {
            var cancel = owner === 'mock' && pairs.includes(group) && this.snapshot && this.snapshot.pickup[group].waiting;
            if ((!this.availableGroup(group) && !cancel) || (owner !== 'me' && owner !== 'mock')) return false;
            if (owner === 'mock' && sharedActive(this.snapshot) && this.snapshot.pickup[group].waiting) return this.cancelTakeover(group);
            return this.mutate('set|' + group + '|' + owner);
        }

        cancelTakeover(group) {
            if (!groups.includes(group)) return false;
            return sharedActive(this.snapshot) ? this.mutate('cancel|' + group + '|pending') : this.setOwner(group, 'mock');
        }
        checkOutput(group) {
            if (!this.availableGroup(group) || !['steering', 'throttle', 'propeller', 'mixture'].includes(group) || !this.snapshot ||
                !this.snapshot.outputCheck || !this.snapshot.outputCheck.available || this.snapshot.outputCheck.writable === false ||
                this.snapshot.shared && this.snapshot.shared.phase !== 0 || !this.snapshot[group + 'Blocked']) return false;
            return this.mutate('check-output|' + group + '|start');
        }
        startWizard() {
            var wizard = this.snapshot && this.snapshot.wizard;
            if (!wizard || !wizard.ready || wizard.phase >= 1 && wizard.phase <= 5) return false;
            return this.mutate('wizard-start', true);
        }
        resetSetup() {
            var wizard = this.snapshot && this.snapshot.wizard;
            if (!wizard || wizard.resetReady !== true || wizard.phase >= 1 && wizard.phase <= 5) return false;
            return this.mutate('reset-setup', true);
        }
        cancelWizard() {
            var wizard = this.snapshot && this.snapshot.wizard;
            if (!wizard || wizard.phase < 1 || wizard.phase > 5) return false;
            return this.mutate('wizard-cancel', true);
        }
        forceWizardGroup() {
            var wizard = this.snapshot && this.snapshot.wizard;
            if (!wizard || !this.snapshot[wizard.group + 'Blocked']) return false;
            return this.mutate('force|' + wizard.group + '|me', true);
        }
        sharedSession() {
            if (!this.snapshot || !this.snapshot.shared) return false;
            if (![1, 2, 3].includes(this.snapshot.shared.phase) && this.snapshot.connection &&
                (this.snapshot.shared.needsRecovery || !this.snapshot.connection.revision || ![2, 6].includes(this.snapshot.connection.backend))) return false;
            return this.mutate([1, 2, 3].includes(this.snapshot.shared.phase) ? 'leave' : 'join');
        }
        changeSeat(action) {
            var shared = this.snapshot && this.snapshot.shared;
            if (!shared || shared.phase !== 3 || !shared.seatRevision ||
                !['request', 'accept', 'decline', 'cancel'].includes(action) ||
                (action === 'request' ? shared.seatPending !== 0 :
                    action === 'cancel' ? ![1, 3].includes(shared.seatPending) : action === 'decline' ? ![2, 4].includes(shared.seatPending) : shared.seatPending !== 2)) return false;
            return this.mutate(['seat', action, shared.seatRevision, shared.seatRequest].join('|'));
        }
        setConnection(value) {
            if (!validConnectionSettings(value) || !this.snapshot || !this.snapshot.connection ||
                this.snapshot.shared && ([1, 2, 3].includes(this.snapshot.shared.phase) || this.snapshot.shared.holding)) return false;
            return this.mutate(['connect', value.relay, value.port, value.local, value.peer, value.role === 1 ? 'host' : 'guest', value.seat === 1 ? 'pilot' : 'copilot'].join('|'));
        }

        restore() { return this.mutate('restore'); }
        canUseLocalInstruments() {
            var state = this.state(), baro = this.snapshot && this.snapshot.altimeters;
            var flags = baro && baro.nativeFlags;
            return !!(state.online && this.snapshot.ready && !this.pending && Number.isInteger(flags) && (flags & 2) && !(flags & 128) &&
                ((flags & 304) || baro.reported && [1, 2, 3, 4, 7].includes(baro.sharedPhase)));
        }
        useLocalInstruments() {
            if (!this.canUseLocalInstruments()) return false;
            return this.mutate('instruments|local|' + ((this.snapshot.shared && this.snapshot.shared.session) || '0'), true);
        }

        forceTakeover(group) {
            if (!this.availableGroup(group)) return false;
            return this.mutate('force|' + group + '|me');
        }
        resyncCockpit() {
            var state = this.state(), cockpit = this.snapshot && this.snapshot.cockpit;
            if (!state.usable || !cockpit || !cockpit.sharing || cockpit.recovery && [1,2].includes(cockpit.recovery.phase)) return false;
            return this.mutate('resync-cockpit');
        }
        configureForceButton(action, device) {
            var binding = this.snapshot && this.snapshot.forceButton;
            if (!this.state().usable || !binding || !['listen','cancel','clear'].includes(action)) return false;
            if (action === 'listen' && (!Number.isInteger(device) || !binding.devices.some(d => d.id === device))) return false;
            return this.mutate('force-button|' + action + '|' + (action === 'listen' ? device : 0));
        }

        setTolerance(group, percent) {
            if (group !== 'all' && !this.availableGroup(group) || ![1, 2, 3, 5, 10, 20, 30].includes(percent)) return false;
            return this.mutate('tolerance|' + group + '|' + percent);
        }
        cycleTolerance() {
            if (!this.state().usable) return false;
            var current = commonTolerance(this.snapshot);
            return this.setTolerance('all', current === 10 ? 20 : current === 20 ? 30 : 10);
        }

        relearn(group) {
            if (!this.availableGroup(group) || sharedActive(this.snapshot)) return false;
            return this.mutate('learn|' + group + '|reset');
        }

        availableGroup(group) { return groups.includes(group); }

        setAdvanced(enabled) {
            if (typeof enabled !== 'boolean') return false;
            return this.mutate('advanced|quadrant|' + (enabled ? 'on' : 'off'));
        }

        setAuto(enabled) {
            if (typeof enabled !== 'boolean') return false;
            return this.mutate('auto|quadrant|' + (enabled ? 'on' : 'off'));
        }

        mutate(operation, independent) {
            var state = this.state();
            if (independent ? !state.online || !this.snapshot || !this.snapshot.ready || this.pending : !state.usable) return false;
            // Install pending before sending: a transport can reply synchronously.
            this.pending = { id: this.nextRequest + 1, started: this.now(), operation: operation };
            this.error = '';
            try { this.send(operation); }
            catch (_) {
                this.pending = null;
                this.error = 'Command could not be sent. Ownership has not been confirmed.';
            }
            this.notify();
            return this.pending !== null || !this.error;
        }

        tick() {
            if (this.pending && this.now() - this.pending.started > 6000) {
                this.pending = null;
                this.error = 'No confirmation received. Check the current ownership before trying again.';
            }
            try { this.send('read'); }
            catch (_) { /* Missing bridge stays offline; never invent a successful state. */ }
            this.notify();
        }
    }

    function createCommBusTransport() {
        var listener = null;
        var registered = false;
        var loadingScript = false;
        var receiver = null;
        function ensureListener() {
            if (listener) return registered;
            if (typeof root.RegisterCommBusListener !== 'function') {
                if (!loadingScript && root.Include && typeof root.Include.addScript === 'function') {
                    loadingScript = true;
                    root.Include.addScript('/JS/Services/CommBus.js');
                }
                return false;
            }
            listener = root.RegisterCommBusListener(function () { registered = true; });
            listener.on(STATUS_EVENT, function (data) { if (receiver) receiver(data); });
            return registered;
        }
        return {
            subscribe: function (callback) {
                receiver = callback;
                ensureListener();
                return function () { receiver = null; };
            },
            send: function (packet) {
                if (!ensureListener()) throw new Error('CommBus is not ready');
                var result = listener.callWasm(COMMAND_EVENT, packet);
                if (result && typeof result.catch === 'function') result.catch(function () { /* timeout exposes delivery failure */ });
            },
            reportNavigation: function (canonical) {
                if (!ensureListener()) return;
                var result=listener.callWasm('FSC_AVIONICS_ROUTE_DIAGNOSTIC',canonical);
                if(result && typeof result.catch==='function')result.catch(function(){});
            }
        };
    }

    root.FscControls = { Controller: Controller, createCommBusTransport: createCommBusTransport, validStatus: validStatus, validConnectionSettings: validConnectionSettings, commonTolerance: commonTolerance, sharedActive: sharedActive, sharedGroup: sharedGroup };
    if (typeof module !== 'undefined' && module.exports) module.exports = root.FscControls;
})(typeof window !== 'undefined' ? window : globalThis);
