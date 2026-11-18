(function (root) {
    'use strict';
    var MAX_ROUTE = 262144;
    function canonicalRoute(route) {
        var nodes = 0;
        function normalize(value, depth) {
            if (++nodes > 20000 || depth > 24) throw new Error('Avionics route is too large.');
            if (value === null || typeof value === 'boolean') return value;
            if (typeof value === 'number') { if (!Number.isFinite(value)) throw new Error('Avionics route contains a nonfinite number.'); return value; }
            if (typeof value === 'string') { if (value.length > 4096) throw new Error('Avionics route field is too long.'); return value; }
            if (Array.isArray(value)) { if (value.length > 2048) throw new Error('Avionics route has too many entries.'); return value.map(item => normalize(item, depth + 1)); }
            if (typeof value !== 'object') throw new Error('Avionics route contains an unsupported field.');
            var result = Object.create(null);
            Object.keys(value).sort().forEach(function (key) {
                if (['__proto__', 'prototype', 'constructor'].includes(key)) throw new Error('Avionics route contains an invalid property.');
                if (value[key] !== undefined) result[key] = normalize(value[key], depth + 1);
            });
            return result;
        }
        if (!route || Array.isArray(route) || typeof route !== 'object' || !('enroute' in route)) throw new Error('Avionics did not return a planned route.');
        var json = JSON.stringify(normalize(route, 0)).replace(/[\u007f-\uffff]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
        if (json.length > MAX_ROUTE) throw new Error('Avionics route exceeds the transfer limit.');
        return json;
    }
    function identifier(value) {
        if (typeof value === 'string') return value.trim().slice(0, 32) || '—';
        if (value && typeof value.ident === 'string') return value.ident || '—';
        return '—';
    }
    function summary(route) {
        return identifier(route.departureAirport) + ' → ' + identifier(route.destinationAirport) + ' · ' +
            (Array.isArray(route.enroute) ? route.enroute.length : 0) + ' enroute entries';
    }
    class RouteInspector {
        constructor(environment, options) {
            this.root = environment; this.options = options || {}; this.listeners = new Set(); this.pending = null; this.manager = null;
            this.state = { phase: 'idle', message: 'Read the avionics route to check flight-plan access. GTN screens and touches remain local.', summary: '', responses: 0 };
        }
        subscribe(callback) { this.listeners.add(callback); callback(this.state); return () => this.listeners.delete(callback); }
        update(value) { this.state = Object.assign({}, this.state, value); this.listeners.forEach(callback => callback(this.state)); }
        async connect() {
            if (this.disposed) throw new Error('Avionics route request cancelled.');
            if (this.manager) return this.manager;
            if (this.connecting) return this.connecting;
            this.connecting = this.open();
            try { return await this.connecting; }
            finally { this.connecting = null; }
        }
        async open() {
            var sdk = this.root.msfssdk;
            // In an EFB view the SDK singleton may wait on aircraft GameState or
            // its initial EFB-route read. The direct listener needs neither.
            if (typeof this.root.RegisterViewListener !== 'function' && sdk && sdk.FlightPlanRouteManager && typeof sdk.FlightPlanRouteManager.getManager === 'function') {
                this.interfaceUsed = 'sdk';
                var manager = await sdk.FlightPlanRouteManager.getManager();
                if (this.disposed) throw new Error('Avionics route request cancelled.');
                var subscription = manager.avionicsRouteRequestResponse.on((sender, response) => this.response(response.route, response.requestId));
                this.unsubscribe = () => subscription.destroy();
                return this.manager = { request: () => manager.requestAvionicsRoute(), efb: () => manager.getEfbRoute() };
            }
            if (typeof this.root.RegisterViewListener !== 'function') throw new Error('The simulator planned-route interface is unavailable.');
            this.interfaceUsed = 'listener';
            return new Promise((resolve, reject) => {
                var listener;
                try {
                    listener = this.root.RegisterViewListener('JS_LISTENER_PLANNEDROUTE', () => Promise.resolve().then(() => {
                        if (this.disposed) { reject(new Error('Avionics route request cancelled.')); return; }
                        var handler = (route, id) => this.response(route, id);
                        listener.on('AvionicsRouteRequestResponse', handler);
                        this.unsubscribe = () => { if (typeof listener.off === 'function') listener.off('AvionicsRouteRequestResponse', handler); };
                        this.manager = { request: () => listener.call('REQUEST_AVIONICS_ROUTE'), efb: () => listener.call('GET_EFB_ROUTE') };
                        resolve(this.manager);
                    }).catch(reject));
                } catch (error) { reject(error); }
            });
        }
        response(route, id) {
            var pending = this.pending;
            if (!pending || !Number.isSafeInteger(id) || id < 0) return;
            if (pending.id !== null && pending.id !== id) { ++pending.foreign; return; }
            if (pending.responses.length >= 64) { pending.finish(new Error('Too many avionics route responses. A source must be identified before copying plans.')); return; }
            pending.responses.push({ id: id, route: route });
            if (pending.id !== null) this.settle(pending);
        }
        settle(pending) {
            if (pending.settleTimer) return;
            pending.settleTimer = this.root.setTimeout(() => {
                var matches = pending.responses.filter(value => value.id === pending.id);
                if (!matches.length) { pending.settleTimer = null; return; }
                try {
                    var canonical = matches.map(value => canonicalRoute(value.route));
                    if (new Set(canonical).size !== 1) pending.finish(new Error('Multiple avionics returned different routes. A source must be identified before copying plans.'));
                    else pending.finish(null, {id:pending.id,route:JSON.parse(canonical[0]),canonical:canonical[0]}, matches.length);
                } catch (error) { pending.finish(error); }
            }, this.options.settleMs === undefined ? 750 : this.options.settleMs);
        }
        async inspect() {
            if (this.pending || this.state.phase === 'reading' || this.disposed) return false;
            this.update({ phase: 'reading', message: 'Requesting the avionics route. No flight plan is being changed.', summary: '', responses: 0 });
            var attempt;
            try {
                var outcome = await new Promise((resolve, reject) => {
                    var pending = attempt = { id: null, stage: 'listener', foreign: 0, responses: [], settleTimer: null, timeout: null, finish: null };
                    pending.finish = (error, value, responses) => {
                        if (this.pending !== pending) return;
                        this.root.clearTimeout(pending.timeout); if (pending.settleTimer) this.root.clearTimeout(pending.settleTimer);
                        this.pending = null; error ? reject(error) : resolve({ value: value, responses: responses });
                    };
                    this.pending = pending;
                    pending.timeout = this.root.setTimeout(() => pending.finish(new Error(pending.stage === 'listener' ?
                        'The simulator route listener did not become ready. No avionics route was requested.' : pending.stage === 'request' ?
                        'The simulator did not acknowledge the avionics route request.' :
                        'No avionics answered the route request. Check whether your navigator and its edition support MSFS 2024 EFB route sync.')),
                        this.options.timeoutMs === undefined ? 8000 : this.options.timeoutMs);
                    this.connect().then(async manager => {
                        if (this.pending !== pending) return;
                        pending.stage = 'request';
                        pending.id = await manager.request();
                        if (!Number.isSafeInteger(pending.id) || pending.id < 0) throw new Error('The simulator did not identify the route request.');
                        pending.stage = 'response';
                        this.settle(pending);
                    }).catch(error => pending.finish(error));
                });
                var efb = null;
                var comparisonTimer;
                try {
                    var comparison = await Promise.race([this.manager.efb(), new Promise(resolve => {comparisonTimer=this.root.setTimeout(()=>resolve(null),this.options.comparisonMs===undefined?1500:this.options.comparisonMs);})]);
                    if (comparison) efb=canonicalRoute(comparison);
                } catch (_) { /* The optional EFB comparison cannot substitute for avionics. */ }
                finally { if(comparisonTimer)this.root.clearTimeout(comparisonTimer); }
                if(this.disposed)return false;
                var same = efb === outcome.value.canonical;
                this.lastRoute = outcome.value;
                this.update({ phase: 'observed', summary: summary(outcome.value.route), responses: outcome.responses,
                    message: same ? 'Avionics returned the same route as the EFB. Compare it with the GTN; this alone does not identify the responding instrument.' :
                        'Avionics returned a route. Compare its airports and waypoints with the GTN. The API does not identify which instrument answered.' });
                if (this.options.report) {try {this.options.report(outcome.value.canonical);} catch (_) { /* A diagnostic log failure does not invalidate the read. */ }}
                return true;
            } catch (error) {
                this.update({ phase: 'error', message: error.message || 'The avionics route could not be read.', summary: '', responses: 0 });
                if (!this.disposed && this.options.reportError) {
                    var diagnostic = { recordType: 'route-inspection-error', version: 1, interface: this.interfaceUsed || 'unavailable',
                        stage: attempt ? attempt.stage : 'listener', requestId: attempt ? attempt.id : null,
                        responses: attempt ? attempt.responses.length : 0, foreignResponses: attempt ? attempt.foreign : 0,
                        message: this.state.message.slice(0, 500) };
                    try { this.options.reportError(JSON.stringify(diagnostic).replace(/[\u007f-\uffff]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'))); } catch (_) {}
                }
                return false;
            }
        }
        dispose() { this.disposed=true;if (this.pending) this.pending.finish(new Error('Avionics route request cancelled.')); if (this.unsubscribe) this.unsubscribe(); this.listeners.clear(); }
    }
    root.FscControls = root.FscControls || {};
    root.FscControls.RouteInspector = RouteInspector;
    root.FscControls.canonicalRoute = canonicalRoute;
    if (typeof module !== 'undefined' && module.exports) module.exports = { RouteInspector: RouteInspector, canonicalRoute: canonicalRoute };
})(typeof window !== 'undefined' ? window : globalThis);
