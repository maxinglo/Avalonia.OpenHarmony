const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

if (!process.argv[2]) throw new Error('Pass the path to the DevEco SDK typescript.js.');
const ts = require(path.resolve(process.argv[2]));
const components = path.resolve(__dirname, '../../src/Avalonia.OpenHarmony.ArkTS/ets/components');
const types = {
  PLAIN_TEXT: 'general.plain-text', FILE: 'general.file', FILE_URI: 'general.file-uri',
  HTML: 'general.html', HYPERLINK: 'general.hyperlink', IMAGE: 'general.image',
  VIDEO: 'general.video', AUDIO: 'general.audio', FOLDER: 'general.folder'
};
const permissions = { NONE: 0, READ: 1, WRITE: 2, PERSIST: 3 };
const behavior = { COPY: 0, MOVE: 1 };
const result = { DRAG_SUCCESSFUL: 0, DRAG_FAILED: 1, DRAG_CANCELED: 2, DROP_ENABLED: 3, DROP_DISABLED: 4 };

class UnifiedRecord {
  entries = new Map();
  addEntry(type, data) { this.entries.set(type, data); }
  getEntry(type) { return this.entries.get(type); }
  getTypes() { return Array.from(this.entries.keys()); }
}
class PlainText extends UnifiedRecord {
  constructor() { super(); this.addEntry(types.PLAIN_TEXT, this); }
}
class UnifiedData {
  records = [];
  addRecord(record) { this.records.push(record); }
  getRecords() { return this.records; }
}
const fileOpens = [];
const fileCloses = [];
const readOnlyUri = 'file://source/readonly.txt';
const writableUri = 'file://source/writable.txt';
const modes = { READ_ONLY: 0, READ_WRITE: 2, NONBLOCK: 4 };
const fileIo = {
  OpenMode: modes,
  openSync(uri, mode) {
    fileOpens.push([uri, mode]);
    if (uri === readOnlyUri && (mode & modes.READ_WRITE)) throw new Error('Write not authorized');
    return { fd: fileOpens.length + 10 };
  },
  statSync() { return { size: 7, isFile: () => true }; },
  closeSync(file) { fileCloses.push(file.fd); }
};
const calls = [];
const bridge = {
  dragEffect(surface, phase, _x, _y, _modifiers, formats, payload) {
    calls.push({ surface, phase, formats, payload: payload && JSON.parse(payload) });
    return (formats & 16) && !(formats & 2) ? 2 : 1;
  },
  drag() { return false; }
};
const dependencies = {
  '@kit.ArkData': {
    unifiedDataChannel: {
      UnifiedData, UnifiedRecord, PlainText, HTML: class extends UnifiedRecord {},
      Hyperlink: class extends UnifiedRecord {}, File: class extends UnifiedRecord {}, UriPermission: permissions
    },
    uniformTypeDescriptor: { UniformDataType: types, getUniformDataTypeByFilenameExtension: () => types.FILE }
  },
  '@kit.CoreFileKit': { fileIo },
  '@kit.PerformanceAnalysisKit': { hilog: { debug() {}, warn() {} } },
  'libavalonia_ohos_bridge.so': bridge
};

class OffscreenCanvasRenderingContext2D {
  measureText(text) { return { width: Array.from(text).length * 10 }; }
  beginPath() {}
  roundRect() {}
  fill() {}
  fillText(text) { this.text = text; }
  getPixelMap(_x, _y, width, height) {
    if (this.text === 'fail preview') throw new Error('Could not draw preview');
    return { width, height, text: this.text };
  }
}

const timers = new Map();
let nextTimer = 1;
function setTimer(callback, delay) {
  assert.equal(delay, 80);
  const id = nextTimer++;
  timers.set(id, callback);
  return id;
}

function expireMouseOffer() {
  assert.equal(timers.size, 1);
  const [id, callback] = timers.entries().next().value;
  timers.delete(id);
  callback();
}

function load(name) {
  const source = fs.readFileSync(path.join(components, name + '.ets'), 'utf8');
  const output = ts.transpileModule(source, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, esModuleInterop: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(output, {
    exports, require: (module) => dependencies[module], DragResult: result, DragBehavior: behavior,
    OffscreenCanvasRenderingContext2D, RenderingContextSettings: class {},
    setTimeout: setTimer, clearTimeout: id => timers.delete(id)
  });
  return exports[name];
}

async function verify() {
  const Source = load('AvaloniaTextDragSource');
  const Receiver = load('AvaloniaDropReceiver');
  const nativeTouch = [];
  const mouseDecisions = [];
  let deferredEvent;
  let inDragUiScope = false;
  const source = new Source(async (pointer, data, previewLabel) => {
    nativeTouch.push([pointer, data.getRecords().map(record => record.textContent), Source.createPreview(previewLabel)]);
    return 'copy';
  }, (waiting, updateEvent) => {
    inDragUiScope = true;
    try {
      updateEvent?.();
      mouseDecisions.push([waiting, deferredEvent.data?.getRecords()[0].textContent, deferredEvent.result]);
    } finally {
      inDragUiScope = false;
    }
  });
  const send = (operation, offer) => Source.execute(operation, JSON.stringify(offer));
  const touch = source.contact(4, 0);
  assert.equal(await send('drag.texts', { gesture: touch, texts: ['first\nline 中文 🎧', 'third'] }), 'copy');
  assert.equal(nativeTouch[0][0], 4);
  assert.deepEqual(nativeTouch[0][1], ['first\nline 中文 🎧', 'third']);
  assert.equal(nativeTouch[0][2].pixelMap.text, 'first line 中文 🎧');
  assert.equal(await send('drag.text', { gesture: touch, text: 'stale' }), null);

  const event = { setData(data) { this.data = data; } };
  assert.equal(source.startMouseDrag(event), undefined);
  let mouse = source.beginMousePress();
  let pending = send('drag.text', { gesture: mouse, text: 'released without a drag' });
  source.endMousePress();
  assert.equal(await pending, null);
  assert.equal(source.startMouseDrag(event), undefined);

  mouse = source.beginMousePress();
  pending = send('drag.data', {
    gesture: mouse, effects: 3, items: [{ text: 'description', html: '<p>中文</p>', link: 'https://avaloniaui.net/' }]
  });
  assert.equal(source.startMouseDrag(event).pixelMap.text, 'description');
  assert.equal(event.data.getRecords()[0].getEntry(types.HTML).htmlContent, '<p>中文</p>');
  assert.deepEqual(Array.from(event.data.getRecords()[0].getEntry(types.HTML).uriAuthorizationPolicies), [permissions.NONE]);
  assert.equal(event.data.getRecords()[0].getEntry(types.HYPERLINK).url, 'https://avaloniaui.net/');
  source.endMousePress();
  source.finishMouseDrag('move');
  assert.equal(await pending, 'move');

  mouse = source.beginMousePress();
  pending = send('drag.data', { gesture: mouse, effects: 1, items: [{ html: '<p>中文</p>' }] });
  assert.equal(source.startMouseDrag(event).pixelMap.text, 'Formatted text');
  source.finishMouseDrag('move');
  assert.equal(await pending, null);

  mouse = source.beginMousePress();
  pending = send('drag.data', { gesture: mouse, effects: 1, items: [{ link: 'https://example.org/' }] });
  assert.equal(source.startMouseDrag(event).pixelMap.text, 'https://example.org/');
  source.finishMouseDrag('copy');
  assert.equal(await pending, 'copy');

  mouse = source.beginMousePress();
  pending = send('drag.text', { gesture: mouse, text: '🎧'.repeat(100) });
  const longPreview = source.startMouseDrag(event).pixelMap;
  assert.ok(longPreview.width <= 240 && longPreview.height === 42);
  assert.equal(longPreview.text, '🎧'.repeat(20) + '…');
  assert.equal(event.data.getRecords()[0].textContent, '🎧'.repeat(100));
  source.finishMouseDrag('copy');
  assert.equal(await pending, 'copy');

  const newEvent = { setData(data) { this.data = data; } };
  mouse = source.beginMousePress();
  pending = send('drag.text', { gesture: mouse, text: 'fail preview' });
  assert.equal(source.startMouseDrag(newEvent), undefined);
  assert.equal(await pending, null);
  assert.equal(newEvent.data, undefined);

  mouse = source.beginMousePress();
  assert.equal(await send('drag.data', { gesture: mouse, effects: 1, items: [{ filePath: '/files/sample.txt' }] }), null);
  assert.equal(source.startMouseDrag(newEvent), undefined);
  mouse = source.beginMousePress();
  assert.equal(await send('drag.data', {
    gesture: mouse, effects: 3, items: [{ text: 'caption' }, { text: 'file', filePath: '/files/sample.txt' }]
  }), null);
  assert.equal(source.startMouseDrag(newEvent), undefined);

  const fallback = Source.createPreview('Text');
  const createMouseEvent = () => ({
    setData(data) {
      assert.equal(inDragUiScope, true);
      this.data = data;
    },
    setResult(dragResult) {
      assert.equal(inDragUiScope, true);
      this.result = dragResult;
    }
  });
  deferredEvent = createMouseEvent();
  assert.equal(source.startMouseDrag(deferredEvent, fallback), undefined);
  assert.equal(timers.size, 0);

  mouse = source.beginMousePress();
  assert.equal(source.startMouseDrag(deferredEvent, fallback), fallback);
  assert.deepEqual(mouseDecisions, [[true, undefined, undefined]]);
  pending = send('drag.text', { gesture: mouse, text: 'late offer 中文' });
  assert.deepEqual(mouseDecisions.at(-1), [false, 'late offer 中文', undefined]);
  assert.equal(timers.size, 0);
  source.finishMouseDrag('copy');
  assert.equal(await pending, 'copy');

  deferredEvent = createMouseEvent();
  mouse = source.beginMousePress();
  assert.equal(source.startMouseDrag(deferredEvent, fallback), fallback);
  source.endMousePress();
  assert.deepEqual(mouseDecisions.at(-1), [false, undefined, result.DRAG_CANCELED]);
  assert.equal(timers.size, 0);
  assert.equal(await send('drag.text', { gesture: mouse, text: 'released' }), null);

  deferredEvent = createMouseEvent();
  mouse = source.beginMousePress();
  assert.equal(source.startMouseDrag(deferredEvent, fallback), fallback);
  expireMouseOffer();
  assert.deepEqual(mouseDecisions.at(-1), [false, undefined, result.DRAG_CANCELED]);
  assert.equal(await send('drag.text', { gesture: mouse, text: 'expired' }), null);

  deferredEvent = createMouseEvent();
  mouse = source.beginMousePress();
  assert.equal(source.startMouseDrag(deferredEvent, fallback), fallback);
  const decisionsBeforeNativeEnd = mouseDecisions.length;
  source.finishMouseDrag(null);
  assert.equal(mouseDecisions.length, decisionsBeforeNativeEnd);
  assert.equal(timers.size, 0);
  assert.equal(await send('drag.text', { gesture: mouse, text: 'ended by ArkUI' }), null);

  const receiver = new Receiver();
  const receive = (records, shift = false) => {
    const drop = {
      getModifierKeyState: (keys) => shift && keys[0] === 'Shift',
      getData: () => ({ getRecords: () => records }),
      setResult(value) { this.result = value; }
    };
    receiver.dispatch('surface', 3, 20, 20, drop);
    return drop;
  };
  const file = (oriUri, uriAuthorizationPolicies) => {
    const record = new UnifiedRecord();
    record.addEntry(types.FILE_URI, { oriUri, uriAuthorizationPolicies, fileType: types.FILE });
    return record;
  };
  const rich = new UnifiedRecord();
  rich.addEntry(types.HTML, { htmlContent: '<p>text</p>', plainContent: 'text' });
  rich.addEntry(types.HYPERLINK, { url: 'https://avaloniaui.net/' });

  assert.equal(receive([rich], true).dragBehavior, behavior.MOVE);
  assert.deepEqual(calls.at(-1).payload.items, [
    { text: 'text', html: '<p>text</p>', link: 'https://avaloniaui.net/' }
  ]);
  assert.equal(receive([file(writableUri, [permissions.READ])], true).dragBehavior, behavior.COPY);
  assert.deepEqual(fileOpens, [[writableUri, modes.NONBLOCK]]);
  assert.equal(calls.at(-1).payload.files[0].writable, false);

  receive([file(writableUri, [permissions.READ, permissions.WRITE])]);
  assert.deepEqual(fileOpens.at(-1), [writableUri, modes.READ_WRITE | modes.NONBLOCK]);
  assert.equal(calls.at(-1).payload.items[0].file.writable, true);
  receive([file(readOnlyUri, [permissions.READ, permissions.WRITE])]);
  assert.deepEqual(fileOpens.slice(-2), [[readOnlyUri, modes.READ_WRITE | modes.NONBLOCK], [readOnlyUri, modes.NONBLOCK]]);
  assert.equal(calls.at(-1).payload.files[0].writable, false);
  assert.deepEqual(fileCloses, [11, 12, 14]);
  console.log('ArkTS drag previews, delayed mouse offers, outbound file rejection, inbound Copy/Move, and FileKit write fallback passed.');
}

verify().catch(error => { console.error(error); process.exitCode = 1; });
