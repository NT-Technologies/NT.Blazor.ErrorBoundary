(function () {
    'use strict';

    interface Breadcrumb {
        isNavigationIntercepted: boolean | null;
        occurredAtUtc: string;
        phase: string;
        targetUri: string | null;
        uri: string | null;
    }

    interface NavigationState {
        completed: boolean;
        expectInteractiveReady: boolean;
        id: string;
        isNavigationIntercepted: boolean | null;
        originUri: string | null;
        phase: string;
        startedAtUtc: string;
        targetUri: string | null;
    }

    interface ClientContext {
        applicationVersion: string | null;
        breadcrumbs: Breadcrumb[];
        clientSessionId: string;
        currentUri: string | null;
        isNavigationIntercepted: boolean | null;
        isOnline: boolean;
        navigationId: string | null;
        navigationPhase: string | null;
        originUri: string | null;
        targetUri: string | null;
        traceParent: string | null;
    }

    interface ErrorReport {
        applicationVersion?: string | null;
        breadcrumbs?: Breadcrumb[];
        clientSessionId?: string | null;
        isNavigationIntercepted?: boolean | null;
        isOnline?: boolean | null;
        navigationId?: string | null;
        navigationPhase?: string | null;
        originUri?: string | null;
        referenceId?: string | null;
        targetUri?: string | null;
        traceParent?: string | null;
        uri?: string | null;
        [key: string]: unknown;
    }

    interface BrowserGlobals {
        Blazor?: {
            addEventListener(eventName: 'enhancednavigationstart' | 'enhancedload', listener: () => void): void;
        };
        NTBlazorErrorBoundary?: {
            getContext(): ClientContext;
            markInteractiveReady(): void;
            markLocationChanging(targetUri: string, isNavigationIntercepted: boolean): void;
            queueReport(report: ErrorReport): void;
            submitReport(report: ErrorReport): Promise<boolean>;
        };
    }

    const browser = globalThis as typeof globalThis & BrowserGlobals;

    const script = document.currentScript as HTMLScriptElement | null;
    const reportUri = script?.dataset.reportUri;
    if (!reportUri || browser.NTBlazorErrorBoundary) {
        return;
    }

    const reportEndpoint = reportUri;
    const applicationVersion = script.dataset.applicationVersion || null;
    const traceParent = script.dataset.traceparent || null;
    const stallTimeoutMs = Number.parseInt(script.dataset.stallTimeoutMs || '15000', 10);
    const breadcrumbKey = 'nt.blazor.error-boundary.breadcrumbs';
    const navigationKey = 'nt.blazor.error-boundary.navigation';
    const queueKey = 'nt.blazor.error-boundary.queue';
    const sentKey = 'nt.blazor.error-boundary.sent';
    const sessionKey = 'nt.blazor.error-boundary.session';
    const maxBreadcrumbs = 20;
    const maxQueuedReports = 20;
    const maxSentReferences = 10;
    const maxKeepaliveBytes = 64000;
    const inFlight = new Set<string>();
    const duplicateWindowMs = 5000;
    const referenceAlphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
    const reconnectModalId = 'components-reconnect-modal';
    const reconnectFailureStates = ['failed', 'resume-failed', 'rejected'];
    const recentReports = new Map();
    let navigation = readJson<NavigationState | null>(navigationKey, null);
    let stallTimer: ReturnType<typeof setTimeout> | null = null;
    const clientSessionId = readSessionId();

    function readSessionId(): string {
        const existing = readText(sessionKey);
        if (existing) {
            return existing;
        }

        const value = createId();
        writeText(sessionKey, value);
        return value;
    }

    function createId(): string {
        if (globalThis.crypto?.randomUUID) {
            return globalThis.crypto.randomUUID();
        }

        return Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
    }

    function createReferenceId(): string {
        const bytes = new Uint8Array(8);
        try {
            globalThis.crypto.getRandomValues(bytes);
        }
        catch {
            for (let i = 0; i < bytes.length; i++) {
                bytes[i] = Math.floor(Math.random() * 256);
            }
        }

        // 256 is divisible by 32, so masking each byte to five bits is unbiased.
        return Array.from(bytes, byte => referenceAlphabet[byte & 31]).join('');
    }

    function readText(key: string): string | null {
        try {
            return sessionStorage.getItem(key);
        }
        catch {
            return null;
        }
    }

    function writeText(key: string, value: string): void {
        try {
            sessionStorage.setItem(key, value);
        }
        catch {
            // Telemetry must never interfere with application behavior.
        }
    }

    function readJson<T>(key: string, fallback: T): T {
        try {
            const value = sessionStorage.getItem(key);
            return value ? JSON.parse(value) as T : fallback;
        }
        catch {
            return fallback;
        }
    }

    function writeJson(key: string, value: unknown): void {
        try {
            sessionStorage.setItem(key, JSON.stringify(value));
        }
        catch {
            // Telemetry must never interfere with application behavior.
        }
    }

    function sanitizeUri(value: string | null | undefined): string | null {
        if (!value) {
            return null;
        }

        try {
            const uri = new URL(value, document.baseURI);
            return uri.origin + uri.pathname;
        }
        catch {
            return null;
        }
    }

    function getBreadcrumbs(): Breadcrumb[] {
        const breadcrumbs = readJson<Breadcrumb[]>(breadcrumbKey, []);
        return Array.isArray(breadcrumbs) ? breadcrumbs.slice(-maxBreadcrumbs) : [];
    }

    function recordBreadcrumb(phase: string, targetUri: string | null | undefined, isNavigationIntercepted: boolean | null | undefined): Breadcrumb {
        const breadcrumb = {
            isNavigationIntercepted: isNavigationIntercepted ?? null,
            occurredAtUtc: new Date().toISOString(),
            phase,
            targetUri: sanitizeUri(targetUri),
            uri: sanitizeUri(location.href),
        };
        const breadcrumbs = [...getBreadcrumbs(), breadcrumb].slice(-maxBreadcrumbs);
        writeJson(breadcrumbKey, breadcrumbs);
        return breadcrumb;
    }

    function persistNavigation(): void {
        writeJson(navigationKey, navigation);
    }

    function beginNavigation(targetUri: string, expectInteractiveReady: boolean): void {
        navigation = {
            completed: false,
            expectInteractiveReady,
            id: createId(),
            isNavigationIntercepted: null,
            originUri: sanitizeUri(location.href),
            phase: 'AnchorClicked',
            startedAtUtc: new Date().toISOString(),
            targetUri: sanitizeUri(targetUri),
        };
        recordBreadcrumb(navigation.phase, navigation.targetUri, navigation.isNavigationIntercepted);
        persistNavigation();
        scheduleStallReport();
    }

    function updateNavigation(phase: string, targetUri: string | null | undefined, isNavigationIntercepted: boolean | null | undefined): void {
        if (!navigation || navigation.completed) {
            navigation = {
                completed: false,
                expectInteractiveReady: false,
                id: createId(),
                isNavigationIntercepted: isNavigationIntercepted ?? null,
                originUri: sanitizeUri(location.href),
                phase,
                startedAtUtc: new Date().toISOString(),
                targetUri: sanitizeUri(targetUri),
            };
        }
        else {
            navigation.phase = phase;
            navigation.targetUri = sanitizeUri(targetUri) || navigation.targetUri;
            navigation.isNavigationIntercepted = isNavigationIntercepted ?? navigation.isNavigationIntercepted;
        }

        recordBreadcrumb(phase, navigation.targetUri, navigation.isNavigationIntercepted);
        persistNavigation();
        scheduleStallReport();
    }

    function completeNavigation(phase: string): void {
        if (!navigation) {
            recordBreadcrumb(phase, location.href, true);
            return;
        }

        navigation.completed = true;
        navigation.phase = phase;
        navigation.targetUri = navigation.targetUri || sanitizeUri(location.href);
        recordBreadcrumb(phase, navigation.targetUri, navigation.isNavigationIntercepted);
        persistNavigation();
        if (stallTimer !== null) {
            clearTimeout(stallTimer);
        }
        stallTimer = null;
    }

    function scheduleStallReport(): void {
        if (stallTimer !== null) {
            clearTimeout(stallTimer);
        }
        if (!navigation || navigation.completed || !navigation.expectInteractiveReady) {
            return;
        }

        const startedAt = Date.parse(navigation.startedAtUtc);
        const elapsed = Number.isFinite(startedAt) ? Date.now() - startedAt : 0;
        stallTimer = setTimeout(reportStalledNavigation, Math.max(0, stallTimeoutMs - elapsed));
    }

    function reportStalledNavigation(): void {
        if (!navigation || navigation.completed) {
            return;
        }

        const phase = navigation.phase;
        void submitReport({
            boundaryName: 'BrowserNavigation',
            exceptionDetails: 'Navigation did not complete. Last phase: ' + phase + '.',
            exceptionMessage: 'Navigation did not reach its expected completion marker within ' + stallTimeoutMs + ' ms.',
            exceptionStackTrace: null,
            exceptionType: 'NT.Blazor.NavigationStalledException',
            isInteractive: true,
            occurredAtUtc: new Date().toISOString(),
            renderMode: 'Browser',
            reportKind: 'NavigationStalled',
        });
        completeNavigation('NavigationStalled');
    }

    function getContext(): ClientContext {
        return {
            applicationVersion,
            breadcrumbs: getBreadcrumbs(),
            clientSessionId,
            currentUri: sanitizeUri(location.href),
            isNavigationIntercepted: navigation?.isNavigationIntercepted ?? null,
            isOnline: navigator.onLine,
            navigationId: navigation?.id ?? null,
            navigationPhase: navigation?.phase ?? null,
            originUri: navigation?.originUri ?? null,
            targetUri: navigation?.targetUri ?? null,
            traceParent,
        };
    }

    function enrichReport(report: ErrorReport): ErrorReport {
        const context = getContext();
        return {
            ...report,
            applicationVersion: report.applicationVersion || context.applicationVersion,
            breadcrumbs: context.breadcrumbs,
            clientSessionId: report.clientSessionId || context.clientSessionId,
            isNavigationIntercepted: report.isNavigationIntercepted ?? context.isNavigationIntercepted,
            isOnline: report.isOnline ?? context.isOnline,
            navigationId: report.navigationId || context.navigationId,
            navigationPhase: report.navigationPhase || context.navigationPhase,
            originUri: report.originUri || context.originUri,
            referenceId: report.referenceId || createReferenceId(),
            targetUri: report.targetUri || context.targetUri,
            traceParent: report.traceParent || context.traceParent,
            uri: context.currentUri || report.uri,
        };
    }

    // Returns true when the report needs no further retry: delivered, or permanently rejected by the server.
    async function tryPost(report: ErrorReport, keepalive: boolean): Promise<boolean> {
        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (report.traceParent) {
            headers.traceparent = report.traceParent;
        }

        try {
            const body = JSON.stringify(report);
            const response = await fetch(reportEndpoint, {
                body,
                credentials: 'same-origin',
                headers,
                // Browsers reject keepalive requests with larger bodies; without this a large queued report would fail on every retry.
                keepalive: keepalive && new TextEncoder().encode(body).length <= maxKeepaliveBytes,
                method: 'POST',
            });
            return response.ok || (response.status >= 400 && response.status < 500 && response.status !== 408 && response.status !== 429);
        }
        catch {
            return false;
        }
    }

    // Stored lists come from earlier pages and older script versions; anything that is not a list of the expected items is ignored rather than trusted.
    function readList<T>(key: string, isItem: (value: unknown) => value is T): T[] {
        const list = readJson<unknown>(key, []);
        return Array.isArray(list) ? list.filter(isItem) : [];
    }

    // The last few reference IDs known to be delivered, kept across navigations. When the page navigates during an upload, the browser still completes
    // the keepalive request, but the page sees it fail after pagehide; without this record the report would be queued and resent by the next page.
    function readSent(): string[] {
        return readList(sentKey, (value): value is string => typeof value === 'string');
    }

    function markSent(referenceIds: string[]): void {
        if (referenceIds.length > 0) {
            writeJson(sentKey, [...readSent().filter(id => !referenceIds.includes(id)), ...referenceIds].slice(-maxSentReferences));
        }
    }

    function wasSent(report: ErrorReport): boolean {
        return readSent().includes(report.referenceId as string);
    }

    // Returns true when the report needs no retry: delivered, permanently rejected, or still in flight when the page hid.
    async function send(report: ErrorReport): Promise<boolean> {
        // Reports queued by an older script version have no reference ID; give them one so delivery can be recorded.
        const referenceId = report.referenceId ||= createReferenceId();
        inFlight.add(referenceId);
        const delivered = await tryPost(report, true);
        inFlight.delete(referenceId);
        if (delivered) {
            markSent([referenceId]);
        }

        return delivered || wasSent(report);
    }

    async function submitReport(report: ErrorReport): Promise<boolean> {
        const enriched = enrichReport(report);
        if (shouldReport(enriched) && !await send(enriched)) {
            writeQueue([...readQueue(), enriched]);
        }

        return true;
    }

    function queueReport(report: ErrorReport): void {
        const enriched = enrichReport(report);
        if (shouldReport(enriched) && !wasSent(enriched)) {
            writeQueue([...readQueue(), enriched]);
        }
    }

    function readQueue(): ErrorReport[] {
        return readList(queueKey, (value): value is ErrorReport => typeof value === 'object' && value !== null);
    }

    function writeQueue(reports: ErrorReport[]): void {
        writeJson(queueKey, reports.slice(-maxQueuedReports));
    }

    // Claims one queued report at a time, so reports not yet sent stay queued for the page-hide beacon or the next page if the page goes mid-flush.
    // Each claim is a synchronous read and write, so concurrent flushes and submissions never take the same report.
    async function flushQueue(): Promise<void> {
        for (let remaining = readQueue().length; remaining > 0; remaining--) {
            const [report, ...rest] = readQueue();
            if (!report) {
                return;
            }

            writeQueue(rest);
            if (!wasSent(report) && !await send(report)) {
                writeQueue([...readQueue(), report]);
            }
        }
    }

    // Uploads still in flight are completed by the browser after the page goes, so they count as delivered from here on.
    // A page entering the back-forward cache is not gone: it may be restored, and its uploads then settle normally, so they are not recorded.
    // Beaconed reports leave the queue and are not recorded, so they cannot push those in-flight references out of the short record.
    function handlePageHide(event: PageTransitionEvent): void {
        if (!event.persisted) {
            markSent([...inFlight]);
        }

        if (!navigator.sendBeacon) {
            return;
        }

        const sent = readSent();
        writeQueue(readQueue().filter(report => !sent.includes(report.referenceId as string)
            && !navigator.sendBeacon(reportEndpoint, new Blob([JSON.stringify(report)], { type: 'application/json' }))));
    }

    function shouldReport(report: ErrorReport): boolean {
        const fingerprint = [report.reportKind, report.exceptionType, report.exceptionMessage, report.boundaryName, report.javaScriptSource, report.javaScriptLine, report.javaScriptColumn].join('|');
        const now = Date.now();
        const previous = recentReports.get(fingerprint);
        recentReports.set(fingerprint, now);
        for (const [key, occurredAt] of recentReports) {
            if (now - occurredAt > duplicateWindowMs) {
                recentReports.delete(key);
            }
        }
        return !previous || now - previous > duplicateWindowMs;
    }

    function reportBrowserIssue(kind: string, message: string, stack: string | null, source: string | null, line: number | null, column: number | null, boundaryName = 'Browser'): void {
        void submitReport({
            boundaryName,
            exceptionDetails: stack || message,
            exceptionMessage: message,
            exceptionStackTrace: stack || null,
            exceptionType: kind,
            isInteractive: true,
            javaScriptColumn: column || null,
            javaScriptLine: line || null,
            javaScriptSource: sanitizeUri(source),
            occurredAtUtc: new Date().toISOString(),
            renderMode: 'Browser',
            reportKind: kind,
        });
    }

    function getBlazorResourceUri(target: EventTarget | null): string | null {
        if (!(target instanceof Element) || (target.tagName !== 'SCRIPT' && target.tagName !== 'LINK')) {
            return null;
        }

        const uri = sanitizeUri((target as HTMLScriptElement).src || (target as HTMLLinkElement).href);
        return uri && /\/_framework\/|blazor\.(web|server|webassembly)\.js$/.test(uri) ? uri : null;
    }

    function reportReconnectFailure(state: string): void {
        reportBrowserIssue('ReconnectFailed', 'Blazor reconnection ended in the ' + state + ' state.', null, null, null, null, 'BlazorReconnect');
    }

    function readReconnectState(modal: Element): string | null {
        // The framework's built-in UI renders into an open shadow root and signals failure only through its status text; on rejection it reloads the page.
        if (modal.shadowRoot) {
            return /^Failed to (rejoin|resume)/.test(modal.shadowRoot.querySelector('p')?.textContent ?? '') ? 'failed' : null;
        }

        return reconnectFailureStates.find(state => modal.classList.contains('components-reconnect-' + state)) ?? null;
    }

    function observeReconnectModal(modal: Element): void {
        let lastState: string | null = null;
        const check = () => {
            const state = readReconnectState(modal);
            if (state && state !== lastState) {
                reportReconnectFailure(state);
            }
            lastState = state;
        };
        new MutationObserver(check).observe(modal.shadowRoot ?? modal, modal.shadowRoot ? { characterData: true, childList: true, subtree: true } : { attributeFilter: ['class'], attributes: true });
        check();
    }

    function watchReconnectModal(): void {
        try {
            const existing = document.getElementById(reconnectModalId);
            if (existing) {
                observeReconnectModal(existing);
                return;
            }

            // Watch for the modal's insertion only until it exists, rather than observing every class change in the document.
            const finder = new MutationObserver(() => {
                const modal = document.getElementById(reconnectModalId);
                if (modal) {
                    finder.disconnect();
                    observeReconnectModal(modal);
                }
            });
            finder.observe(document.body, { childList: true, subtree: true });
        }
        catch {
            // Telemetry must never interfere with application behavior.
        }
    }

    function findAnchor(event: MouseEvent): HTMLAnchorElement | null {
        return event.target instanceof Element ? event.target.closest('a[href]') : null;
    }

    function isEligibleNavigation(event: MouseEvent, anchor: HTMLAnchorElement): boolean {
        if (event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || anchor.hasAttribute('download')) {
            return false;
        }

        const target = anchor.getAttribute('target');
        if (target && target.toLowerCase() !== '_self') {
            return false;
        }

        try {
            return new URL(anchor.href, document.baseURI).origin === location.origin;
        }
        catch {
            return false;
        }
    }

    document.addEventListener('click', event => {
        const anchor = findAnchor(event);
        if (!anchor || !isEligibleNavigation(event, anchor)) {
            return;
        }

        beginNavigation(anchor.href, anchor.dataset.ntRequireInteractiveReady === 'true');
    }, true);

    window.addEventListener('error', event => {
        // Capture-phase listeners also receive resource load failures, which are plain Events rather than script errors.
        if (!(event instanceof ErrorEvent)) {
            const resource = getBlazorResourceUri((event as Event).target);
            if (resource) {
                reportBrowserIssue('ResourceLoadFailed', 'Failed to load Blazor framework resource.', null, resource, null, null);
            }
            return;
        }

        const error = event.error;
        reportBrowserIssue(
            'JavaScriptError',
            event.message || error?.message || 'Unknown JavaScript error',
            error?.stack || null,
            event.filename || null,
            event.lineno || null,
            event.colno || null);
    }, true);

    // The event does not bubble from the modal, but capture-phase listeners on ancestors still receive it.
    document.addEventListener('components-reconnect-state-changed', event => {
        const state = (event as CustomEvent<{ state?: string }>).detail?.state;
        if (state && reconnectFailureStates.includes(state)) {
            reportReconnectFailure(state);
        }
    }, true);

    window.addEventListener('unhandledrejection', event => {
        const reason = event.reason;
        const message = reason?.message || String(reason || 'Unhandled promise rejection');
        reportBrowserIssue('JavaScriptUnhandledRejection', message, reason?.stack || null, null, null, null);
    });

    window.addEventListener('online', () => void flushQueue());
    window.addEventListener('pagehide', handlePageHide);

    function attachBlazorEvents(): void {
        if (!browser.Blazor?.addEventListener) {
            return;
        }

        browser.Blazor.addEventListener('enhancednavigationstart', () => {
            updateNavigation('EnhancedNavigationStarted', navigation?.targetUri, true);
        });
        browser.Blazor.addEventListener('enhancedload', () => {
            updateNavigation('LocationChanged', location.href, true);
            if (!navigation?.expectInteractiveReady) {
                completeNavigation('EnhancedLoad');
            }
        });
    }

    browser.NTBlazorErrorBoundary = {
        getContext,
        markInteractiveReady: () => completeNavigation('InteractiveReady'),
        markLocationChanging: (targetUri, isNavigationIntercepted) => updateNavigation('LocationChanging', targetUri, isNavigationIntercepted),
        queueReport,
        submitReport,
    };

    recordBreadcrumb('ClientSessionStarted', null, null);
    if (navigation && !navigation.completed) {
        if (sanitizeUri(location.href) === navigation.targetUri) {
            updateNavigation('DocumentLoaded', location.href, navigation.isNavigationIntercepted);
            if (!navigation.expectInteractiveReady) {
                completeNavigation('DocumentLoaded');
            }
        }
        else {
            scheduleStallReport();
        }
    }

    function onDocumentReady(): void {
        attachBlazorEvents();
        watchReconnectModal();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', onDocumentReady, { once: true });
    }
    else {
        onDocumentReady();
    }
    void flushQueue();
}());
