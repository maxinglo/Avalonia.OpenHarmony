import { window } from '@kit.ArkUI';
import { BusinessError } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

enum ChildEvent {
  Shown,
  Hidden,
  Focused,
  Unfocused,
  Destroyed,
  StageCreated,
  ModalityReleased
}

interface WindowRequest {
  id: number;
  operation: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  title?: string;
  modal: boolean;
  activate: boolean;
  decorated: boolean;
  resizable?: boolean;
  limits?: window.WindowLimits;
}

export const childWindowPage: string = 'pages/AvaloniaChildWindow';

export function childWindowName(key: number): string {
  return 'AvaloniaChild' + key;
}

export function reportStageDestroyed(): void {
  bridge.reportChildEvent(0, ChildEvent.Destroyed);
}

export class ChildWindows {
  private stage: window.WindowStage;
  private main: window.Window;
  private enableAccessibility: boolean;
  private enableInboundDrop: boolean;
  private enableTextDragSource: boolean;
  private windows: Map<number, window.Window> = new Map();
  private defaultLimits: Map<number, window.WindowLimits> = new Map();
  private closing: Map<number, Promise<boolean>> = new Map();
  private modalWindows: Set<number> = new Set();
  private active: boolean = true;

  constructor(stage: window.WindowStage, main: window.Window, enableAccessibility: boolean,
    enableInboundDrop: boolean = false, enableTextDragSource: boolean = false) {
    this.stage = stage;
    this.main = main;
    this.enableAccessibility = enableAccessibility;
    this.enableInboundDrop = enableInboundDrop;
    this.enableTextDragSource = enableTextDragSource;
    bridge.reportChildEvent(0, ChildEvent.StageCreated);
  }

  stop(): void {
    this.active = false;
    for (const key of this.windows.keys()) {
      this.close(key);
    }
  }

  async execute(argument: string): Promise<string | null> {
    const request = JSON.parse(argument) as WindowRequest;
    if (!Number.isInteger(request.id) || request.id < 0) {
      return null;
    }
    if (request.id === 0) {
      return request.operation === 'activate' && this.active && await this.activateStage() ? '' : null;
    }

    if (request.operation === 'close') {
      return await this.close(request.id) ? '' : null;
    }
    if (!this.active || this.closing.has(request.id)) {
      return null;
    }
    const child = this.windows.get(request.id);
    if (request.operation === 'show') {
      return await this.show(request, child) ? '' : null;
    }
    if (child === undefined) {
      return null;
    }
    switch (request.operation) {
      case 'hide':
        await child.minimize();
        this.publishEvent(request.id, child, ChildEvent.Hidden);
        break;
      case 'activate':
        await child.raiseToAppTop();
        break;
      case 'resize':
        if (request.width === undefined || request.height === undefined) {
          return null;
        }
        await child.resizeAsync(request.width, request.height);
        this.publishFrame(request.id, child);
        break;
      case 'move':
        if (request.x === undefined || request.y === undefined) {
          return null;
        }
        await child.moveWindowToAsync(request.x, request.y);
        this.publishFrame(request.id, child);
        break;
      case 'setTitle':
        await child.setWindowTitle(request.title ?? '');
        break;
      case 'setDecorations':
        child.setWindowDecorVisible(request.decorated);
        try {
          if (request.resizable !== undefined) {
            await this.setResizable(child, request.resizable);
          }
        } finally {
          if (request.limits !== undefined) {
            await this.setLimits(request.id, child, request.limits);
          }
          this.publishFrame(request.id, child);
        }
        break;
      case 'setResizable':
        if (request.resizable === undefined) {
          return null;
        }
        await this.setResizable(child, request.resizable);
        break;
      case 'setLimits':
        if (request.limits === undefined) {
          return null;
        }
        await this.setLimits(request.id, child, request.limits);
        break;
      default:
        return null;
    }
    return '';
  }

  private async activateStage(): Promise<boolean> {
    if (this.main.isFocused()) {
      return true;
    }
    if (!this.main.isWindowShowing() || this.modalWindows.size > 0) {
      return false;
    }
    for (const key of this.windows.keys()) {
      const child = this.windows.get(key);
      if (child !== undefined && !this.closing.has(key) && child.isFocused()) {
        await window.shiftAppWindowFocus(child.getWindowProperties().id, this.main.getWindowProperties().id);
        return true;
      }
    }
    return false;
  }

  private async show(request: WindowRequest, existing?: window.Window): Promise<boolean> {
    const key = request.id;
    if (existing !== undefined) {
      await this.showWindow(existing, request.activate);
      this.publishEvent(key, existing, ChildEvent.Shown);
      return true;
    }

    const main = this.main.getWindowProperties().windowRect;
    const width = request.width ?? Math.round(main.width * 0.65);
    const height = request.height ?? Math.round(main.height * 0.55);
    const rect: window.Rect = {
      left: request.x ?? main.left + Math.round((main.width - width) / 2),
      top: request.y ?? main.top + Math.round((main.height - height) / 2),
      width,
      height
    };
    const name = childWindowName(key);
    const options: window.SubWindowOptions = {
      title: request.title || 'Avalonia child window',
      windowRect: rect,
      decorEnabled: request.decorated
    };
    if (request.modal) {
      options.isModal = true;
      options.modalityType = window.ModalityType.WINDOW_MODALITY;
      this.modalWindows.add(key);
    }

    let child: window.Window | undefined;
    try {
      child = await this.createWindow(name, options);
      this.windows.set(key, child);
      this.observe(key, child);
      if (!this.active) {
        await this.close(key);
        return false;
      }

      if (request.modal) {
        try {
          await child.setSystemAvoidAreaEnabled(true);
        } catch (error) {
          this.warnClose(key, 'enable system avoidance', error as Error);
        }
        if (!this.active || this.windows.get(key) !== child) {
          await this.close(key);
          return false;
        }
      }

      this.publishFrame(key, child);
      const storage = new LocalStorage();
      storage.setOrCreate<number>('avaloniaChildWindowKey', key);
      storage.setOrCreate<boolean>('avaloniaChildAccessibility', this.enableAccessibility);
      storage.setOrCreate<boolean>('avaloniaChildInboundDrop', this.enableInboundDrop);
      storage.setOrCreate<boolean>('avaloniaChildTextDragSource', this.enableTextDragSource);
      await child.loadContent(childWindowPage, storage);
      if (!this.active || this.windows.get(key) !== child) {
        await this.close(key);
        return false;
      }
      await this.showWindow(child, request.activate);
      if (!this.active || this.windows.get(key) !== child) {
        return false;
      }
      if (request.limits !== undefined) {
        try {
          await this.setLimits(key, child, request.limits);
        } catch (error) {
          this.warnClose(key, 'set size limits', error as Error);
        }
      }
      if (request.resizable === false) {
        try {
          await this.setResizable(child, false);
        } catch (error) {
          this.warnClose(key, 'disable drag resize', error as Error);
        }
      }
      this.publishFrame(key, child);
      this.publishEvent(key, child, ChildEvent.Shown);
      return true;
    } catch (error) {
      const failure = error as Error;
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Child window %{public}d show failed: %{public}s', key, failure.message);
      await this.close(key);
      return false;
    }
  }

  private close(key: number): Promise<boolean> {
    const current = this.closing.get(key);
    if (current !== undefined) {
      return current;
    }
    const pending = this.destroyChild(key).finally((): void => {
      this.closing.delete(key);
    });
    this.closing.set(key, pending);
    return pending;
  }

  private async destroyChild(key: number): Promise<boolean> {
    let child = this.windows.get(key);
    if (child === undefined) {
      try {
        child = window.findWindow(childWindowName(key));
        this.windows.set(key, child);
      } catch (error) {
        if ((error as BusinessError).code === 1300002) {
          this.modalWindows.delete(key);
          bridge.reportChildEvent(key, ChildEvent.Destroyed);
          return true;
        }
        this.warnClose(key, 'window lookup', error as Error);
        return false;
      }
    }

    try {
      await child.destroyWindow();
    } catch (error) {
      if (this.windows.get(key) !== child || this.isWindowMissing(key)) {
        this.closed(key, child);
        return true;
      }
      this.warnClose(key, 'destroy', error as Error);
      if (this.modalWindows.has(key)) {
        try {
          await child.setSubWindowModal(false);
          this.modalWindows.delete(key);
          this.publishEvent(key, child, ChildEvent.ModalityReleased);
        } catch (modalError) {
          this.warnClose(key, 'release modality', modalError as Error);
        }
      }
      try {
        await child.minimize();
        if (!child.isWindowShowing()) {
          this.publishEvent(key, child, ChildEvent.Hidden);
        }
      } catch (hideError) {
        this.warnClose(key, 'hide', hideError as Error);
      }
      return this.windows.get(key) !== child;
    }
    this.closed(key, child);
    return true;
  }

  private isWindowMissing(key: number): boolean {
    try {
      window.findWindow(childWindowName(key));
      return false;
    } catch (error) {
      return (error as BusinessError).code === 1300002;
    }
  }

  private warnClose(key: number, operation: string, error: Error): void {
    hilog.warn(0x0202, 'AvaloniaOHOS', 'Child window %{public}d %{public}s failed: %{public}s',
      key, operation, error.message);
  }

  private async setResizable(child: window.Window, resizable: boolean): Promise<void> {
    if (child.getWindowDecorVisible()) {
      await child.setResizeByDragEnabled(resizable);
    } else {
      await child.enableDrag(resizable);
    }
  }

  private async setLimits(key: number, child: window.Window, limits: window.WindowLimits): Promise<void> {
    let defaults = this.defaultLimits.get(key);
    if (defaults === undefined) {
      defaults = child.getWindowLimits();
      this.defaultLimits.set(key, defaults);
    }
    const properties = child.getWindowProperties();
    const frame = properties.windowRect;
    const drawable = properties.drawableRect;
    const horizontal = drawable.width > 0 ? Math.max(0, frame.width - drawable.width) : 0;
    const vertical = drawable.height > 0 ? Math.max(0, frame.height - drawable.height) : 0;
    const applied = await child.setWindowLimits({
      minWidth: this.frameLimit(limits.minWidth, defaults.minWidth, horizontal),
      minHeight: this.frameLimit(limits.minHeight, defaults.minHeight, vertical),
      maxWidth: this.frameLimit(limits.maxWidth, defaults.maxWidth, horizontal),
      maxHeight: this.frameLimit(limits.maxHeight, defaults.maxHeight, vertical)
    });
    hilog.info(0x0202, 'AvaloniaOHOS', 'Child window %{public}d native size limits %{public}d x %{public}d to %{public}d x %{public}d',
      key, applied.minWidth ?? 0, applied.minHeight ?? 0, applied.maxWidth ?? 0, applied.maxHeight ?? 0);
  }

  private frameLimit(limit: number | undefined, defaultLimit: number | undefined, chrome: number): number | undefined {
    return limit === undefined ? defaultLimit : Math.min(limit + chrome, 2147483647);
  }

  private async createWindow(name: string, options: window.SubWindowOptions): Promise<window.Window> {
    try {
      return await this.stage.createSubWindowWithOptions(name, options);
    } catch (error) {
      const code = (error as BusinessError).code;
      if (code !== 401 && code !== 801) {
        throw new Error('createSubWindowWithOptions failed (' + code + ')');
      }
      const fallback: window.SubWindowOptions = { title: options.title, decorEnabled: options.decorEnabled };
      if (options.isModal) {
        fallback.isModal = true;
        fallback.modalityType = window.ModalityType.WINDOW_MODALITY;
      }
      try {
        return await this.stage.createSubWindowWithOptions(name, fallback);
      } catch (retry) {
        const failure = retry as BusinessError;
        if (options.isModal || (failure.code !== 401 && failure.code !== 801)) {
          throw new Error('createSubWindowWithOptions without a frame failed (' + failure.code + ')');
        }
        return await this.stage.createSubWindow(name);
      }
    }
  }

  private async showWindow(child: window.Window, activate: boolean): Promise<void> {
    if (activate) {
      await child.showWindow();
      return;
    }
    await child.setWindowFocusable(false);
    try {
      await child.showWindow();
    } finally {
      await child.setWindowFocusable(true);
    }
  }

  private observe(key: number, child: window.Window): void {
    child.on('windowEvent', (event: window.WindowEventType): void => {
      switch (event) {
        case window.WindowEventType.WINDOW_SHOWN:
          this.publishEvent(key, child, ChildEvent.Shown);
          break;
        case window.WindowEventType.WINDOW_HIDDEN:
          this.publishEvent(key, child, ChildEvent.Hidden);
          break;
        case window.WindowEventType.WINDOW_ACTIVE:
          this.publishEvent(key, child, ChildEvent.Focused);
          break;
        case window.WindowEventType.WINDOW_INACTIVE:
          this.publishEvent(key, child, ChildEvent.Unfocused);
          break;
        case window.WindowEventType.WINDOW_DESTROYED:
          this.closed(key, child);
          break;
      }
    });
    child.on('windowSizeChange', (_size: window.Size): void => this.publishFrame(key, child));
    try {
      child.on('windowRectChange', (_change: window.RectChangeOptions): void => this.publishFrame(key, child));
      child.on('windowWillClose', (): Promise<boolean> => bridge.requestChildClose(key));
    } catch (error) {
      const failure = error as BusinessError;
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Child window %{public}d desktop events unavailable (%{public}d)',
        key, failure.code);
    }
  }

  private publishFrame(key: number, child: window.Window): void {
    if (this.windows.get(key) !== child) {
      return;
    }
    const props = child.getWindowProperties();
    const rect = props.windowRect;
    if (rect.width > 0 && rect.height > 0) {
      bridge.reportChildFrame(key, props.id, props.displayId ?? 0, rect.left, rect.top, rect.width, rect.height);
    }
  }

  private publishEvent(key: number, child: window.Window, event: ChildEvent): void {
    if (this.windows.get(key) === child) {
      bridge.reportChildEvent(key, event);
    }
  }

  private closed(key: number, child: window.Window): void {
    if (this.windows.get(key) === child) {
      this.windows.delete(key);
      this.defaultLimits.delete(key);
      this.modalWindows.delete(key);
      bridge.reportChildEvent(key, ChildEvent.Destroyed);
    }
  }
}
