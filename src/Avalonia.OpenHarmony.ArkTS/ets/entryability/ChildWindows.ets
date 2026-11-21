import { window } from '@kit.ArkUI';
import { BusinessError } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

enum ChildEvent {
  Shown,
  Hidden,
  Focused,
  Unfocused,
  Destroyed
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
}

export const childWindowPage: string = 'pages/AvaloniaChildWindow';

export class ChildWindows {
  private stage: window.WindowStage;
  private main: window.Window;
  private enableAccessibility: boolean;
  private windows: Map<number, window.Window> = new Map();
  private active: boolean = true;

  constructor(stage: window.WindowStage, main: window.Window, enableAccessibility: boolean) {
    this.stage = stage;
    this.main = main;
    this.enableAccessibility = enableAccessibility;
  }

  stop(): void {
    this.active = false;
    this.windows.clear();
    bridge.reportChildEvent(0, ChildEvent.Destroyed);
  }

  async execute(argument: string): Promise<string | null> {
    if (!this.active) {
      return null;
    }
    const request = JSON.parse(argument) as WindowRequest;
    if (!Number.isInteger(request.id) || request.id <= 0) {
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
      case 'close':
        await child.destroyWindow();
        this.closed(request.id, child);
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
      default:
        return null;
    }
    return '';
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
    const name = 'AvaloniaChild' + key;
    const options: window.SubWindowOptions = {
      title: request.title || 'Avalonia child window',
      windowRect: rect,
      decorEnabled: request.decorated
    };
    if (request.modal) {
      options.isModal = true;
      options.modalityType = window.ModalityType.WINDOW_MODALITY;
    }

    let child: window.Window | undefined;
    try {
      child = await this.createWindow(name, options);
      if (!this.active) {
        await child.destroyWindow();
        return false;
      }

      this.windows.set(key, child);
      this.observe(key, child);
      this.publishFrame(key, child);
      const storage = new LocalStorage();
      storage.setOrCreate<number>('avaloniaChildWindowKey', key);
      storage.setOrCreate<boolean>('avaloniaChildAccessibility', this.enableAccessibility);
      await child.loadContent(childWindowPage, storage);
      if (!this.active || this.windows.get(key) !== child) {
        return false;
      }
      await this.showWindow(child, request.activate);
      this.publishFrame(key, child);
      this.publishEvent(key, child, ChildEvent.Shown);
      return true;
    } catch (error) {
      const failure = error as Error;
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Child window %{public}d show failed: %{public}s', key, failure.message);
      if (child !== undefined) {
        try {
          await child.destroyWindow();
        } catch (_) {
          // The stage or the window may already have been destroyed.
        }
        this.closed(key, child);
      }
      return false;
    }
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
      bridge.reportChildEvent(key, ChildEvent.Destroyed);
    }
  }
}
