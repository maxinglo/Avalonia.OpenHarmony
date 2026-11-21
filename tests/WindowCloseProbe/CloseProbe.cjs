const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
if (!process.argv[2]) throw new Error('Pass the path to the DevEco SDK typescript.js.');
const ts = require(path.resolve(process.argv[2]));
const source = fs.readFileSync(path.resolve(__dirname, '../../src/Avalonia.OpenHarmony.ArkTS/ets/entryability/ChildWindows.ets'), 'utf8');
const native = new Map();
const calls = [];
const events = [];
const nativeTypes = { WINDOW_SHOWN: 1, WINDOW_HIDDEN: 4, WINDOW_DESTROYED: 5 };
const missing = Object.assign(new Error('no window'), { code: 1300002 });
const window = {
  ModalityType: { WINDOW_MODALITY: 0 },
  WindowEventType: nativeTypes,
  findWindow(name) {
    if (!native.has(name)) throw missing;
    return native.get(name);
  }
};
const bridge = {
  reportChildEvent: (key, type) => events.push([key, type]),
  reportChildFrame: () => {},
  requestChildClose: async () => false
};
const dependencies = {
  '@kit.ArkUI': { window },
  '@kit.PerformanceAnalysisKit': { hilog: { warn() {} } },
  'libavalonia_ohos_bridge.so': { default: bridge }
};
const mod = { exports: {} };
const compiled = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
});
vm.runInNewContext(compiled.outputText, {
  exports: mod.exports,
  require: name => dependencies[name],
  LocalStorage: class { setOrCreate() {} }
});
const ChildWindows = mod.exports.ChildWindows;
const rect = { left: 0, top: 0, width: 800, height: 600 };

class NativeWindow {
  constructor(name, options, behavior) {
    this.name = name;
    this.options = options;
    this.behavior = behavior;
    this.listeners = new Map();
    this.visible = false;
    this.modal = !!options.isModal;
    this.destroyCalls = 0;
  }
  on(type, callback) { this.listeners.set(type, callback); }
  getWindowProperties() { return { id: 18, displayId: 0, windowRect: rect }; }
  async loadContent() { if (this.behavior.loadFails) throw new Error('load failed'); }
  async showWindow() { this.visible = true; }
  async destroyWindow() {
    this.destroyCalls++;
    if (this.behavior.destroyFails-- > 0) {
      if (this.behavior.absentOnRejection) native.delete(this.name);
      throw Object.assign(new Error('destroy rejected'), { code: 1300002 });
    }
    this.destroyNow();
  }
  destroyNow() {
    native.delete(this.name);
    this.visible = false;
    this.listeners.get('windowEvent')?.(nativeTypes.WINDOW_DESTROYED);
  }
  async setSubWindowModal(value) { this.modal = value; }
  async minimize() {
    if (this.behavior.hideFails) throw new Error('hide rejected');
    this.visible = false;
  }
  isWindowShowing() { return this.visible; }
}

let next = { destroyFails: 0 };
let suspendCreate;
const stage = {
  async createSubWindowWithOptions(name, options) {
    if (suspendCreate) await suspendCreate;
    const child = new NativeWindow(name, options, next);
    native.set(name, child);
    return child;
  }
};
const main = { getWindowProperties: () => ({ windowRect: rect }) };
const windows = new ChildWindows(stage, main, false);
const send = (id, operation, modal = false) =>
  windows.execute(JSON.stringify({ id, operation, activate: true, modal, decorated: false }));
const has = (key, event) => events.some(item => item[0] === key && item[1] === event);

(async () => {
  assert.ok(has(0, 5));
  next = { destroyFails: 2 };
  assert.equal(await send(1, 'show', true), '');
  const first = native.get('AvaloniaChild1');
  assert.equal(await send(1, 'close'), null);
  assert.equal(native.get('AvaloniaChild1'), first);
  assert.equal(first.modal, false);
  assert.equal(first.visible, false);
  assert.ok(has(1, 6));
  assert.ok(has(1, 1));
  assert.ok(!has(1, 4));
  assert.equal(await send(1, 'close'), null);
  assert.equal(native.get('AvaloniaChild1'), first);
  assert.ok(!has(1, 4));
  assert.equal(await send(1, 'close'), '');
  assert.equal(first.destroyCalls, 3);
  assert.ok(has(1, 4));
  assert.equal(native.has('AvaloniaChild1'), false);
  calls.push('Failed destroy retains handle and cancels modality before successful retry');

  next = { loadFails: true, destroyFails: 1 };
  assert.equal(await send(2, 'show', true), null);
  const second = native.get('AvaloniaChild2');
  assert.equal(second.destroyCalls, 1);
  assert.ok(!has(2, 4));
  assert.equal(await send(2, 'close'), '');
  assert.equal(second.destroyCalls, 2);
  calls.push('Partial show and failed cleanup keep their exact window for retry');

  next = { hideFails: true, destroyFails: 1 };
  assert.equal(await send(3, 'show', true), '');
  assert.equal(await send(3, 'close'), null);
  assert.ok(has(3, 6));
  assert.ok(!has(3, 1));
  assert.ok(!has(3, 4));
  assert.equal(await send(3, 'close'), '');
  calls.push('A released modal lease is distinct from a failed hide');

  next = { destroyFails: 1, absentOnRejection: true };
  assert.equal(await send(4, 'show'), '');
  assert.equal(await send(4, 'close'), '');
  assert.equal(native.has('AvaloniaChild4'), false);
  assert.ok(has(4, 4));
  calls.push('Explicit native name lookup recognizes a window destroyed despite a rejected promise');

  next = { destroyFails: 0 };
  assert.equal(await send(5, 'show'), '');
  const fifth = native.get('AvaloniaChild5');
  assert.deepEqual(await Promise.all([send(5, 'close'), send(5, 'close')]), ['', '']);
  assert.equal(fifth.destroyCalls, 1);
  calls.push('Concurrent duplicate Close requests share one native destroy');

  let finish;
  suspendCreate = new Promise(resolve => { finish = resolve; });
  const showing = send(6, 'show');
  windows.stop();
  finish();
  assert.equal(await showing, null);
  assert.equal(native.has('AvaloniaChild6'), false);
  calls.push('Stage shutdown closes a child created by an in-flight show');

  for (const line of calls) console.log(line);
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
