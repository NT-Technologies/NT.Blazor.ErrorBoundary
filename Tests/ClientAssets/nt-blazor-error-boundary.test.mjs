import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const scriptUrl = new URL('../../NT.Blazor.ErrorBoundary/wwwroot/nt-blazor-error-boundary.js', import.meta.url);
const source = await readFile(scriptUrl, 'utf8').catch(() => {
    throw new Error(`Build NT.Blazor.ErrorBoundary or run "npm run build" first; ${scriptUrl.pathname} is missing.`);
});
const queueKey = 'nt.blazor.error-boundary.queue';
const sentKey = 'nt.blazor.error-boundary.sent';
const origin = 'https://app.test';

class FakeElement {
    constructor(attributes = {}) {
        this.attributes = attributes;
        this.dataset = {};
        this.id = attributes.id ?? '';
        this.tagName = (attributes.tagName ?? 'DIV').toUpperCase();
        this.classes = new Set();
        this.classList = {
            add: name => this.classes.add(name),
            contains: name => this.classes.has(name),
            remove: name => this.classes.delete(name)
        };
        this.src = attributes.src ? new URL(attributes.src, origin).href : undefined;
        if (attributes['data-nt-require-interactive-ready']) {
            this.dataset.ntRequireInteractiveReady = attributes['data-nt-require-interactive-ready'];
        }
        this.href = attributes.href ? new URL(attributes.href, origin).href : undefined;
    }

    closest(selector) {
        return selector === 'a[href]' && this.href ? this : null;
    }

    getAttribute(name) {
        return this.attributes[name] ?? null;
    }

    hasAttribute(name) {
        return name in this.attributes;
    }
}

class FakeErrorEvent {
    constructor(init) {
        Object.assign(this, init);
    }
}

// The script beacons each report as a JSON Blob; keeping the parsed report lets a test read it synchronously.
class ReportBlob extends Blob {
    constructor(parts, options) {
        super(parts, options);
        this.report = JSON.parse(parts.join(''));
    }
}

// beaconResult decides per beaconed report whether sendBeacon accepts it; null leaves navigator without sendBeacon. sessionStorage replaces the storage fake.
function loadScript({ storage = {}, fetchResult = () => true, traceParent, modal, beaconResult = () => true, sessionStorage: storageOverride } = {}) {
    const listeners = {};
    const timers = new Map();
    const fetches = [];
    const beacons = [];
    const observers = [];
    const elements = modal ? { [modal.id]: modal } : {};
    let nextTimerId = 1;

    const on = target => (type, listener) => {
        (listeners[`${target}:${type}`] ??= []).push(listener);
    };
    const sessionStorage = storageOverride ?? {
        getItem: key => storage[key] ?? null,
        setItem: (key, value) => {
            storage[key] = String(value);
        }
    };
    const fetch = (url, init) => {
        const request = { body: JSON.parse(init.body), keepalive: init.keepalive, url };
        fetches.push(request);
        request.headers = init.headers;
        return Promise.resolve(fetchResult(request)).then(result => {
            if (result === 'network-error') {
                throw new TypeError('Failed to fetch');
            }
            const status = typeof result === 'number' ? result : result ? 200 : 503;
            return { ok: status >= 200 && status < 300, status };
        });
    };

    class FakeMutationObserver {
        constructor(callback) {
            this.callback = callback;
            this.connected = false;
            observers.push(this);
        }

        observe(target, options) {
            this.connected = true;
            this.target = target;
            this.options = options;
        }

        disconnect() {
            this.connected = false;
        }
    }

    const context = {
        Blob: ReportBlob,
        TextEncoder,
        MutationObserver: FakeMutationObserver,
        Uint8Array,
        Element: FakeElement,
        ErrorEvent: FakeErrorEvent,
        URL,
        clearTimeout: id => timers.delete(id),
        crypto: globalThis.crypto,
        document: {
            addEventListener: on('document'),
            baseURI: `${origin}/`,
            body: {},
            currentScript: { dataset: { reportUri: '/api/blazor-errors', stallTimeoutMs: '15000', ...(traceParent && { traceparent: traceParent }) } },
            getElementById: id => elements[id] ?? null,
            readyState: 'complete'
        },
        fetch,
        location: { href: `${origin}/start?token=secret`, origin },
        navigator: { onLine: true, ...(beaconResult && { sendBeacon: (url, body) => beacons.push({ body, url }) > 0 && beaconResult(body.report) }) },
        sessionStorage,
        setTimeout: callback => {
            const id = nextTimerId++;
            timers.set(id, callback);
            return id;
        }
    };
    context.window = context;
    context.addEventListener = on('window');
    context.Blazor = { addEventListener: on('blazor') };
    vm.runInNewContext(source, context);

    return {
        api: context.NTBlazorErrorBoundary,
        beacons,
        beaconedIds: () => beacons.map(beacon => beacon.body.report.referenceId),
        sent: () => JSON.parse(storage[sentKey] ?? '[]'),
        click: anchor => listeners['document:click'].forEach(listener => listener({ altKey: false, button: 0, ctrlKey: false, metaKey: false, shiftKey: false, target: anchor })),
        dispatchDocument: (type, event) => listeners[`document:${type}`].forEach(listener => listener(event)),
        elements,
        mutate: () => observers.filter(observer => observer.connected).forEach(observer => observer.callback([])),
        observers,
        dispatch: (type, event = {}) => listeners[`window:${type}`].forEach(listener => listener(event)),
        fetches,
        queue: () => JSON.parse(storage[queueKey] ?? '[]'),
        runTimers: () => {
            const pending = [...timers.values()];
            timers.clear();
            pending.forEach(callback => callback());
        }
    };
}

function deferred() {
    let resolve;
    const promise = new Promise(done => {
        resolve = done;
    });
    return { promise, resolve };
}

const settle = () => new Promise(resolve => setImmediate(resolve));

function reportKinds(browser) {
    return browser.fetches.map(request => request.body.reportKind);
}

test('Navigation_WithoutInteractiveReadyAttribute_DoesNotReportStall', async () => {
    const browser = loadScript();

    browser.click(new FakeElement({ href: '/claims#details' }));
    browser.runTimers();
    await settle();

    assert.deepEqual(reportKinds(browser), []);
    assert.equal(browser.api.getContext().navigationPhase, 'AnchorClicked');
});

test('Navigation_WithInteractiveReadyAttributeThatNeverCompletes_ReportsStall', async () => {
    const browser = loadScript();

    browser.click(new FakeElement({ href: '/claims/recovery/7', 'data-nt-require-interactive-ready': 'true' }));
    browser.runTimers();
    await settle();

    assert.deepEqual(reportKinds(browser), ['NavigationStalled']);
    assert.equal(browser.fetches[0].body.targetUri, `${origin}/claims/recovery/7`);
});

test('Navigation_WithInteractiveReadyAttributeThatCompletes_DoesNotReportStall', async () => {
    const browser = loadScript();

    browser.click(new FakeElement({ href: '/claims/recovery/7', 'data-nt-require-interactive-ready': 'true' }));
    browser.api.markInteractiveReady();
    browser.runTimers();
    await settle();

    assert.deepEqual(reportKinds(browser), []);
});

test('ResourceLoadError_IsNotReported', async () => {
    const browser = loadScript();

    browser.dispatch('error', { target: new FakeElement({ src: '/missing.png' }), type: 'error' });
    await settle();

    assert.deepEqual(reportKinds(browser), []);
});

test('ScriptError_IsReportedAsJavaScriptError', async () => {
    const browser = loadScript();

    browser.dispatch('error', new FakeErrorEvent({ colno: 4, error: new Error('boom'), filename: `${origin}/app.js?v=1`, lineno: 12, message: 'boom' }));
    await settle();

    assert.deepEqual(reportKinds(browser), ['JavaScriptError']);
    assert.equal(browser.fetches[0].body.exceptionMessage, 'boom');
    assert.equal(browser.fetches[0].body.javaScriptSource, `${origin}/app.js`);
});

test('ReportSubmittedDuringQueueFlush_RemainsQueued', async () => {
    const queuedUpload = deferred();
    const browser = loadScript({
        fetchResult: request => request.body.exceptionMessage === 'queued' ? queuedUpload.promise : false,
        storage: { [queueKey]: JSON.stringify([{ exceptionMessage: 'queued' }]) }
    });

    await browser.api.submitReport({ exceptionMessage: 'offline' });
    queuedUpload.resolve(true);
    await settle();

    assert.deepEqual(browser.queue().map(report => report.exceptionMessage), ['offline']);
});

test('ConcurrentQueueFlushes_SendEachQueuedReportOnce', async () => {
    const queuedUpload = deferred();
    const browser = loadScript({
        fetchResult: () => queuedUpload.promise,
        storage: { [queueKey]: JSON.stringify([{ exceptionMessage: 'queued' }]) }
    });

    browser.dispatch('online', {});
    queuedUpload.resolve(true);
    await settle();

    assert.equal(browser.fetches.length, 1);
    assert.deepEqual(browser.queue(), []);
});

test('QueueFlushFailure_KeepsReportQueued', async () => {
    const browser = loadScript({
        fetchResult: () => false,
        storage: { [queueKey]: JSON.stringify([{ exceptionMessage: 'queued' }]) }
    });

    await settle();

    assert.equal(browser.fetches.length, 1);
    assert.deepEqual(browser.queue().map(report => report.exceptionMessage), ['queued']);
});

const newModal = () => new FakeElement({ id: 'components-reconnect-modal' });
const reconnectEvent = state => ({ detail: { state }, type: 'components-reconnect-state-changed' });
const referenceIdPattern = /^[0-9A-HJKMNP-TV-Z]{8}$/;

test('ScriptWithTraceParent_ContextReportBodyAndHeaderCarryTraceParent', async () => {
    const traceParent = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01';
    const browser = loadScript({ traceParent });

    await browser.api.submitReport({ exceptionMessage: 'boom' });

    assert.equal(browser.api.getContext().traceParent, traceParent);
    assert.equal(browser.fetches[0].body.traceParent, traceParent);
    assert.equal(browser.fetches[0].headers.traceparent, traceParent);
});

test('ScriptWithoutTraceParent_ContextIsNullAndHeaderIsOmitted', async () => {
    const browser = loadScript();

    await browser.api.submitReport({ exceptionMessage: 'boom' });

    assert.equal(browser.api.getContext().traceParent, null);
    assert.equal('traceparent' in browser.fetches[0].headers, false);
});

test('ReportWithoutReferenceId_GetsEightCharacterCrockfordId', async () => {
    const browser = loadScript();

    await browser.api.submitReport({ exceptionMessage: 'boom' });

    assert.match(browser.fetches[0].body.referenceId, referenceIdPattern);
});

test('ReportWithReferenceId_KeepsProvidedReferenceId', async () => {
    const browser = loadScript();

    await browser.api.submitReport({ exceptionMessage: 'boom', referenceId: 'ABCD1234' });

    assert.equal(browser.fetches[0].body.referenceId, 'ABCD1234');
});

test('QueueReport_QueuesWithoutFetchingAndFlushesWhenOnline', async () => {
    const browser = loadScript();

    browser.api.queueReport({ exceptionMessage: 'failed post' });
    await settle();

    assert.equal(browser.fetches.length, 0);
    assert.deepEqual(browser.queue().map(report => report.exceptionMessage), ['failed post']);
    assert.match(browser.queue()[0].referenceId, referenceIdPattern);

    browser.dispatch('online', {});
    await settle();

    assert.equal(browser.fetches.length, 1);
    assert.deepEqual(browser.queue(), []);
});

test('QueueReportBeyondCap_KeepsNewestTwentyReports', () => {
    const browser = loadScript();

    for (let i = 0; i < 25; i++) {
        browser.api.queueReport({ exceptionMessage: `failure ${i}` });
    }

    assert.deepEqual(browser.queue().map(report => report.exceptionMessage), Array.from({ length: 20 }, (_, i) => `failure ${i + 5}`));
});

test('SubmitReportRejectedWith400_IsNotQueued', async () => {
    const browser = loadScript({ fetchResult: () => 400 });

    await browser.api.submitReport({ exceptionMessage: 'invalid' });

    assert.equal(browser.fetches.length, 1);
    assert.deepEqual(browser.queue(), []);
});

test('SubmitReportFailingWithRetryableStatusOrNetworkError_IsQueued', async () => {
    for (const result of [503, 408, 429, 'network-error']) {
        const browser = loadScript({ fetchResult: () => result });

        await browser.api.submitReport({ exceptionMessage: 'retry' });

        assert.deepEqual(browser.queue().map(report => report.exceptionMessage), ['retry'], String(result));
    }
});

const report = (referenceId, exceptionMessage = referenceId) => ({ boundaryName: 'Claims', exceptionMessage, exceptionType: 'Error', referenceId, reportKind: 'JavaScriptError' });

test('UploadStillInFlightWhenPageHides_IsNotQueuedOrResentByNextPage', async () => {
    const storage = {};
    const upload = deferred();
    const browser = loadScript({ storage, fetchResult: () => upload.promise });

    const submitted = browser.api.submitReport(report('INFLIGHT1'));
    await settle();
    // The browser finishes keepalive uploads after the page goes, but the page sees them fail after pagehide.
    browser.dispatch('pagehide');
    upload.resolve('network-error');
    await submitted;

    assert.deepEqual(browser.queue(), []);
    assert.deepEqual(browser.beacons, []);
    const nextPage = loadScript({ storage });
    await settle();
    assert.deepEqual(nextPage.fetches, []);
});

test('UploadInFlightWhenPageEntersBackForwardCache_IsQueuedIfItFailsAfterRestore', async () => {
    const storage = {};
    const upload = deferred();
    const browser = loadScript({ storage, fetchResult: () => upload.promise });

    const submitted = browser.api.submitReport(report('BFCACHE1'));
    await settle();
    // The page may come back from the back-forward cache, where an upload aborted while it was frozen fails like any other.
    browser.dispatch('pagehide', { persisted: true });
    upload.resolve('network-error');
    await submitted;

    assert.deepEqual(browser.sent(), []);
    assert.deepEqual(browser.queue().map(queued => queued.referenceId), ['BFCACHE1']);
});

test('ReportWithinKeepaliveLimit_IsUploadedWithKeepalive', async () => {
    const browser = loadScript();

    await browser.api.submitReport(report('SMALL001'));

    assert.equal(browser.fetches[0].keepalive, true);
});

test('QueuedReportBeyondKeepaliveLimit_IsRetriedWithoutKeepalive', async () => {
    const storage = {};
    const large = { ...report('LARGE001'), exceptionMessage: 'x'.repeat(64000) };
    const browser = loadScript({ storage, fetchResult: () => 'network-error' });
    await browser.api.submitReport(large);

    const nextPage = loadScript({ storage });
    await nextPage.api.submitReport(report('TRIGGER2'));
    await settle();

    const retry = nextPage.fetches.find(request => request.body.referenceId === 'LARGE001');
    assert.equal(retry.keepalive, false);
    assert.deepEqual(nextPage.queue(), []);
});

test('UploadFailingWhilePageIsVisible_IsQueuedAndSentByNextPage', async () => {
    const storage = {};
    const browser = loadScript({ storage, fetchResult: () => 'network-error' });

    await browser.api.submitReport(report('FAILED01'));

    assert.deepEqual(browser.queue().map(queued => queued.referenceId), ['FAILED01']);
    const nextPage = loadScript({ storage });
    await nextPage.api.submitReport(report('TRIGGER1'));
    await settle();
    assert.ok(nextPage.fetches.some(request => request.body.referenceId === 'FAILED01'));
});

test('QueuedReportAlreadyDelivered_IsDroppedWithoutSending', async () => {
    const storage = { [queueKey]: JSON.stringify([report('DONE0001')]), [sentKey]: JSON.stringify(['DONE0001']) };
    const browser = loadScript({ storage });

    browser.dispatch('online');
    await settle();
    browser.dispatch('pagehide');

    assert.deepEqual(browser.fetches, []);
    assert.deepEqual(browser.beacons, []);
    assert.deepEqual(browser.queue(), []);
});

test('ReportQueuedAfterDelivery_IsNotQueuedAgain', async () => {
    const browser = loadScript();

    await browser.api.submitReport(report('ONCE0001'));
    browser.api.queueReport(report('ONCE0001', 'same report, queued by the WebAssembly reporter'));

    assert.deepEqual(browser.queue(), []);
});

test('PageHideBeacon_SendsQueuedReportsWithoutRecordingThem', async () => {
    // The load-time flush fails, so the report is still queued when the page hides.
    const storage = { [queueKey]: JSON.stringify([report('BEACON01')]) };
    const browser = loadScript({ storage, fetchResult: () => 'network-error' });
    await settle();

    browser.dispatch('pagehide');

    assert.equal(browser.beacons.length, 1);
    assert.deepEqual(browser.queue(), []);
    // Recording beacons could push in-flight references out of the ten-entry record.
    assert.deepEqual(browser.sent(), []);
});

test('DeliveredReferences_KeepOnlyTheLastTen', async () => {
    const browser = loadScript();

    for (let index = 0; index < 12; index++) {
        await browser.api.submitReport(report(`REF${String(index).padStart(5, '0')}`));
    }

    assert.deepEqual(browser.sent(), Array.from({ length: 10 }, (_, index) => `REF${String(index + 2).padStart(5, '0')}`));
});

test('QueueFlushRejectedWith400_DropsReportButKeepsRetryableOnes', async () => {
    const browser = loadScript({
        fetchResult: request => request.body.exceptionMessage === 'invalid' ? 400 : 503,
        storage: { [queueKey]: JSON.stringify([{ exceptionMessage: 'invalid' }, { exceptionMessage: 'retry' }]) }
    });

    await settle();

    assert.equal(browser.fetches.length, 2);
    assert.deepEqual(browser.queue().map(report => report.exceptionMessage), ['retry']);
});

test('FrameworkResourceLoadError_IsReported', async () => {
    const browser = loadScript();

    browser.dispatch('error', { target: new FakeElement({ src: '/_framework/blazor.web.js?v=1', tagName: 'script' }), type: 'error' });
    browser.dispatch('error', { target: new FakeElement({ href: '/_content/Lib/blazor.server.js', tagName: 'link' }), type: 'error' });
    await settle();

    assert.deepEqual(reportKinds(browser), ['ResourceLoadFailed', 'ResourceLoadFailed']);
    assert.equal(browser.fetches[0].body.exceptionType, 'ResourceLoadFailed');
    assert.equal(browser.fetches[0].body.boundaryName, 'Browser');
    assert.equal(browser.fetches[0].body.javaScriptSource, `${origin}/_framework/blazor.web.js`);
});

test('NonFrameworkScriptAndImageLoadErrors_AreIgnored', async () => {
    const browser = loadScript();

    browser.dispatch('error', { target: new FakeElement({ src: '/app.js', tagName: 'script' }), type: 'error' });
    browser.dispatch('error', { target: new FakeElement({ src: '/_framework/logo.png', tagName: 'img' }), type: 'error' });
    await settle();

    assert.deepEqual(reportKinds(browser), []);
});

test('ReconnectModalClassChangesToFailed_ReportsOnceWhileStateHolds', async () => {
    const modal = newModal();
    const browser = loadScript({ modal });

    modal.classList.add('components-reconnect-failed');
    browser.mutate();
    browser.mutate();
    await settle();

    assert.deepEqual(reportKinds(browser), ['ReconnectFailed']);
    assert.equal(browser.fetches[0].body.exceptionType, 'ReconnectFailed');
    assert.equal(browser.fetches[0].body.boundaryName, 'BlazorReconnect');
    assert.match(browser.fetches[0].body.exceptionMessage, /failed/);
    assert.deepEqual(browser.observers.map(observer => [...observer.options.attributeFilter]), [['class']]);
});

test('ReconnectModalClassChangesToRejected_ReportsRejectedState', async () => {
    const modal = newModal();
    const browser = loadScript({ modal });

    modal.classList.add('components-reconnect-rejected');
    browser.mutate();
    await settle();

    assert.match(browser.fetches[0].body.exceptionMessage, /rejected/);
});

test('ReconnectModalInsertedLater_StopsWatchingBodyAndWatchesModalClass', async () => {
    const browser = loadScript();
    const [finder] = browser.observers;
    const modal = newModal();

    assert.equal(finder.options.subtree, true);
    browser.elements[modal.id] = modal;
    browser.mutate();
    modal.classList.add('components-reconnect-failed');
    browser.mutate();
    await settle();

    assert.equal(finder.connected, false);
    assert.deepEqual(reportKinds(browser), ['ReconnectFailed']);
});

test('ReconnectStateChangedEvent_ReportsFailureStatesOnly', async () => {
    const browser = loadScript();

    for (const state of ['show', 'retrying', 'paused', 'hide', 'failed']) {
        browser.dispatchDocument('components-reconnect-state-changed', reconnectEvent(state));
    }
    await settle();

    assert.deepEqual(reportKinds(browser), ['ReconnectFailed']);

    browser.dispatchDocument('components-reconnect-state-changed', reconnectEvent('rejected'));
    await settle();

    assert.match(browser.fetches[1].body.exceptionMessage, /rejected/);
});

test('ReconnectStateChangedEventResumeFailed_Reports', async () => {
    const browser = loadScript();

    browser.dispatchDocument('components-reconnect-state-changed', reconnectEvent('resume-failed'));
    await settle();

    assert.match(browser.fetches[0].body.exceptionMessage, /resume-failed/);
});

test('BuiltInReconnectUiShowsFailedText_ReportsOnceWhileTextHolds', async () => {
    const modal = newModal();
    const status = { textContent: '' };
    modal.shadowRoot = { querySelector: selector => selector === 'p' ? status : null };
    const browser = loadScript({ modal });

    status.textContent = 'Rejoining the server...';
    browser.mutate();
    await settle();
    assert.deepEqual(reportKinds(browser), []);

    status.textContent = 'Failed to rejoin.Please retry or reload the page.';
    browser.mutate();
    browser.mutate();
    await settle();

    assert.deepEqual(reportKinds(browser), ['ReconnectFailed']);
    assert.match(browser.fetches[0].body.exceptionMessage, /failed/);
    assert.equal(browser.observers[0].target, modal.shadowRoot);
    assert.equal(browser.observers[0].options.subtree, true);
});

test('ReconnectFailureSeenByClassAndEvent_ReportsOnce', async () => {
    const modal = newModal();
    const browser = loadScript({ modal });

    modal.classList.add('components-reconnect-failed');
    browser.mutate();
    browser.dispatchDocument('components-reconnect-state-changed', reconnectEvent('failed'));
    await settle();

    assert.deepEqual(reportKinds(browser), ['ReconnectFailed']);
});

test('DuplicateBoundaryReportsWithinWindow_AreSentOnce', async () => {
    const browser = loadScript();
    const report = { boundaryName: 'Claims', exceptionMessage: 'loop', exceptionType: 'System.Exception', reportKind: 'Exception' };

    await browser.api.submitReport({ ...report });
    await browser.api.submitReport({ ...report });
    browser.api.queueReport({ ...report });
    await browser.api.submitReport({ ...report, exceptionMessage: 'different' });

    assert.deepEqual(browser.fetches.map(request => request.body.exceptionMessage), ['loop', 'different']);
    assert.deepEqual(browser.queue(), []);
});

const referenceIds = reports => reports.map(queued => queued.referenceId);
const fetchedIds = browser => browser.fetches.map(request => request.body.referenceId);

test('FailedUploadThenPageHide_IsBeaconedNotTreatedAsInFlight', async () => {
    const browser = loadScript({ fetchResult: () => 'network-error' });

    await browser.api.submitReport(report('FAILED01'));
    browser.dispatch('pagehide');

    assert.deepEqual(browser.beaconedIds(), ['FAILED01']);
    assert.deepEqual(browser.queue(), []);
    assert.deepEqual(browser.sent(), []);
});

test('PageHideWithoutSendBeacon_StillRecordsInFlightUpload', async () => {
    const storage = { [queueKey]: JSON.stringify([report('QUEUED01')]) };
    const upload = deferred();
    const browser = loadScript({ storage, beaconResult: null, fetchResult: request => request.body.referenceId === 'INFLIGHT' ? upload.promise : 'network-error' });
    await settle();

    const submitted = browser.api.submitReport(report('INFLIGHT'));
    await settle();
    browser.dispatch('pagehide');
    upload.resolve('network-error');
    await submitted;

    assert.deepEqual(browser.sent(), ['INFLIGHT']);
    assert.deepEqual(referenceIds(browser.queue()), ['QUEUED01']);
    const nextPage = loadScript({ storage });
    await settle();
    assert.deepEqual(fetchedIds(nextPage), ['QUEUED01']);
});

test('PageHideBeaconRejected_KeepsReportQueued', async () => {
    const storage = { [queueKey]: JSON.stringify([report('BEACON01'), report('BEACON02'), report('BEACON03')]) };
    const browser = loadScript({ storage, fetchResult: () => 'network-error', beaconResult: beaconed => beaconed.referenceId !== 'BEACON02' });
    await settle();

    browser.dispatch('pagehide');

    assert.deepEqual(browser.beaconedIds(), ['BEACON01', 'BEACON02', 'BEACON03']);
    assert.deepEqual(referenceIds(browser.queue()), ['BEACON02']);
    assert.deepEqual(browser.sent(), []);
});

test('PageHideBeaconingManyReports_KeepsInFlightReferenceDelivered', async () => {
    // More beacons than the ten-entry sent record holds: recording them would evict the in-flight reference.
    const queued = Array.from({ length: 12 }, (_, index) => report(`QUEUED${String(index).padStart(2, '0')}`));
    const storage = { [queueKey]: JSON.stringify(queued) };
    const upload = deferred();
    const browser = loadScript({ storage, fetchResult: request => request.body.referenceId === 'INFLIGHT' ? upload.promise : 'network-error' });
    await settle();

    const submitted = browser.api.submitReport(report('INFLIGHT'));
    await settle();
    browser.dispatch('pagehide');
    upload.resolve('network-error');
    await submitted;

    assert.deepEqual(browser.beaconedIds(), referenceIds(queued));
    assert.deepEqual(browser.sent(), ['INFLIGHT']);
    assert.deepEqual(browser.queue(), []);
    const nextPage = loadScript({ storage });
    await settle();
    assert.deepEqual(nextPage.fetches, []);
});

test('PageHideDuringQueueFlush_LeavesUnclaimedReportsQueuedOrBeaconed', async () => {
    for (const hasBeacon of [true, false]) {
        const storage = { [queueKey]: JSON.stringify([report('FLUSH001'), report('FLUSH002'), report('FLUSH003')]) };
        const upload = deferred();
        const browser = loadScript({ storage, beaconResult: hasBeacon ? () => true : null, fetchResult: () => upload.promise });
        // The load-time flush has claimed the first report and is waiting for its upload.
        await settle();

        browser.dispatch('pagehide');

        assert.deepEqual(fetchedIds(browser), ['FLUSH001'], `beacon: ${hasBeacon}`);
        assert.deepEqual(browser.sent(), ['FLUSH001'], `beacon: ${hasBeacon}`);
        assert.deepEqual(hasBeacon ? browser.beaconedIds() : referenceIds(browser.queue()), ['FLUSH002', 'FLUSH003'], `beacon: ${hasBeacon}`);
        assert.deepEqual(hasBeacon ? browser.queue() : browser.beacons, [], `beacon: ${hasBeacon}`);

        // The browser completes the claimed upload after the page goes; the page sees it fail, but must not queue it again.
        upload.resolve('network-error');
        await settle();
        assert.ok(!referenceIds(browser.queue()).includes('FLUSH001'), `beacon: ${hasBeacon}`);
    }
});

test('QueuedNullAndNonObjectEntries_AreSkippedAndLaterReportsStillSent', async () => {
    const storage = { [queueKey]: JSON.stringify([null, 5, 'text', true, report('VALID001')]) };
    const browser = loadScript({ storage });

    await settle();

    assert.deepEqual(fetchedIds(browser), ['VALID001']);
    assert.deepEqual(browser.queue(), []);
});

test('PageHideWithCorruptQueueEntry_DoesNotThrow', async () => {
    const storage = {};
    const browser = loadScript({ storage, fetchResult: () => 'network-error' });
    await settle();
    storage[queueKey] = JSON.stringify([null, 'text', 7, report('VALID001')]);

    assert.doesNotThrow(() => browser.dispatch('pagehide'));

    assert.deepEqual(browser.beaconedIds(), ['VALID001']);
    assert.deepEqual(browser.queue(), []);
});

test('LegacyQueuedReportWithoutReferenceId_GetsOneAndIsNotResent', async () => {
    const storage = { [queueKey]: JSON.stringify([{ boundaryName: 'Claims', exceptionMessage: 'legacy', exceptionType: 'Error', reportKind: 'JavaScriptError' }]) };
    const upload = deferred();
    const browser = loadScript({ storage, fetchResult: () => upload.promise });
    await settle();
    const referenceId = browser.fetches[0].body.referenceId;

    browser.dispatch('pagehide');
    upload.resolve('network-error');
    await settle();

    assert.match(referenceId, referenceIdPattern);
    assert.deepEqual(browser.sent(), [referenceId]);
    assert.deepEqual(browser.queue(), []);
    const nextPage = loadScript({ storage });
    await settle();
    assert.deepEqual(nextPage.fetches, []);
});

test('CorruptedSentRecord_IsTreatedAsEmpty', async () => {
    const corruptRecords = {
        'object': '{"0":"QUEUED01","length":1}',
        // A string record would match any reference ID it contains.
        'string': '"xxQUEUED01xx"',
        'invalid JSON': '["QUEUED01"',
        'non-string elements': '[null,1,{"id":"QUEUED01"},["QUEUED01"]]'
    };
    for (const [label, corrupt] of Object.entries(corruptRecords)) {
        const storage = { [queueKey]: JSON.stringify([report('QUEUED01')]), [sentKey]: corrupt };
        const browser = loadScript({ storage });

        await settle();

        assert.deepEqual(fetchedIds(browser), ['QUEUED01'], label);
        assert.deepEqual(browser.sent(), ['QUEUED01'], label);
        assert.deepEqual(browser.queue(), [], label);
    }
});

test('SessionStorageThrowing_SubmitAndPageHideDoNotThrow', async () => {
    // Storage that is disabled or blocked throws on every access.
    const blockedStorage = new Proxy({}, {
        get() {
            throw new Error('SecurityError: access to sessionStorage is denied');
        }
    });
    const upload = deferred();
    const browser = loadScript({ sessionStorage: blockedStorage, fetchResult: request => request.body.referenceId === 'INFLIGHT' ? upload.promise : 'network-error' });

    assert.equal(await browser.api.submitReport(report('FAILED01')), true);
    browser.api.queueReport(report('QUEUED01'));
    const submitted = browser.api.submitReport(report('INFLIGHT'));
    await settle();
    assert.doesNotThrow(() => browser.dispatch('pagehide'));
    browser.dispatch('online');
    upload.resolve(true);

    assert.equal(await submitted, true);
    await settle();
    assert.deepEqual(fetchedIds(browser), ['FAILED01', 'INFLIGHT']);
    assert.deepEqual(browser.beacons, []);
});

test('DuplicateReferenceRecorded_IsKeptOnce', async () => {
    const storage = { [sentKey]: JSON.stringify(['DUP00001', 'OTHER001']) };
    const browser = loadScript({ storage });

    await browser.api.submitReport(report('DUP00001', 'first'));
    await browser.api.submitReport(report('DUP00001', 'second'));

    assert.deepEqual(fetchedIds(browser), ['DUP00001', 'DUP00001']);
    assert.deepEqual(browser.sent(), ['OTHER001', 'DUP00001']);
});
