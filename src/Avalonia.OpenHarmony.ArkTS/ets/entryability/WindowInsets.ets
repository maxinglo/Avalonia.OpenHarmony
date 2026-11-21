import { window } from '@kit.ArkUI';
import { deviceInfo } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

function warn(error: Error): void {
  hilog.warn(0x0202, 'AvaloniaOHOS', 'Window insets: %{public}s', error.message);
}

function publishArea(kind: window.AvoidAreaType, area: window.AvoidArea): void {
  if (kind < window.AvoidAreaType.TYPE_SYSTEM || kind > window.AvoidAreaType.TYPE_FLOAT_NAVIGATION ||
    kind === window.AvoidAreaType.TYPE_SYSTEM_GESTURE) {
    return;
  }
  const rects = new Int32Array([
    area.leftRect.left, area.leftRect.top, area.leftRect.width, area.leftRect.height,
    area.topRect.left, area.topRect.top, area.topRect.width, area.topRect.height,
    area.rightRect.left, area.rightRect.top, area.rightRect.width, area.rightRect.height,
    area.bottomRect.left, area.bottomRect.top, area.bottomRect.width, area.bottomRect.height
  ]);
  // HarmonyOS defines the rectangles as the authoritative avoidance geometry.
  bridge.setAvoidArea(kind, rects);
}

function statusBarColor(mainWindow: window.Window): string | undefined {
  try {
    return mainWindow.getWindowSystemBarProperties().statusBarColor;
  } catch (_) {
    return undefined;
  }
}

function publishWindowProperties(mainWindow: window.Window): void {
  try {
    const edgeToEdge = mainWindow.getWindowProperties().isLayoutFullScreen;
    const color = statusBarColor(mainWindow);
    if (color !== undefined && /^#(?:[0-9a-f]{6}|[0-9a-f]{8})$/i.test(color)) {
      const argb = color.length === 7 ? 'FF' + color.substring(1) : color.substring(1);
      bridge.setWindowInsetProperties(edgeToEdge, true, parseInt(argb, 16));
    } else {
      bridge.setWindowInsetProperties(edgeToEdge, false, 0);
    }
  } catch (error) {
    warn(error as Error);
  }
}

export class WindowInsetsObserver {
  private mainWindow: window.Window;
  private listening: boolean = false;
  private avoidAreaChange = (update: window.AvoidAreaOptions): void => {
    if (!this.listening) {
      return;
    }
    publishArea(update.type, update.area);
    if (update.type === window.AvoidAreaType.TYPE_SYSTEM) {
      publishWindowProperties(this.mainWindow);
    }
  };

  constructor(mainWindow: window.Window) {
    this.mainWindow = mainWindow;
  }

  start(): void {
    try {
      this.mainWindow.on('avoidAreaChange', this.avoidAreaChange);
      this.listening = true;
    } catch (error) {
      warn(error as Error);
    }

    // Get initial data once. Resize callbacks can precede the corresponding system area update.
    const initialTypes: window.AvoidAreaType[] = [
      window.AvoidAreaType.TYPE_SYSTEM,
      window.AvoidAreaType.TYPE_CUTOUT,
      window.AvoidAreaType.TYPE_KEYBOARD,
      window.AvoidAreaType.TYPE_NAVIGATION_INDICATOR,
      window.AvoidAreaType.TYPE_FLOAT_NAVIGATION
    ];
    for (const kind of initialTypes) {
      try {
        publishArea(kind, this.mainWindow.getWindowAvoidArea(kind));
      } catch (error) {
        warn(error as Error);
      }
    }
    publishWindowProperties(this.mainWindow);
  }

  stop(): void {
    if (this.listening) {
      this.listening = false;
      try {
        this.mainWindow.off('avoidAreaChange', this.avoidAreaChange);
      } catch (error) {
        warn(error as Error);
      }
    }
    bridge.clearWindowInsets();
  }
}

export class WindowInsetsRequests {
  private mainWindow: window.Window;
  private initialStatusBarColor: string | undefined;
  private active: boolean = true;

  constructor(mainWindow: window.Window) {
    this.mainWindow = mainWindow;
    this.initialStatusBarColor = statusBarColor(mainWindow);
  }

  stop(): void {
    this.active = false;
  }

  async execute(operation: string, argument: string): Promise<string | null> {
    if (!this.active) {
      return null;
    }
    switch (operation) {
      case 'window.edgeToEdge':
        if (argument !== 'true' && argument !== 'false') {
          return null;
        }
        await this.mainWindow.setWindowLayoutFullScreen(argument === 'true');
        break;
      case 'window.systemBars':
        if (deviceInfo.deviceType === '2in1') {
          return null;
        }
        if (argument !== 'true' && argument !== 'false' && argument !== 'default') {
          return null;
        }
        await this.mainWindow.setWindowSystemBarEnable(argument === 'false' ? [] : ['status', 'navigation']);
        break;
      case 'window.statusBarColor':
        if (deviceInfo.deviceType === '2in1') {
          return null;
        }
        const color = argument === 'default' ? this.initialStatusBarColor
          : /^[0-9a-f]{8}$/i.test(argument) ? '#' + argument : undefined;
        if (color === undefined) {
          return null;
        }
        await this.mainWindow.setWindowSystemBarProperties({ statusBarColor: color });
        break;
      default:
        return null;
    }
    if (this.active) {
      publishWindowProperties(this.mainWindow);
      return '';
    }
    return null;
  }
}
