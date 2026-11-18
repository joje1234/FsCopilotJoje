// EFB interface adapter based on the MIT-licensed minimal registration example
// by Rafael Coelho. See THIRD_PARTY_NOTICES.txt. UI and control logic are local.
(function (root) {
    'use strict';
    var INTERNAL_NAME = 'FsCopilotControls';
    var BASE_URL = 'coui://html_ui/efb_ui/efb_apps/' + INTERNAL_NAME;
    var style = /*__FSC_STYLE__*/ '';
    var controller = null;
    var registered = false;

    function register() {
        if (registered) return true;
        var sdk = root.msfssdk;
        var efb = root.EFB_API;
        if (!sdk || !efb || typeof efb.use !== 'function') return false;

        class ControlView extends sdk.DisplayComponent {
            constructor(props) {
                super(props);
                this.element = sdk.FSComponent.createRef();
                this.pageKeyActions = new Map();
                this.unmount = null;
            }
            render() {
                return sdk.FSComponent.buildComponent('div', {
                    ref: this.element,
                    style: 'position:absolute;top:0;right:0;bottom:0;left:0;width:100%;height:100%;box-sizing:border-box;padding:24px 0 56px;'
                });
            }
            onAfterRender() {
                this.unmount = root.FscControls.mount(this.element.instance, controller);
            }
            onOpen() { controller.start(); controller.tick(); }
            onResume() { controller.start(); controller.tick(); }
            onPause() {}
            onClose() {}
            onUpdate() {}
            routeGamepadInteractionEvent() {}
            handlePageKeyAction() {}
            destroy() {
                if (this.unmount) this.unmount();
                super.destroy();
            }
        }

        // Implements the SDK App contract using EFB-provided globals so the
        // package has no npm dependencies or bundled copy of the EFB framework.
        class FsCopilotControls {
            constructor() {
                this.available = sdk.Subject.create(true);
                this.BootMode = 0;
                this.SuspendMode = 0;
                this._isReady = false;
                this.favoriteIndex = -1;
            }
            get name() { return 'FS Copilot Controls'; }
            get internalName() { return INTERNAL_NAME; }
            get icon() { return BASE_URL + '/Assets/icon.svg'; }
            get isReady() { return this._isReady; }
            get compatibleAircraftModels() { return undefined; }
            get unitsSettingsManager() { return this._unitsSettingsManager; }
            getIsSearchable() { return true; }
            getIsFavoritable() { return true; }
            getVersion() { return '0.9.10'; }
            async _install(props) {
                this.bus = props.bus;
                this._unitsSettingsManager = props.unitsSettingManager;
                this.notificationManager = props.notificationManager;
                this.efbSettingsManager = props.efbSettingsManager;
                this.options = props.options;
                if (!root.document.getElementById('fsc-controls-style')) {
                    var element = root.document.createElement('style');
                    element.id = 'fsc-controls-style';
                    element.textContent = style;
                    root.document.head.appendChild(element);
                }
                if (!controller) {
                    controller = new root.FscControls.Controller(root.FscControls.createCommBusTransport());
                    controller.configureRouteInspector(new root.FscControls.RouteInspector(root,{
                        report: canonical => controller.transport.reportNavigation(canonical),
                        reportError: diagnostic => controller.transport.reportNavigation(diagnostic)
                    }));
                }
                this._isReady = true;
                if (root.Coherent && typeof root.Coherent.trigger === 'function')
                    root.Coherent.trigger('EFB_APP_INSTALLED', this.name, this.internalName, '1.0.3', this.getVersion());
            }
            render() { return sdk.FSComponent.buildComponent(ControlView, { bus: this.bus }); }
        }
        efb.use(FsCopilotControls);
        registered = true;
        return true;
    }

    function retry(attempt) {
        try { if (register()) return; }
        catch (error) { if (attempt === 149) console.error('[FS Copilot Controls] Still waiting for EFB registration', error); }
        // Aircraft/avionics loading can take longer than a fixed startup timeout.
        root.setTimeout(function () { retry(attempt + 1); }, attempt < 149 ? 100 : 1000);
    }
    // Preview/test harnesses deliberately have no EFB container.
    if (!root.FSC_CONTROLS_PREVIEW) retry(0);
})(window);
