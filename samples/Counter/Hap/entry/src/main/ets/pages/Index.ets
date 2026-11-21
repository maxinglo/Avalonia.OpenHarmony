import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

class SurfaceController extends XComponentController {
  surfaceId: string = '';
  scale: number = 1;
  private mouseButtons: number = 0;
  private mouseX: number = 0;
  private mouseY: number = 0;
  private metaPressed: boolean = false;
  readScale: () => number = (): number => 1;
  geometryChanged: () => void = (): void => {};

  onSurfaceCreated(surfaceId: string): void {
    this.surfaceId = surfaceId;
  }

  onSurfaceChanged(surfaceId: string, rect: SurfaceRect): void {
    if (rect.surfaceWidth <= 0 || rect.surfaceHeight <= 0) {
      return;
    }
    try {
      this.scale = this.readScale();
      bridge.updateSurface(surfaceId, rect.surfaceWidth, rect.surfaceHeight, this.scale);
      this.geometryChanged();
    } catch (error) {
      hilog.error(0x0202, 'AvaloniaOHOS', '%{public}s', (error as Error).message);
    }
  }

  onSurfaceDestroyed(surfaceId: string): void {
    bridge.closeSurface(surfaceId);
    if (this.surfaceId === surfaceId) {
      this.surfaceId = '';
      this.resetHardwareState();
    }
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
      bridge.touch(this.surfaceId, point.id, phase, point.x * this.scale, point.y * this.scale);
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

@Entry
@Component
struct CounterPage {
  private surface: SurfaceController = new SurfaceController();

  aboutToAppear(): void {
    this.surface.readScale = (): number => this.getUIContext().vp2px(1);
    this.surface.geometryChanged = (): void => this.updateSurfaceOrigin();
  }

  onBackPress(): boolean {
    return bridge.requestBack();
  }

  private updateSurfaceOrigin(): void {
    const surfaceId = this.surface.surfaceId;
    if (surfaceId) {
      try {
        const info = this.getUIContext().getComponentUtils().getRectangleById('avalonia-surface');
        bridge.setSurfaceOffset(surfaceId, Math.round(info.windowOffset.x), Math.round(info.windowOffset.y));
      } catch (error) {
        hilog.warn(0x0202, 'AvaloniaOHOS', 'Surface position: %{public}s', (error as Error).message);
      }
    }
  }

  build() {
    Column() {
      XComponent({ id: 'avalonia', type: XComponentType.SURFACE, controller: this.surface })
        .id('avalonia-surface')
        .width('100%')
        .layoutWeight(1)
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
        .onAreaChange((_previous: Area, _current: Area) => this.updateSurfaceOrigin())
      Row() {
        PasteButton()
          .id('system-paste')
          .onClick((_event: ClickEvent, result: PasteButtonOnClickResult): void => {
            if (result === PasteButtonOnClickResult.SUCCESS) {
              bridge.requestPaste();
            }
          })
      }.width('100%').justifyContent(FlexAlign.End).padding(12)
    }.width('100%').height('100%')
  }
}
