(function (root) {
    'use strict';

    var COMMAND_EVENT = 'FSC_CONTROLS_TEST_COMMAND';
    var STATUS_EVENT = 'FSC_CONTROLS_TEST_STATUS';

    function validActivity(value) {
        return value && Number.isSafeInteger(value.seen) && value.seen >= 0 &&
            Number.isSafeInteger(value.blocked) && value.blocked >= 0 &&
            typeof value.last === 'string' && typeof value.value === 'number';
    }

    function validStatus(value) {
        return value && value.version === 1 && typeof value.session === 'string' && /^[a-f0-9]{1,23}$/.test(value.session) &&
            Number.isSafeInteger(value.revision) && value.revision >= 0 &&
            typeof value.client === 'string' && Number.isSafeInteger(value.request) && value.request >= 0 &&
            typeof value.ready === 'boolean' && typeof value.applying === 'boolean' &&
            typeof value.steeringBlocked === 'boolean' && typeof value.quadrantBlocked === 'boolean' &&
            typeof value.error === 'string' && validActivity(value.steering) && validActivity(value.quadrant);
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
                usable: !!(online && this.snapshot && this.snapshot.ready && !this.snapshot.applying && !this.pending),
                snapshot: this.snapshot,
                pending: this.pending,
                error: this.error || (this.snapshot ? this.snapshot.error : '')
            };
        }

        notify() {
            var state = this.state();
            this.listeners.forEach(listener => listener(state));
        }

        receive(raw) {
            var status;
            try { status = typeof raw === 'string' ? JSON.parse(raw.replace(/\0+$/, '')) : raw; }
            catch (_) { return; }
            if (!validStatus(status) || this.retiredSessions.has(status.session)) return;
            if (this.snapshot && status.session === this.snapshot.session && status.revision < this.snapshot.revision) return;
            if (this.snapshot && status.session !== this.snapshot.session) {
                this.retiredSessions.add(this.snapshot.session);
                this.pending = null;
                this.error = 'Control bridge restarted. Check the ownership shown below.';
            }
            this.snapshot = status;
            this.receivedAt = this.now();
            if (this.pending && status.client === this.client && status.request === this.pending.id) {
                // A read reply or periodic update must never acknowledge a mutation.
                if (!status.applying) {
                    this.error = status.error;
                    this.pending = null;
                }
            }
            if (!status.ready && !status.applying && status.error) this.pending = null;
            this.notify();
        }

        send(operation) {
            var id = ++this.nextRequest;
            var packet = '1|' + this.client + '|' + id + '|' + operation;
            this.transport.send(packet);
            return id;
        }

        setOwner(group, owner) {
            if ((group !== 'steering' && group !== 'quadrant') || (owner !== 'me' && owner !== 'mock')) return false;
            return this.mutate('set|' + group + '|' + owner);
        }

        restore() { return this.mutate('restore'); }

        mutate(operation) {
            if (!this.state().usable) return false;
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
            }
        };
    }

    root.FscControls = { Controller: Controller, createCommBusTransport: createCommBusTransport, validStatus: validStatus };
    if (typeof module !== 'undefined' && module.exports) module.exports = root.FscControls;
})(typeof window !== 'undefined' ? window : globalThis);
