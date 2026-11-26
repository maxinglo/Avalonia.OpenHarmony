import { Configuration, ConfigurationConstant } from '@kit.AbilityKit';
import { display, window } from '@kit.ArkUI';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';
import { WindowInsetsObserver } from './WindowInsets';

export enum HostLifecycle {
  WindowHidden = 0,
  WindowShown = 1,
  WindowInactive = 2,
  WindowActive = 3,
  ApplicationBackground = 4,
  ApplicationForeground = 5
}

export function updateTheme(config: Configuration): void {
  if (config.colorMode === ConfigurationConstant.ColorMode.COLOR_MODE_DARK) {
    bridge.setTheme(true);
  } else if (config.colorMode === ConfigurationConstant.ColorMode.COLOR_MODE_LIGHT) {
    bridge.setTheme(false);
  }
}

export class PlatformWindow {
  private stage: window.WindowStage;
  private mainWindow: window.Window;
  private insets: WindowInsetsObserver;
  private hostDisplay: display.Display | undefined = undefined;
  private useWindowVisibility: boolean = false;

  private stageEvent = (state: window.WindowStageLifecycleEventType): void => {
    if (state === window.WindowStageLifecycleEventType.SHOWN) {
      bridge.setLifecycle(HostLifecycle.WindowShown);
      this.refreshDisplays();
    } else if (state === window.WindowStageLifecycleEventType.HIDDEN) {
      bridge.setLifecycle(HostLifecycle.WindowHidden);
    }
  };

  private windowEvent = (state: window.WindowEventType): void => {
    if (state === window.WindowEventType.WINDOW_ACTIVE) {
      bridge.setLifecycle(HostLifecycle.WindowActive);
    } else if (state === window.WindowEventType.WINDOW_INACTIVE) {
      bridge.setLifecycle(HostLifecycle.WindowInactive);
    } else if (this.useWindowVisibility && state === window.WindowEventType.WINDOW_SHOWN) {
      bridge.setLifecycle(HostLifecycle.WindowShown);
      this.refreshDisplays();
    } else if (this.useWindowVisibility && state === window.WindowEventType.WINDOW_HIDDEN) {
      bridge.setLifecycle(HostLifecycle.WindowHidden);
    }
  };

  private windowMoved = (_change: window.RectChangeOptions): void => {
    this.reportWindowPosition();
  };

  private displayChanged = (_displayId: number): void => {
    this.refreshDisplays();
  };

  private availableAreaChanged = (_bounds: display.Rect): void => {
    bridge.refreshDisplays();
  };

  constructor(stage: window.WindowStage, mainWindow: window.Window) {
    this.stage = stage;
    this.mainWindow = mainWindow;
    this.insets = new WindowInsetsObserver(mainWindow);
  }

  start(): void {
    this.observe(() => {
      if (!bridge.startDisplayObservation()) {
        hilog.warn(0x0202, 'AvaloniaOHOS', 'Global display available-area observation is unavailable');
      }
    });
    this.useWindowVisibility = !this.observe(() => this.stage.on('windowStageLifecycleEvent', this.stageEvent));
    this.observe(() => this.mainWindow.on('windowEvent', this.windowEvent));
    this.observe(() => this.mainWindow.on('windowRectChange', this.windowMoved));
    this.observe(() => this.mainWindow.on('displayIdChange', this.displayChanged));
    this.observe(() => display.on('add', this.displayChanged));
    this.observe(() => display.on('remove', this.displayChanged));
    this.observe(() => display.on('change', this.displayChanged));
    this.observe(() => bridge.setLifecycle(this.mainWindow.isWindowShowing() ?
      HostLifecycle.WindowShown : HostLifecycle.WindowHidden));
    this.observe(() => bridge.setLifecycle(this.mainWindow.isFocused() ?
      HostLifecycle.WindowActive : HostLifecycle.WindowInactive));
    this.refreshDisplays();
    this.insets.start();
  }

  stop(): void {
    this.insets.stop();
    bridge.stopDisplayObservation();
    bridge.setLifecycle(HostLifecycle.WindowHidden);
    if (!this.useWindowVisibility) {
      this.observe(() => this.stage.off('windowStageLifecycleEvent', this.stageEvent));
    }
    this.observe(() => this.mainWindow.off('windowEvent', this.windowEvent));
    this.observe(() => this.mainWindow.off('windowRectChange', this.windowMoved));
    this.observe(() => this.mainWindow.off('displayIdChange', this.displayChanged));
    this.observe(() => display.off('add', this.displayChanged));
    this.observe(() => display.off('remove', this.displayChanged));
    this.observe(() => display.off('change', this.displayChanged));
    this.unobserveAvailableArea();
  }

  private refreshDisplays(): void {
    this.reportWindowPosition();
    bridge.refreshDisplays();
    this.observe(() => {
      const id = this.mainWindow.getWindowProperties().displayId ?? display.getDefaultDisplaySync().id;
      if (this.hostDisplay?.id !== id) {
        this.unobserveAvailableArea();
        const next = display.getDisplayByIdSync(id);
        next.on('availableAreaChange', this.availableAreaChanged);
        this.hostDisplay = next;
      }
    });
  }

  private reportWindowPosition(): void {
    this.observe(() => {
      const properties = this.mainWindow.getWindowProperties();
      const id = properties.displayId ?? display.getDefaultDisplaySync().id;
      bridge.setWindowPosition(id, properties.windowRect.left, properties.windowRect.top);
    });
  }

  private unobserveAvailableArea(): void {
    const old = this.hostDisplay;
    this.hostDisplay = undefined;
    if (old !== undefined) {
      this.observe(() => old.off('availableAreaChange', this.availableAreaChanged));
    }
  }

  private observe(action: () => void): boolean {
    try {
      action();
      return true;
    } catch (error) {
      // Some devices only expose a subset of the display and window notifications.
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Window observation: %{public}s', (error as Error).message);
      return false;
    }
  }
}
