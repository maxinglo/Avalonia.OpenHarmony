import { hilog } from '@kit.PerformanceAnalysisKit';
import { accessibility } from '@kit.AccessibilityKit';
import { dragController, NodeContent, window } from '@kit.ArkUI';
import { unifiedDataChannel } from '@kit.ArkData';
import bridge from 'libavalonia_ohos_bridge.so';
import { AvaloniaDropReceiver, avaloniaDropTypes } from './AvaloniaDropReceiver';
import { AvaloniaTextDragSource } from './AvaloniaTextDragSource';
import { childWindowName } from '../entryability/ChildWindows';
import { WindowInsetsObserver } from '../entryability/WindowInsets';

class SurfaceController extends XComponentController {
  surfaceId: string = '';
  scale: number = 1;
  offsetX: number = 0;
  offsetY: number = 0;
  windowKey: number = 0;
  attached: boolean = false;
  textDragSource: AvaloniaTextDragSource | undefined;
  private childInsets: WindowInsetsObserver | undefined;
  private mouseButtons: number = 0;
  private mouseX: number = 0;
  private mouseY: number = 0;
  private metaPressed: boolean = false;
  readScale: () => number = (): number => 1;
  geometryChanged: () => void = (): void => {};

  onSurfaceCreated(surfaceId: string): void {
    if (this.surfaceId !== surfaceId) {
      this.stopChildInsets();
    }
    this.surfaceId = surfaceId;
  }

  onSurfaceChanged(surfaceId: string, rect: SurfaceRect): void {
    if (this.surfaceId !== surfaceId || rect.surfaceWidth <= 0 || rect.surfaceHeight <= 0) {
      return;
    }
    try {
      this.scale = this.readScale();
      this.offsetX = rect.offsetX ?? 0;
      this.offsetY = rect.offsetY ?? 0;
      if (this.windowKey > 0) {
        bridge.updateSurface(surfaceId, rect.surfaceWidth, rect.surfaceHeight, this.scale, this.windowKey);
      } else {
        bridge.updateSurface(surfaceId, rect.surfaceWidth, rect.surfaceHeight, this.scale);
      }
      this.geometryChanged();
      if (this.attached && this.windowKey > 0 && this.childInsets === undefined) {
        const child = window.findWindow(childWindowName(this.windowKey));
        this.childInsets = new WindowInsetsObserver(child, surfaceId);
        this.childInsets.start();
      }
    } catch (error) {
      hilog.error(0x0202, 'AvaloniaOHOS', '%{public}s', (error as Error).message);
    }
  }

  onSurfaceDestroyed(surfaceId: string): void {
    if (this.surfaceId === surfaceId) {
      this.stopChildInsets();
      this.textDragSource?.surfaceDestroyed();
      this.surfaceId = '';
      this.resetHardwareState();
    }
    bridge.closeSurface(surfaceId);
  }

  stopChildInsets(): void {
    this.childInsets?.stop();
    this.childInsets = undefined;
  }

  onTouch(event: TouchEvent): void {
    if (!this.surfaceId || event.source === SourceType.Mouse) {
      return;
    }
    let phase: number;
    switch (event.type) {
      case TouchType.Down:
        phase = 0;
        break;
      case TouchType.Move:
        phase = 1;
        break;
      case TouchType.Up:
        phase = 2;
        break;
      case TouchType.Cancel:
        phase = 3;
        break;
      default:
        return;
    }
    for (const point of event.changedTouches) {
      const contact = this.textDragSource?.contact(point.id, phase) ?? point.id;
      bridge.touch(this.surfaceId, contact, phase, point.x * this.scale, point.y * this.scale);
    }
    if (phase === 3 && this.textDragSource !== undefined) {
      for (const contact of this.textDragSource.cancelContacts()) {
        bridge.touch(this.surfaceId, contact, phase, 0, 0);
      }
    }
  }

  private pointerModifiers(event: BaseEvent): number {
    return (event.getModifierKeyState?.(['Alt']) ? 1 : 0)
      | (event.getModifierKeyState?.(['Ctrl']) ? 2 : 0)
      | (event.getModifierKeyState?.(['Shift']) ? 4 : 0)
      | (this.metaPressed ? 8 : 0) | this.mouseButtons;
  }

  private buttonCode(button: MouseButton): number {
    switch (button) {
      case MouseButton.Left: return 1;
      case MouseButton.Right: return 2;
      case MouseButton.Middle: return 3;
      case MouseButton.Back: return 4;
      case MouseButton.Forward: return 5;
      default: return 0;
    }
  }

  private buttonMask(code: number): number {
    return code === 0 ? 0 : 1 << (code + 3);
  }

  onHardwareMouse(event: MouseEvent): void {
    if (!this.surfaceId || event.source === SourceType.TouchScreen) {
      return;
    }
    const button = this.buttonCode(event.button);
    let phase: number;
    switch (event.action) {
      case MouseAction.Press: phase = 1; break;
      case MouseAction.Release: phase = 2; break;
      case MouseAction.Move:
      case MouseAction.Hover: phase = 0; break;
      default: return;
    }
    if (phase !== 0 && button === 0) {
      return;
    }
    if (event.pressedButtons !== undefined) {
      this.mouseButtons = 0;
      for (const pressed of event.pressedButtons) {
        this.mouseButtons |= this.buttonMask(this.buttonCode(pressed));
      }
    }
    if (phase === 1) {
      this.mouseButtons |= this.buttonMask(button);
    } else if (phase === 2) {
      this.mouseButtons &= ~this.buttonMask(button);
    }
    this.mouseX = event.x * this.scale;
    this.mouseY = event.y * this.scale;
    bridge.pointer(this.surfaceId, phase, button, this.mouseX, this.mouseY, 0, 0, this.pointerModifiers(event));
  }

  onHardwareHover(isHover: boolean, event: HoverEvent): void {
    if (!isHover && this.surfaceId && event.source === SourceType.Mouse) {
      bridge.pointer(this.surfaceId, 3, 0, this.mouseX, this.mouseY, 0, 0, this.pointerModifiers(event));
    }
  }

  onHardwareAxis(event: AxisEvent): void {
    if (!this.surfaceId || event.action === AxisAction.END || event.action === AxisAction.CANCEL) {
      return;
    }
    // Wheel deltas are angles with system sensitivity applied. A touchpad supplies a distance instead.
    const unitsPerStep = event.sourceTool === SourceTool.TOUCHPAD ? 48 : 15;
    const x = -event.getHorizontalAxisValue() / unitsPerStep;
    const y = -event.getVerticalAxisValue() / unitsPerStep;
    if (x !== 0 || y !== 0) {
      bridge.pointer(this.surfaceId, 4, 0, event.x * this.scale, event.y * this.scale, x, y,
        this.pointerModifiers(event));
    }
  }

  onHardwareKey(event: KeyEvent): boolean {
    if (!this.surfaceId || event.keySource !== KeySource.Keyboard) {
      return false;
    }
    if (event.type !== KeyType.Down && event.type !== KeyType.Up && event.type !== KeyType.CANCEL) {
      return false;
    }
    this.metaPressed = event.metaKey === 1;
    const modifiers = (event.getModifierKeyState?.(['Alt']) ? 1 : 0)
      | (event.getModifierKeyState?.(['Ctrl']) ? 2 : 0)
      | (event.getModifierKeyState?.(['Shift']) ? 4 : 0)
      | (this.metaPressed ? 8 : 0);
    return bridge.key(this.surfaceId, event.type === KeyType.Down, event.keyCode, modifiers,
      event.unicode ?? 0, event.isNumLockOn !== false ? 1 : 0);
  }

  onHardwareBlur(): void {
    this.textDragSource?.revokeGestures();
    if (this.surfaceId) {
      bridge.inputBlur(this.surfaceId);
    }
    this.resetHardwareState();
  }

  private resetHardwareState(): void {
    this.mouseButtons = 0;
    this.mouseX = 0;
    this.mouseY = 0;
    this.metaPressed = false;
  }
}

@Component
export struct AvaloniaSurfaceHost {
  @Prop enableAccessibility: boolean = false;
  @Prop enableTextDragSource: boolean = false;
  @Prop windowKey: number = 0;
  @Prop enableInboundDrop: boolean = false;
  private surface: SurfaceController = new SurfaceController();
  private dropReceiver: AvaloniaDropReceiver = new AvaloniaDropReceiver();
  private semantics: NodeContent = new NodeContent();
  private accessibilityActive: boolean = false;
  private accessibilityStatus: number = -2;
  private accessibilityChanged = (enabled: boolean): void => {
    this.accessibilityActive = enabled;
    this.publishAccessibility();
  };

  aboutToAppear(): void {
    this.surface.attached = true;
    this.surface.windowKey = this.windowKey;
    this.surface.readScale = (): number => this.getUIContext().vp2px(1);
    this.surface.geometryChanged = (): void => this.updateSurfaceOrigin();
    if (this.enableTextDragSource) {
      this.surface.textDragSource = new AvaloniaTextDragSource(
        (pointerId: number, texts: string[]): Promise<boolean> => this.startTextDrag(pointerId, texts));
    }
    if (this.enableAccessibility) {
      this.accessibilityActive = accessibility.isOpenAccessibilitySync();
      accessibility.on('accessibilityStateChange', this.accessibilityChanged);
    }
  }

  aboutToDisappear(): void {
    this.surface.attached = false;
    this.surface.stopChildInsets();
    this.surface.textDragSource?.surfaceDestroyed();
    if (this.enableInboundDrop && this.surface.surfaceId) {
      bridge.drag(this.surface.surfaceId, 2, 0, 0, 0, 0, '');
    }
    if (this.enableAccessibility) {
      accessibility.off('accessibilityStateChange', this.accessibilityChanged);
      this.accessibilityChanged(false);
    }
  }

  @Builder
  private textDragPreview() {
    Text('Text')
      .height(48).width(92).textAlign(TextAlign.Center).fontColor(Color.White)
      .backgroundColor(0x24549B).borderRadius(12)
  }

  private async startTextDrag(pointerId: number, texts: string[]): Promise<boolean> {
    const data = new unifiedDataChannel.UnifiedData();
    for (const text of texts) {
      const record = new unifiedDataChannel.PlainText();
      record.textContent = text;
      data.addRecord(record);
    }
    const info: dragController.DragInfo = {
      pointerId,
      data
    };
    try {
      const result = await this.getUIContext().getDragController().executeDrag((): void => this.textDragPreview(), info);
      const status = result.event.getResult();
      hilog.info(0x0202, 'AvaloniaOHOS', 'System text drag result: %{public}d', status);
      return status === DragResult.DRAG_SUCCESSFUL;
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not start system text drag: %{public}s', (error as Error).message);
      return false;
    }
  }

  private publishAccessibility(): void {
    const surfaceId = this.surface.surfaceId;
    if (!surfaceId || (!this.accessibilityActive && this.accessibilityStatus !== 0)) {
      return;
    }
    try {
      const status = bridge.configureAccessibility(surfaceId, this.accessibilityActive,
        this.accessibilityActive ? this.semantics : null);
      if (status !== this.accessibilityStatus) {
        hilog.info(0x0202, 'AvaloniaOHOS', 'Accessibility registration: %{public}d', status);
      }
      this.accessibilityStatus = this.accessibilityActive ? status : -2;
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Accessibility: %{public}s', (error as Error).message);
    }
  }

  private updateSurfaceOrigin(): void {
    const surfaceId = this.surface.surfaceId;
    if (surfaceId) {
      try {
        const info = this.getUIContext().getComponentUtils().getRectangleById('avalonia-surface');
        bridge.setSurfaceOffset(surfaceId, Math.round(info.windowOffset.x + this.surface.offsetX),
          Math.round(info.windowOffset.y + this.surface.offsetY));
      } catch (error) {
        hilog.warn(0x0202, 'AvaloniaOHOS', 'Surface position: %{public}s', (error as Error).message);
      }
      this.publishAccessibility();
    }
  }

  private receiveDrag(phase: number, event: DragEvent): void {
    const id = this.surface.surfaceId;
    if (!this.enableInboundDrop || !id) {
      return;
    }
    if (phase === 2) {
      bridge.drag(id, phase, 0, 0, 0, 0, '');
      return;
    }
    try {
      const component = this.getUIContext().getComponentUtils().getRectangleById('avalonia-surface');
      const x = event.getWindowX() * this.surface.scale - component.windowOffset.x - this.surface.offsetX;
      const y = event.getWindowY() * this.surface.scale - component.windowOffset.y - this.surface.offsetY;
      this.dropReceiver.dispatch(id, phase, x, y, event);
    } catch (error) {
      bridge.drag(id, 2, 0, 0, 0, 0, '');
      event.setResult(phase === 3 ? DragResult.DRAG_FAILED : DragResult.DROP_DISABLED);
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not route the system drag event');
    }
  }

  build() {
    Stack() {
      XComponent({ id: 'avalonia', type: XComponentType.SURFACE, controller: this.surface })
        .id('avalonia-surface')
        .width('100%')
        .height('100%')
        .focusable(true)
        .focusOnTouch(true)
        .defaultFocus(true)
        .onTouch((event: TouchEvent) => this.surface.onTouch(event))
        .onMouse((event: MouseEvent): void => {
          if (event.action === MouseAction.Press && event.source === SourceType.Mouse) {
            this.getUIContext().getFocusController().requestFocus('avalonia-surface');
          }
          this.surface.onHardwareMouse(event);
        })
        .onHover((isHover: boolean, event: HoverEvent) => this.surface.onHardwareHover(isHover, event))
        .onAxisEvent((event: AxisEvent) => this.surface.onHardwareAxis(event))
        .onKeyEvent((event: KeyEvent): boolean => this.surface.onHardwareKey(event))
        .onBlur(() => this.surface.onHardwareBlur())
        .allowDrop(this.enableInboundDrop ? avaloniaDropTypes : null)
        .onDragEnter((event: DragEvent) => this.receiveDrag(0, event))
        .onDragMove((event: DragEvent) => this.receiveDrag(1, event))
        .onDragLeave((event: DragEvent) => this.receiveDrag(2, event))
        .onDrop((event: DragEvent) => this.receiveDrag(3, event))
        .onAreaChange((_previous: Area, _current: Area) => this.updateSurfaceOrigin())
      if (this.enableAccessibility) {
        Stack() {
          ContentSlot(this.semantics)
        }.width('100%').height('100%').hitTestBehavior(HitTestMode.None)
      }
    }.width('100%').height('100%')
  }
}
