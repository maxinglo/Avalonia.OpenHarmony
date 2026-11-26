import { ConfigurationConstant } from '@kit.AbilityKit';
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
  ModalityReleased,
  StageDesktopAvailable,
  StageDesktopUnavailable,
  StageNormal,
  StageMinimized,
  StageMaximized,
  StageFullScreen
}

interface WindowRequest {
  id: number;
  operation: string;
  ownerId?: number;
  independent?: boolean;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  resizeFromLeft?: boolean;
  resizeFromTop?: boolean;
  title?: string;
  modal: boolean;
  activate: boolean;
  decorated: boolean;
  resizable?: boolean;
  limits?: window.WindowLimits;
  state?: string;
  canMinimize?: boolean;
  canMaximize?: boolean;
  topmost?: boolean;
  extendTitleBar?: boolean;
  titleBarHeight?: number;
  titleBarTheme?: string;
}

let currentStage: ChildWindows | undefined;

export const childWindowPage: string = 'pages/AvaloniaChildWindow';

export function childWindowName(key: number): string {
  return 'AvaloniaChild' + key;
}

export function reportStageDestroyed(): void {
  bridge.reportChildEvent(0, ChildEvent.Destroyed);
}

export function handleStageTitleBarPress(surfaceId: string, mouse: boolean, contact: number, x: number, y: number,
  modifiers: number, scale: number): number {
  return currentStage?.titleBarPress(surfaceId, mouse, contact, x, y, modifiers, scale) ?? 0;
}

export class ChildWindows {
  private stage: window.WindowStage;
  private main: window.Window;
  private enableAccessibility: boolean;
  private enableInboundDrop: boolean;
  private enableTextDragSource: boolean;
  private windows: Map<number, window.Window> = new Map();
  private parents: Map<number, number> = new Map();
  private independentWindows: Set<number> = new Set();
  private minimizedChildren: Set<number> = new Set();
  private hiddenChildren: Set<number> = new Set();
  private defaultLimits: Map<number, window.WindowLimits> = new Map();
  private closing: Map<number, Promise<boolean>> = new Map();
  private modalWindows: Set<number> = new Set();
  private active: boolean = true;
  private desktop: boolean = false;
  private stageStatusDidChange: boolean = false;
  private stageState: ChildEvent | undefined = undefined;
  private stageDecorated: boolean = true;
  private stageCanMaximize: boolean = true;
  private stageCanMinimize: boolean = true;
  private extendedTitleBar: boolean = false;
  private titleBarHeightHint: number = -1;
  private nativeTitleBarHeight: number = 0;
  private titleBarHeight: number = 0;
  private titleBarButtonInset: number = 0;
  private observingTitleButtons: boolean = false;
  private originalButtonColor: ConfigurationConstant.ColorMode | undefined;
  private appliedButtonColor: ConfigurationConstant.ColorMode | undefined;

  constructor(stage: window.WindowStage, main: window.Window, enableAccessibility: boolean,
    enableInboundDrop: boolean = false, enableTextDragSource: boolean = false) {
    this.stage = stage;
    this.main = main;
    this.enableAccessibility = enableAccessibility;
    this.enableInboundDrop = enableInboundDrop;
    this.enableTextDragSource = enableTextDragSource;
    currentStage = this;
    bridge.reportChildEvent(0, ChildEvent.StageCreated);
    this.observeStage();
  }

  stop(): void {
    this.active = false;
    if (currentStage === this) {
      currentStage = undefined;
    }
    this.stopObservingTitleButtons();
    this.unobserveStage();
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
      return this.active && await this.executeStage(request) ? '' : null;
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
        this.hiddenChildren.add(request.id);
        try {
          await child.minimize();
        } catch (error) {
          this.hiddenChildren.delete(request.id);
          throw error as Error;
        }
        this.minimizedChildren.delete(request.id);
        this.publishEvent(request.id, child, ChildEvent.Hidden);
        break;
      case 'activate':
        if (this.independentWindows.has(request.id) || this.hasModalChild(request.id)) {
          return await this.activateWindow(request.id, child) ? '' : null;
        }
        await child.raiseToAppTop();
        break;
      case 'resize':
        if (request.width === undefined || request.height === undefined) {
          return null;
        }
        await child.resizeAsync(request.width, request.height);
        this.publishFrame(request.id, child);
        break;
      case 'resizeFromGrip':
        if (!await this.resizeFromGrip(request, child)) {
          return null;
        }
        this.publishFrame(request.id, child);
        break;
      case 'move':
        if (request.x === undefined || request.y === undefined) {
          return null;
        }
        if (this.desktop) {
          await child.moveWindowToGlobalDisplay(request.x, request.y);
        } else {
          await child.moveWindowToAsync(request.x, request.y);
        }
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
      case 'setState':
        return await this.setChildState(request.id, child, request.state) ? '' : null;
      case 'setCaptionButtons':
        if (!this.desktop || !this.independentWindows.has(request.id) || request.canMaximize === undefined ||
          request.canMinimize === undefined) {
          return null;
        }
        child.setWindowTitleButtonVisible(request.canMaximize, request.canMinimize);
        break;
      default:
        return null;
    }
    return '';
  }

  private async executeStage(request: WindowRequest): Promise<boolean> {
    if (request.operation === 'activate') {
      return this.activateWindow(0, this.main);
    }
    if (!this.desktop) {
      return false;
    }
    try {
      switch (request.operation) {
        case 'show':
          if (this.main.getWindowStatus() === window.WindowStatusType.MINIMIZE) {
            await this.main.restore();
          }
          this.stageStatusChanged(this.main.getWindowStatus());
          return true;
        case 'hide':
          return await this.setStageState('minimized');
        case 'setState':
          return await this.setStageState(request.state);
        case 'resize': {
          if (request.width === undefined || request.height === undefined ||
            this.main.getWindowStatus() !== window.WindowStatusType.FLOATING) {
            return false;
          }
          await this.main.resizeAsync(request.width, request.height);
          this.stageRectChanged();
          return true;
        }
        case 'resizeFromGrip':
          if (!await this.resizeFromGrip(request, this.main)) {
            return false;
          }
          this.stageRectChanged();
          return true;
        case 'setResizable':
          if (request.resizable === undefined) {
            return false;
          }
          await this.main.setResizeByDragEnabled(request.resizable);
          return true;
        case 'move':
          if (request.x === undefined || request.y === undefined ||
            this.main.getWindowStatus() !== window.WindowStatusType.FLOATING) {
            return false;
          }
          await this.main.moveWindowToGlobalDisplay(request.x, request.y);
          this.stageRectChanged();
          return true;
        case 'setTitle':
          await this.main.setWindowTitle(request.title ?? '');
          return true;
        case 'setTopmost':
          if (request.topmost === undefined) {
            return false;
          }
          await this.main.setWindowTopmost(request.topmost);
          return true;
        case 'setDecorations':
          this.stageDecorated = request.decorated;
          if (!this.extendedTitleBar) {
            if (!request.decorated && this.main.getWindowDecorVisible()) {
              this.nativeTitleBarHeight = this.main.getWindowDecorHeight();
            }
            this.main.setWindowDecorVisible(request.decorated);
          }
          this.applyStageCaptionButtons(this.extendedTitleBar);
          if (request.resizable !== undefined) {
            await this.main.setResizeByDragEnabled(request.resizable);
          }
          if (request.limits !== undefined) {
            await this.setLimits(0, this.main, request.limits);
          }
          this.stageRectChanged();
          return true;
        case 'setLimits':
          if (request.limits === undefined) {
            return false;
          }
          await this.setLimits(0, this.main, request.limits);
          return true;
        case 'setCaptionButtons':
          if (request.canMaximize === undefined || request.canMinimize === undefined) {
            return false;
          }
          this.stageCanMaximize = request.canMaximize;
          this.stageCanMinimize = request.canMinimize;
          this.applyStageCaptionButtons(this.extendedTitleBar);
          return true;
        case 'setExtendedTitleBar':
          return request.extendTitleBar !== undefined && request.titleBarHeight !== undefined &&
            Number.isFinite(request.titleBarHeight) &&
            this.setExtendedTitleBar(request.extendTitleBar, request.titleBarHeight, request.titleBarTheme);
        default:
          return false;
      }
    } catch (error) {
      this.warnStage(request.operation, error as BusinessError);
      return false;
    }
  }

  private async resizeFromGrip(request: WindowRequest, target: window.Window): Promise<boolean> {
    if (!this.desktop || request.width === undefined || request.height === undefined ||
      request.x === undefined || request.y === undefined || target.getWindowStatus() !== window.WindowStatusType.FLOATING) {
      return false;
    }
    await target.resizeAsync(request.width, request.height);
    if (request.resizeFromLeft || request.resizeFromTop) {
      const frame = target.getWindowProperties().windowRect;
      const x = request.resizeFromLeft ? request.x - frame.width : request.x;
      const y = request.resizeFromTop ? request.y - frame.height : request.y;
      await target.moveWindowToGlobalDisplay(x, y);
    }
    return true;
  }

  private applyStageCaptionButtons(extended: boolean): void {
    const show = extended || this.stageDecorated;
    this.main.setWindowTitleButtonVisible(show && this.stageCanMaximize, show && this.stageCanMinimize, show);
  }

  private setExtendedTitleBar(extended: boolean, heightHint: number, theme?: string): boolean {
    if (!bridge.supportsTitleBar()) {
      return false;
    }
    this.titleBarHeightHint = heightHint;
    if (this.extendedTitleBar !== extended) {
      if (extended && this.main.getWindowDecorVisible()) {
        this.nativeTitleBarHeight = this.main.getWindowDecorHeight();
      }
      const showDecor = this.stageDecorated && !extended;
      this.main.setWindowDecorVisible(showDecor);
      if (this.main.getWindowDecorVisible() !== showDecor) {
        return false;
      }
    }
    this.applyStageCaptionButtons(extended);
    if (extended) {
      if (!this.observingTitleButtons) {
        this.observingTitleButtons = this.observeStageAction(() =>
          this.main.on('windowTitleButtonRectChange', this.titleButtonsChanged));
      }
      try {
        this.updateTitleBarTheme(theme);
        this.updateTitleBar(this.main.getTitleButtonRect());
      } catch (error) {
        this.observeStageAction(() => this.main.setWindowDecorVisible(this.stageDecorated));
        this.observeStageAction(() => this.applyStageCaptionButtons(false));
        this.clearTitleBarLayout();
        this.warnStage('getTitleButtonRect', error as BusinessError);
        return false;
      }
    } else {
      this.clearTitleBarLayout();
    }
    return true;
  }

  private titleButtonsChanged = (buttons: window.TitleButtonRect): void => {
    if (this.extendedTitleBar) {
      this.observeStageAction(() => this.updateTitleBar(buttons));
    }
  };

  private updateTitleBar(buttons: window.TitleButtonRect): void {
    const requestedHeight = this.titleBarHeightHint >= 0 ? this.titleBarHeightHint : this.nativeTitleBarHeight;
    const height = Math.max(requestedHeight, buttons.top + buttons.height);
    const inset = Math.max(0, buttons.width);
    if (!this.extendedTitleBar || height !== this.titleBarHeight || inset !== this.titleBarButtonInset) {
      this.extendedTitleBar = true;
      this.titleBarHeight = height;
      this.titleBarButtonInset = inset;
      bridge.reportTitleBar(true, height, inset);
    }
  }

  private stopObservingTitleButtons(): void {
    if (this.observingTitleButtons) {
      this.observeStageAction(() => this.main.off('windowTitleButtonRectChange', this.titleButtonsChanged));
      this.observingTitleButtons = false;
    }
  }

  private updateTitleBarTheme(theme?: string): void {
    // Keep native caption buttons legible when the extended Avalonia title bar changes theme.
    const restore = theme !== 'dark' && theme !== 'light';
    const color = theme === 'dark' ? ConfigurationConstant.ColorMode.COLOR_MODE_DARK :
      theme === 'light' ? ConfigurationConstant.ColorMode.COLOR_MODE_LIGHT : this.originalButtonColor;
    if (color === undefined) {
      return;
    }
    try {
      if (color !== this.appliedButtonColor) {
        const style = this.main.getDecorButtonStyle();
        const previousColor = style.colorMode ?? ConfigurationConstant.ColorMode.COLOR_MODE_NOT_SET;
        style.colorMode = color;
        this.main.setDecorButtonStyle(style);
        this.originalButtonColor = this.originalButtonColor ?? previousColor;
      }
      this.appliedButtonColor = restore ? undefined : color;
      if (restore) {
        this.originalButtonColor = undefined;
      }
    } catch (error) {
      this.warnStage('setDecorButtonStyle', error as BusinessError);
    }
  }

  private clearTitleBarLayout(): void {
    this.stopObservingTitleButtons();
    this.updateTitleBarTheme();
    if (this.extendedTitleBar) {
      this.extendedTitleBar = false;
      this.titleBarHeight = 0;
      this.titleBarButtonInset = 0;
      bridge.reportTitleBar(false, 0, 0);
    }
  }

  titleBarPress(surfaceId: string, mouse: boolean, contact: number, x: number, y: number, modifiers: number,
    scale: number): number {
    if (!this.extendedTitleBar || y < 0 || y >= this.titleBarHeight * scale ||
      this.stageState !== ChildEvent.StageNormal) {
      return 0;
    }
    try {
      if (this.main.getWindowStatus() !== window.WindowStatusType.FLOATING) {
        return 0;
      }
    } catch (error) {
      this.warnStage('getWindowStatus', error as BusinessError);
      return 0;
    }
    const route = bridge.titleBarPress(surfaceId, mouse, contact, x, y, modifiers);
    if (route === 2) {
      try {
        this.main.startMoving().catch((error: BusinessError): void => this.warnStage('startMoving', error));
      } catch (error) {
        this.warnStage('startMoving', error as BusinessError);
      }
    }
    return route;
  }

  private async setStageState(state: string | undefined): Promise<boolean> {
    if (state !== 'normal' && state !== 'minimized' && state !== 'maximized' && state !== 'fullscreen') {
      return false;
    }
    try {
      let current = this.main.getWindowStatus();
      if (state === 'minimized') {
        if (current !== window.WindowStatusType.MINIMIZE) {
          await this.main.minimize();
        }
        return true;
      }
      if (current === window.WindowStatusType.MINIMIZE) {
        await this.main.restore();
        current = this.main.getWindowStatus();
        if (current === window.WindowStatusType.MINIMIZE) {
          return false;
        }
      }
      switch (state) {
        case 'normal':
          if (current !== window.WindowStatusType.FLOATING) {
            await this.main.recover();
          }
          return true;
        case 'maximized':
          if (current !== window.WindowStatusType.MAXIMIZE) {
            await this.main.maximize(window.MaximizePresentation.EXIT_IMMERSIVE);
          }
          return true;
        case 'fullscreen':
          if (current !== window.WindowStatusType.FULL_SCREEN) {
            await this.main.maximize(window.MaximizePresentation.ENTER_IMMERSIVE_DISABLE_TITLE_AND_DOCK_HOVER);
          }
          return true;
        default:
          return false;
      }
    } finally {
      this.observeStageAction(() => this.stageStatusChanged(this.main.getWindowStatus()));
    }
  }

  private async setChildState(key: number, child: window.Window, state: string | undefined): Promise<boolean> {
    if (!this.desktop || this.modalWindows.has(key) || this.hiddenChildren.has(key) ||
      (state !== 'normal' && state !== 'minimized' && state !== 'maximized' && state !== 'fullscreen')) {
      return false;
    }
    try {
      if (state === 'minimized') {
        if (!this.minimizedChildren.has(key)) {
          this.minimizedChildren.add(key);
          try {
            await child.minimize();
          } catch (error) {
            this.minimizedChildren.delete(key);
            throw error as Error;
          }
          this.publishEvent(key, child, ChildEvent.Hidden);
        }
        return true;
      }
      if (this.minimizedChildren.delete(key)) {
        try {
          await child.showWindow();
        } catch (error) {
          this.minimizedChildren.add(key);
          throw error as Error;
        }
        this.publishEvent(key, child, ChildEvent.Shown);
      }
      const current = child.getWindowStatus();
      if (state === 'normal' && current !== window.WindowStatusType.FLOATING) {
        await child.recover();
      } else if (state === 'maximized' && current !== window.WindowStatusType.MAXIMIZE) {
        await child.maximize(window.MaximizePresentation.EXIT_IMMERSIVE);
      } else if (state === 'fullscreen' && current !== window.WindowStatusType.FULL_SCREEN) {
        await child.maximize(window.MaximizePresentation.ENTER_IMMERSIVE_DISABLE_TITLE_AND_DOCK_HOVER);
      }
      return true;
    } finally {
      this.publishChildStatus(key, child);
    }
  }

  private stageStatusChanged = (state: window.WindowStatusType): void => {
    this.stageRectChanged();
    this.reportStageState(state);
    if (this.extendedTitleBar) {
      this.observeStageAction(() => this.updateTitleBar(this.main.getTitleButtonRect()));
    }
  };

  private stageVisibilityChanged = (event: window.WindowEventType): void => {
    if (event === window.WindowEventType.WINDOW_SHOWN || event === window.WindowEventType.WINDOW_HIDDEN) {
      if (event === window.WindowEventType.WINDOW_SHOWN) {
        this.observeStageAction(() => this.stageDesktopChanged(this.main.isInFreeWindowMode()));
      }
      if (this.desktop) {
        this.observeStageAction(() => this.stageStatusChanged(this.main.getWindowStatus()));
      }
    }
  };

  private stageDesktopChanged = (enabled: boolean): void => {
    if (!this.active || this.desktop === enabled) {
      return;
    }
    const event = enabled ? ChildEvent.StageDesktopAvailable : ChildEvent.StageDesktopUnavailable;
    if (!this.observeStageAction(() => bridge.reportChildEvent(0, event))) {
      return;
    }
    this.desktop = enabled;
    this.stageState = undefined;
    if (enabled) {
      this.observeStageAction(() => this.stageStatusChanged(this.main.getWindowStatus()));
      this.windows.forEach((child: window.Window, key: number): void => this.publishChildStatus(key, child));
    } else {
      this.clearTitleBarLayout();
    }
  };

  private stageRectChanged = (): void => {
    if (!this.active || !this.desktop) {
      return;
    }
    this.observeStageAction(() => {
      const props = this.main.getWindowProperties();
      const rect = props.windowRect;
      if (rect.width > 0 && rect.height > 0) {
        bridge.reportChildFrame(0, props.id, props.displayId ?? 0, rect.left, rect.top, rect.width, rect.height);
      }
    });
  };

  private reportStageState(state: window.WindowStatusType): void {
    if (!this.active || !this.desktop) {
      return;
    }
    const event = this.windowStatusEvent(state);
    if (event !== undefined && this.stageState !== event &&
      this.observeStageAction(() => bridge.reportChildEvent(0, event))) {
      this.stageState = event;
    }
  }

  private windowStatusEvent(state: window.WindowStatusType): ChildEvent | undefined {
    switch (state) {
      case window.WindowStatusType.FLOATING:
      case window.WindowStatusType.SPLIT_SCREEN:
        return ChildEvent.StageNormal;
      case window.WindowStatusType.MINIMIZE:
        return ChildEvent.StageMinimized;
      case window.WindowStatusType.MAXIMIZE:
        return ChildEvent.StageMaximized;
      case window.WindowStatusType.FULL_SCREEN:
        return ChildEvent.StageFullScreen;
      default:
        return undefined;
    }
  }

  private observeStage(): void {
    this.stageStatusDidChange = this.observeStageAction(() => this.main.on('windowStatusDidChange', this.stageStatusChanged));
    if (!this.stageStatusDidChange) {
      this.observeStageAction(() => this.main.on('windowStatusChange', this.stageStatusChanged));
    }
    this.observeStageAction(() => this.main.on('windowEvent', this.stageVisibilityChanged));
    this.observeStageAction(() => this.main.on('windowRectChange', this.stageRectChanged));
    this.observeStageAction(() => this.main.on('freeWindowModeChange', this.stageDesktopChanged));
    this.observeStageAction(() => this.stageDesktopChanged(this.main.isInFreeWindowMode()));
  }

  private unobserveStage(): void {
    if (this.stageStatusDidChange) {
      this.observeStageAction(() => this.main.off('windowStatusDidChange', this.stageStatusChanged));
    } else {
      this.observeStageAction(() => this.main.off('windowStatusChange', this.stageStatusChanged));
    }
    this.observeStageAction(() => this.main.off('windowEvent', this.stageVisibilityChanged));
    this.observeStageAction(() => this.main.off('windowRectChange', this.stageRectChanged));
    this.observeStageAction(() => this.main.off('freeWindowModeChange', this.stageDesktopChanged));
  }

  private observeStageAction(action: () => void): boolean {
    try {
      action();
      return true;
    } catch (error) {
      this.warnStage('observe', error as BusinessError);
      return false;
    }
  }

  private warnStage(operation: string, error: BusinessError): void {
    hilog.warn(0x0202, 'AvaloniaOHOS', 'Stage window %{public}s failed (%{public}d): %{public}s',
      operation, error.code ?? 0, error.message);
  }

  private hasModalChild(key: number): boolean {
    for (const modal of this.modalWindows) {
      if (this.parents.get(modal) === key && !this.hiddenChildren.has(modal)) {
        return true;
      }
    }
    return false;
  }

  private async activateWindow(key: number, target: window.Window): Promise<boolean> {
    if (target.isFocused()) {
      return true;
    }
    if (!target.isWindowShowing() || this.hasModalChild(key)) {
      return false;
    }
    if (key !== 0 && this.main.isFocused()) {
      await window.shiftAppWindowFocus(this.main.getWindowProperties().id, target.getWindowProperties().id);
      return true;
    }
    for (const childKey of this.windows.keys()) {
      const child = this.windows.get(childKey);
      if (child !== undefined && !this.closing.has(childKey) && child.isFocused()) {
        await window.shiftAppWindowFocus(child.getWindowProperties().id, target.getWindowProperties().id);
        return true;
      }
    }
    return false;
  }

  private async show(request: WindowRequest, existing?: window.Window): Promise<boolean> {
    const key = request.id;
    const ownerId = request.ownerId ?? 0;
    if (!Number.isInteger(ownerId) || ownerId < 0 || this.closing.has(ownerId) ||
      request.independent && (request.modal || ownerId !== 0 || (!this.desktop && existing === undefined))) {
      return false;
    }
    const parent = ownerId === 0 ? this.main : this.windows.get(ownerId);
    if (parent === undefined) {
      return false;
    }
    if (existing !== undefined) {
      if (this.parents.get(key) !== ownerId) {
        await existing.setParentWindow(parent.getWindowProperties().id);
        this.parents.set(key, ownerId);
      }
      if (this.modalWindows.has(key) !== request.modal) {
        await existing.setSubWindowModal(request.modal);
        if (request.modal) {
          this.modalWindows.add(key);
        } else {
          this.modalWindows.delete(key);
        }
      }
      await this.showWindow(existing, request.activate);
      this.hiddenChildren.delete(key);
      this.minimizedChildren.delete(key);
      this.publishEvent(key, existing, ChildEvent.Shown);
      this.publishChildStatus(key, existing);
      return true;
    }

    const owner = parent.getWindowProperties().windowRect;
    const width = request.width ?? Math.round(owner.width * 0.65);
    const height = request.height ?? Math.round(owner.height * 0.55);
    const rect: window.Rect = {
      left: request.x ?? owner.left + Math.round((owner.width - width) / 2),
      top: request.y ?? owner.top + Math.round((owner.height - height) / 2),
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
    if (request.canMaximize && !request.modal) {
      options.maximizeSupported = true;
    }
    if (request.independent) {
      options.zLevelAboveParentLoosened = true;
    }

    let child: window.Window | undefined;
    try {
      child = await this.createWindow(name, ownerId === 0 ? undefined : parent, options);
      this.windows.set(key, child);
      this.parents.set(key, ownerId);
      if (request.independent) {
        this.independentWindows.add(key);
      }
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
      if (request.independent && (request.canMaximize === false || request.canMinimize === false)) {
        try {
          child.setWindowTitleButtonVisible(request.canMaximize ?? true, request.canMinimize ?? true);
        } catch (error) {
          this.warnClose(key, 'set caption buttons', error as Error);
        }
      }
      this.publishFrame(key, child);
      this.publishEvent(key, child, ChildEvent.Shown);
      this.publishChildStatus(key, child);
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
    hilog.info(0x0202, 'AvaloniaOHOS', 'Window %{public}d native size limits %{public}d x %{public}d to %{public}d x %{public}d',
      key, applied.minWidth ?? 0, applied.minHeight ?? 0, applied.maxWidth ?? 0, applied.maxHeight ?? 0);
  }

  private frameLimit(limit: number | undefined, defaultLimit: number | undefined, chrome: number): number | undefined {
    return limit === undefined ? defaultLimit : Math.min(limit + chrome, 2147483647);
  }

  private async createWindow(name: string, parent: window.Window | undefined,
    options: window.SubWindowOptions): Promise<window.Window> {
    try {
      return parent === undefined ? await this.stage.createSubWindowWithOptions(name, options) :
        await parent.createSubWindowWithOptions(name, options);
    } catch (error) {
      const code = (error as BusinessError).code;
      if (code !== 401 && code !== 801) {
        throw new Error('createSubWindowWithOptions failed (' + code + ')');
      }
      const fallback: window.SubWindowOptions = {
        title: options.title,
        decorEnabled: options.decorEnabled,
        zLevelAboveParentLoosened: options.zLevelAboveParentLoosened,
        maximizeSupported: options.maximizeSupported
      };
      if (options.isModal) {
        fallback.isModal = true;
        fallback.modalityType = window.ModalityType.WINDOW_MODALITY;
      }
      try {
        return parent === undefined ? await this.stage.createSubWindowWithOptions(name, fallback) :
          await parent.createSubWindowWithOptions(name, fallback);
      } catch (retry) {
        const failure = retry as BusinessError;
        if (options.isModal || options.zLevelAboveParentLoosened || parent !== undefined ||
          (failure.code !== 401 && failure.code !== 801)) {
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
          this.minimizedChildren.delete(key);
          this.publishEvent(key, child, ChildEvent.Shown);
          this.publishChildStatus(key, child);
          break;
        case window.WindowEventType.WINDOW_HIDDEN:
          if (this.desktop && this.independentWindows.has(key) && !this.hiddenChildren.has(key) &&
            !this.closing.has(key)) {
            this.minimizedChildren.add(key);
          }
          this.publishEvent(key, child, ChildEvent.Hidden);
          this.publishChildStatus(key, child);
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
    const statusChanged = (_status: window.WindowStatusType): void => this.publishChildStatus(key, child);
    if (!this.observeStageAction(() => child.on('windowStatusDidChange', statusChanged))) {
      this.observeStageAction(() => child.on('windowStatusChange', statusChanged));
    }
    try {
      child.on('windowRectChange', (_change: window.RectChangeOptions): void => this.publishFrame(key, child));
      child.on('windowWillClose', (): Promise<boolean> => bridge.requestChildClose(key));
    } catch (error) {
      const failure = error as BusinessError;
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Child window %{public}d desktop events unavailable (%{public}d)',
        key, failure.code);
    }
  }

  private publishChildStatus(key: number, child: window.Window): void {
    if (!this.active || !this.desktop || this.windows.get(key) !== child || this.hiddenChildren.has(key)) {
      return;
    }
    this.observeStageAction(() => {
      const state = this.minimizedChildren.has(key) ? ChildEvent.StageMinimized :
        this.windowStatusEvent(child.getWindowStatus());
      if (state !== undefined) {
        this.publishEvent(key, child, state);
      }
    });
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
      this.parents.delete(key);
      this.independentWindows.delete(key);
      this.minimizedChildren.delete(key);
      this.hiddenChildren.delete(key);
      this.defaultLimits.delete(key);
      this.modalWindows.delete(key);
      bridge.reportChildEvent(key, ChildEvent.Destroyed);
    }
  }
}
