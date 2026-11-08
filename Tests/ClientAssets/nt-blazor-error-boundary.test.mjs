import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const scriptUrl = new URL('../../NT.Blazor.ErrorBoundary/wwwroot/nt-blazor-error-boundary.js', import.meta.url);
const source = await readFile(scriptUrl, 'utf8').catch(() => {
    throw new Error(`Build NT.Blazor.ErrorBoundary or run "npm run build" first; ${scriptUrl.pathname} is missing.`);
});
const queueKey = 'nt.blazor.error-boundary.queue';
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

function loadScript({ storage = {}, fetchResult = () => true, traceParent, modal } = {}) {
    const listeners = {};
    const timers = new Map();
    const fetches = [];
    const observers = [];
    const elements = modal ? { [modal.id]: modal } : {};
    let nextTimerId = 1;

    const on = target => (type, listener) => {
        (listeners[`${target}:${type}`] ??= []).push(listener);
    };
    const sessionStorage = {
        getItem: key => storage[key] ?? null,
        setItem: (key, value) => {
            storage[key] = String(value);
        }
    };
    const fetch = (url, init) => {
        const request = { body: JSON.parse(init.body), url };
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
        Blob,
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
        navigator: { onLine: true, sendBeacon: () => true },
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
        click: anchor => listeners['document:click'].forEach(listener => listener({ altKey: false, button: 0, ctrlKey: false, metaKey: false, shiftKey: false, target: anchor })),
        dispatchDocument: (type, event) => listeners[`document:${type}`].forEach(listener => listener(event)),
        elements,
        mutate: () => observers.filter(observer => observer.connected).forEach(observer => observer.callback([])),
        observers,
        dispatch: (type, event) => listeners[`window:${type}`].forEach(listener => listener(event)),
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
