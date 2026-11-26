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
let rejectStageEvent;
const focusTransfers = [];
const titleBarLayouts = [];
const titleBarPresses = [];
let supportsTitleBar = false;
let titleBarPressRoute = 1;
const nativeTypes = { WINDOW_SHOWN: 1, WINDOW_HIDDEN: 4, WINDOW_DESTROYED: 5 };
const statuses = { FULL_SCREEN: 1, MAXIMIZE: 2, MINIMIZE: 3, FLOATING: 4 };
const presentations = { EXIT_IMMERSIVE: 1, ENTER_IMMERSIVE_DISABLE_TITLE_AND_DOCK_HOVER: 3 };
const colors = { COLOR_MODE_NOT_SET: -1, COLOR_MODE_DARK: 0, COLOR_MODE_LIGHT: 1 };
const missing = Object.assign(new Error('no window'), { code: 1300002 });
const window = {
  ModalityType: { WINDOW_MODALITY: 0 },
  WindowEventType: nativeTypes,
  WindowStatusType: statuses,
  MaximizePresentation: presentations,
  findWindow(name) {
    if (!native.has(name)) throw missing;
    return native.get(name);
  },
  async shiftAppWindowFocus(source, target) {
    focusTransfers.push([source, target]);
    main.focused = true;
  }
};
const bridge = {
  reportChildEvent(key, type) {
    if (key === 0 && type === rejectStageEvent) {
      rejectStageEvent = undefined;
      throw new Error('stage notification rejected');
    }
    events.push([key, type]);
  },
  reportChildFrame: () => {},
  supportsTitleBar: () => supportsTitleBar,
  reportTitleBar: (...values) => titleBarLayouts.push(values),
  titleBarPress(...values) {
    titleBarPresses.push(values);
    return titleBarPressRoute;
  },
  requestChildClose: async () => false
};
const dependencies = {
  '@kit.AbilityKit': { ConfigurationConstant: { ColorMode: colors } },
  '@kit.ArkUI': { window },
  '@kit.PerformanceAnalysisKit': { hilog: { info() {}, warn() {} } },
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
const handleTitleBarPress = mod.exports.handleStageTitleBarPress;
const rect = { left: 0, top: 0, width: 800, height: 600 };

class NativeWindow {
  constructor(name, options, behavior) {
    this.name = name;
    this.options = options;
    this.behavior = behavior;
    this.listeners = new Map();
    this.visible = false;
    this.focused = false;
    this.modal = !!options.isModal;
    this.decorated = options.decorEnabled;
    this.limits = { minWidth: 240, minHeight: 180, maxWidth: 1200, maxHeight: 900 };
    this.avoidanceEnabled = false;
    this.destroyCalls = 0;
  }
  on(type, callback) { this.listeners.set(type, callback); }
  getWindowProperties() {
    const drawableRect = this.decorated
      ? { ...rect, width: 790, height: 560 } : rect;
    return { id: 100 + Number(this.name.slice('AvaloniaChild'.length)), displayId: 0, windowRect: rect, drawableRect };
  }
  isFocused() { return this.focused; }
  getWindowLimits() { return this.limits; }
  async setWindowLimits(limits) {
    if (this.behavior.limitsFail) throw new Error('limits unavailable');
    this.limits = Object.assign({}, limits);
    return this.limits;
  }
  getWindowDecorVisible() { return this.decorated; }
  setWindowDecorVisible(value) { this.decorated = value; }
  async setResizeByDragEnabled(value) { this.decoratedResize = value; }
  async enableDrag(value) { this.undecoratedResize = value; }
  async setSystemAvoidAreaEnabled(value) {
    if (this.behavior.avoidanceFails) throw new Error('avoidance unavailable');
    this.avoidanceEnabled = value;
  }
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
const main = {
  visible: true,
  focused: true,
  desktop: false,
  status: statuses.FLOATING,
  decorated: true,
  decorButtonStyle: { spacingBetweenButtons: 20 },
  titleButtonRect: { right: 0, top: 4, width: 140, height: 28 },
  moveCount: 0,
  listeners: new Map(),
  getWindowProperties: () => ({ id: 70, windowRect: rect, drawableRect: { ...rect, width: 790, height: 560 } }),
  on(type, callback) { this.listeners.set(type, callback); },
  off(type, callback) { assert.equal(this.listeners.get(type), callback); this.listeners.delete(type); },
  isInFreeWindowMode() { return this.desktop; },
  getWindowStatus() { return this.status; },
  reportStatus(status) {
    this.status = status;
    this.listeners.get('windowStatusDidChange')?.(status);
  },
  async resizeAsync(width, height) { this.resize = [width, height]; },
  async setResizeByDragEnabled(enabled) { this.dragResize = enabled; },
  setWindowTitleButtonVisible(canMaximize, canMinimize) { this.captionButtons = [canMaximize, canMinimize]; },
  getWindowDecorHeight() {
    assert.equal(this.decorated, true);
    return 37;
  },
  getWindowDecorVisible() { return this.decorated; },
  setWindowDecorVisible(visible) { this.decorated = visible; },
  getTitleButtonRect() { return this.titleButtonRect; },
  getDecorButtonStyle() { return { ...this.decorButtonStyle }; },
  setDecorButtonStyle(style) { this.decorButtonStyle = style; },
  async startMoving() { this.moveCount++; },
  async maximize(presentation) {
    this.presentation = presentation;
    this.reportStatus(presentation === presentations.EXIT_IMMERSIVE ? statuses.MAXIMIZE : statuses.FULL_SCREEN);
  },
  async recover() { this.reportStatus(statuses.FLOATING); },
  async minimize() { this.reportStatus(statuses.MINIMIZE); },
  async restore() { if (this.canRestore) this.reportStatus(statuses.FLOATING); },
  isFocused() { return this.focused; },
  isWindowShowing() { return this.visible; }
};
const windows = new ChildWindows(stage, main, false);
const send = (id, operation, modal = false, options = {}) =>
  windows.execute(JSON.stringify({ id, operation, activate: true, modal, decorated: false, ...options }));
const has = (key, event) => events.some(item => item[0] === key && item[1] === event);

(async () => {
  assert.ok(has(0, 5));
  const setStageState = state => send(0, 'setState', false, { state });
  assert.equal(await setStageState('maximized'), null);
  assert.ok(!has(0, 7));
  main.desktop = true;
  rejectStageEvent = 7;
  main.listeners.get('freeWindowModeChange')(main.desktop);
  assert.ok(!has(0, 7));
  main.listeners.get('windowEvent')(nativeTypes.WINDOW_SHOWN);
  assert.ok(has(0, 7));
  assert.ok(has(0, 9));
  assert.equal(await setStageState('maximized'), '');
  assert.equal(main.presentation, presentations.EXIT_IMMERSIVE);
  assert.deepEqual(events.at(-1), [0, 11]);
  assert.equal(events.filter(([key, event]) => key === 0 && event === 11).length, 1);
  assert.equal(await send(0, 'resize', false, { width: 700, height: 350 }), null);
  assert.equal(await setStageState('fullscreen'), '');
  assert.equal(main.presentation, presentations.ENTER_IMMERSIVE_DISABLE_TITLE_AND_DOCK_HOVER);
  assert.deepEqual(events.at(-1), [0, 12]);
  assert.equal(await setStageState('normal'), '');
  assert.equal(await send(0, 'resize', false, { width: 710, height: 390 }), '');
  assert.deepEqual(main.resize, [710, 390]);
  assert.equal(await send(0, 'setResizable', false, { resizable: false }), '');
  assert.equal(main.dragResize, false);
  assert.equal(await send(0, 'setCaptionButtons', false, { canMaximize: false, canMinimize: true }), '');
  assert.deepEqual(main.captionButtons, [false, true]);
  assert.equal(await setStageState('minimized'), '');
  assert.deepEqual(events.at(-1), [0, 10]);
  assert.equal(await setStageState('normal'), null);
  assert.deepEqual(events.at(-1), [0, 10]);
  main.canRestore = true;
  assert.equal(await setStageState('normal'), '');
  assert.deepEqual(events.at(-1), [0, 9]);
  rejectStageEvent = 11;
  main.reportStatus(statuses.MAXIMIZE);
  assert.deepEqual(events.at(-1), [0, 9]);
  main.reportStatus(statuses.MAXIMIZE);
  assert.deepEqual(events.at(-1), [0, 11]);
  main.reportStatus(statuses.FLOATING);
  assert.deepEqual(events.at(-1), [0, 9]);

  const extendTitleBar = (enabled, height = -1, theme) =>
    send(0, 'setExtendedTitleBar', false, { extendTitleBar: enabled, titleBarHeight: height, titleBarTheme: theme });
  assert.equal(await extendTitleBar(true), null);
  assert.equal(main.decorated, true);
  supportsTitleBar = true;
  assert.equal(await extendTitleBar(true), '');
  assert.equal(main.decorated, false);
  assert.deepEqual(titleBarLayouts.at(-1), [true, 37, 140]);
  assert.equal(await extendTitleBar(true, 46), '');
  assert.deepEqual(titleBarLayouts.at(-1), [true, 46, 140]);
  main.titleButtonRect = { right: 0, top: 4, width: 92, height: 28 };
  main.listeners.get('windowTitleButtonRectChange')(main.titleButtonRect);
  assert.deepEqual(titleBarLayouts.at(-1), [true, 46, 92]);
  assert.equal(handleTitleBarPress('1', false, -2, 10, 12, 0, 2), 1);
  assert.deepEqual(titleBarPresses.at(-1), ['1', false, -2, 10, 12, 0]);
  assert.equal(main.moveCount, 0);
  titleBarPressRoute = 2;
  assert.equal(handleTitleBarPress('1', true, 0, 10, 92, 16, 2), 0);
  assert.equal(handleTitleBarPress('1', true, 0, 10, 12, 16, 2), 2);
  assert.equal(main.moveCount, 1);
  assert.equal(await extendTitleBar(true, 46, 'dark'), '');
  assert.deepEqual(main.decorButtonStyle, { spacingBetweenButtons: 20, colorMode: colors.COLOR_MODE_DARK });
  assert.equal(await extendTitleBar(true, 46, 'light'), '');
  assert.equal(main.decorButtonStyle.colorMode, colors.COLOR_MODE_LIGHT);
  main.reportStatus(statuses.MAXIMIZE);
  assert.equal(handleTitleBarPress('1', true, 0, 10, 12, 16, 2), 0);
  main.reportStatus(statuses.FLOATING);
  assert.equal(await extendTitleBar(false), '');
  assert.equal(main.decorated, true);
  assert.equal(main.decorButtonStyle.colorMode, colors.COLOR_MODE_NOT_SET);
  assert.equal(main.listeners.has('windowTitleButtonRectChange'), false);
  assert.deepEqual(titleBarLayouts.at(-1), [false, 0, 0]);
  calls.push('Stage reports title height and native button geometry, starts only requested floating drags, and restores native decorations');

  main.desktop = false;
  main.listeners.get('freeWindowModeChange')(main.desktop);
  assert.deepEqual(events.at(-1), [0, 8]);
  assert.equal(await setStageState('maximized'), null);
  calls.push('Stage routes native desktop states, floating frame resize and caption policy, and retries rejected notifications');

  next = { destroyFails: 2 };
  assert.equal(await send(1, 'show', true), '');
  const first = native.get('AvaloniaChild1');
  assert.equal(first.avoidanceEnabled, true);
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

  next = { hideFails: true, destroyFails: 1, avoidanceFails: true };
  assert.equal(await send(3, 'show', true), '');
  assert.equal(native.get('AvaloniaChild3').avoidanceEnabled, false);
  assert.equal(await send(3, 'close'), null);
  assert.ok(has(3, 6));
  assert.ok(!has(3, 1));
  assert.ok(!has(3, 4));
  assert.equal(await send(3, 'close'), '');
  calls.push('A released modal lease is distinct from a failed hide');

  next = { destroyFails: 1, absentOnRejection: true };
  assert.equal(await send(4, 'show'), '');
  assert.equal(native.get('AvaloniaChild4').avoidanceEnabled, false);
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

  next = { destroyFails: 0 };
  const limits = { minWidth: 300, maxHeight: 450 };
  assert.equal(await send(7, 'show', false, { decorated: true, resizable: false, limits }), '');
  const bounded = native.get('AvaloniaChild7');
  assert.deepEqual(bounded.limits, { minWidth: 310, minHeight: 180, maxWidth: 1200, maxHeight: 490 });
  assert.equal(bounded.decoratedResize, false);
  assert.equal(await send(7, 'setDecorations', false, { limits, resizable: false }), '');
  assert.equal(bounded.decorated, false);
  assert.equal(bounded.undecoratedResize, false);
  assert.deepEqual(bounded.limits, { minWidth: 300, minHeight: 180, maxWidth: 1200, maxHeight: 450 });
  assert.equal(await send(7, 'setLimits', false, { limits: {} }), '');
  assert.deepEqual(bounded.limits, { minWidth: 240, minHeight: 180, maxWidth: 1200, maxHeight: 900 });
  assert.equal(await send(7, 'setResizable', false, { resizable: true }), '');
  assert.equal(bounded.undecoratedResize, true);
  assert.equal(await send(7, 'setDecorations', false, { decorated: true, resizable: true }), '');
  assert.equal(bounded.decoratedResize, true);
  assert.equal(await send(7, 'close'), '');
  calls.push('Child size limits account for decorations and reset to the native defaults');

  next = { destroyFails: 0, limitsFail: true };
  assert.equal(await send(8, 'show', false, { limits }), '');
  assert.equal(native.get('AvaloniaChild8').visible, true);
  assert.equal(await send(8, 'close'), '');
  calls.push('Unsupported initial limits leave a child open');

  assert.equal(await send(0, 'activate'), '');
  assert.equal(await send(0, 'close'), null);
  main.focused = false;
  assert.equal(await send(0, 'activate'), null);
  next = { destroyFails: 0 };
  assert.equal(await send(9, 'show'), '');
  native.get('AvaloniaChild9').focused = true;
  main.visible = false;
  assert.equal(await send(0, 'activate'), null);
  main.visible = true;
  assert.equal(await send(0, 'activate'), '');
  assert.deepEqual(focusTransfers, [[109, 70]]);
  await send(9, 'close');
  main.focused = false;
  assert.equal(await send(10, 'show', true), '');
  native.get('AvaloniaChild10').focused = true;
  assert.equal(await send(0, 'activate'), null);
  assert.equal(focusTransfers.length, 1);
  await send(10, 'close');
  calls.push('Stage activation only transfers focus from a known nonmodal window of this app');

  let finish;
  suspendCreate = new Promise(resolve => { finish = resolve; });
  const showing = send(6, 'show');
  windows.stop();
  assert.equal(main.listeners.size, 0);
  finish();
  assert.equal(await showing, null);
  assert.equal(native.has('AvaloniaChild6'), false);
  calls.push('Stage shutdown closes a child created by an in-flight show');

  for (const line of calls) console.log(line);
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
