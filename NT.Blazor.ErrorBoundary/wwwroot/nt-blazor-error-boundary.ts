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
        targetUri?: string | null;
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
    const stallTimeoutMs = Number.parseInt(script.dataset.stallTimeoutMs || '15000', 10);
    const breadcrumbKey = 'nt.blazor.error-boundary.breadcrumbs';
    const navigationKey = 'nt.blazor.error-boundary.navigation';
    const queueKey = 'nt.blazor.error-boundary.queue';
    const sessionKey = 'nt.blazor.error-boundary.session';
    const maxBreadcrumbs = 20;
    const maxQueuedReports = 20;
    const duplicateWindowMs = 5000;
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
        if (!navigation || navigation.completed) {
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
            targetUri: report.targetUri || context.targetUri,
            uri: context.currentUri || report.uri,
        };
    }

    async function postReport(report: ErrorReport, keepalive: boolean): Promise<boolean> {
        try {
            const response = await fetch(reportEndpoint, {
                body: JSON.stringify(report),
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                keepalive,
                method: 'POST',
            });
            return response.ok;
        }
        catch {
            return false;
        }
    }

    async function submitReport(report: ErrorReport): Promise<boolean> {
        const enriched = enrichReport(report);
        if (await postReport(enriched, true)) {
            return true;
        }

        const queue = readJson<ErrorReport[]>(queueKey, []);
        const reports = Array.isArray(queue) ? queue : [];
        reports.push(enriched);
        writeJson(queueKey, reports.slice(-maxQueuedReports));
        return true;
    }

    async function flushQueue(): Promise<void> {
        const queue = readJson<ErrorReport[]>(queueKey, []);
        if (!Array.isArray(queue) || queue.length === 0) {
            return;
        }

        const remaining = [];
        for (const report of queue) {
            if (!await postReport(report, true)) {
                remaining.push(report);
            }
        }
        writeJson(queueKey, remaining.slice(-maxQueuedReports));
    }

    function flushQueueWithBeacon(): void {
        if (!navigator.sendBeacon) {
            return;
        }

        const queue = readJson<ErrorReport[]>(queueKey, []);
        if (!Array.isArray(queue) || queue.length === 0) {
            return;
        }

        const remaining = [];
        for (const report of queue) {
            const body = new Blob([JSON.stringify(report)], { type: 'application/json' });
            if (!navigator.sendBeacon(reportEndpoint, body)) {
                remaining.push(report);
            }
        }
        writeJson(queueKey, remaining.slice(-maxQueuedReports));
    }

    function shouldReport(fingerprint: string): boolean {
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

    function reportJavaScriptError(kind: string, message: string, stack: string | null, source: string | null, line: number | null, column: number | null): void {
        const fingerprint = [kind, message, stack, source, line, column].join('|');
        if (!shouldReport(fingerprint)) {
            return;
        }

        void submitReport({
            boundaryName: 'Browser',
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
        const error = event.error;
        reportJavaScriptError(
            'JavaScriptError',
            event.message || error?.message || 'Unknown JavaScript error',
            error?.stack || null,
            event.filename || null,
            event.lineno || null,
            event.colno || null);
    }, true);

    window.addEventListener('unhandledrejection', event => {
        const reason = event.reason;
        const message = reason?.message || String(reason || 'Unhandled promise rejection');
        reportJavaScriptError('JavaScriptUnhandledRejection', message, reason?.stack || null, null, null, null);
    });

    window.addEventListener('online', () => void flushQueue());
    window.addEventListener('pagehide', flushQueueWithBeacon);

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

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', attachBlazorEvents, { once: true });
    }
    else {
        attachBlazorEvents();
    }
    void flushQueue();
}());
