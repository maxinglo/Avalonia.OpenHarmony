import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

class SurfaceController extends XComponentController {
  surfaceId: string = '';
  scale: number = 1;

  onSurfaceCreated(surfaceId: string): void {
    this.surfaceId = surfaceId;
  }

  onSurfaceChanged(surfaceId: string, rect: SurfaceRect): void {
    if (rect.surfaceWidth <= 0 || rect.surfaceHeight <= 0) {
      return;
    }
    try {
      bridge.updateSurface(surfaceId, rect.surfaceWidth, rect.surfaceHeight, this.scale);
    } catch (error) {
      hilog.error(0x0202, 'AvaloniaOHOS', '%{public}s', (error as Error).message);
    }
  }

  onSurfaceDestroyed(surfaceId: string): void {
    bridge.closeSurface(surfaceId);
    if (this.surfaceId === surfaceId) {
      this.surfaceId = '';
    }
  }

  onTouch(event: TouchEvent): void {
    if (!this.surfaceId) {
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
}

@Entry
@Component
struct CounterPage {
  private surface: SurfaceController = new SurfaceController();

  aboutToAppear(): void {
    this.surface.scale = this.getUIContext().vp2px(1);
  }

  build() {
    Stack() {
      XComponent({ id: 'avalonia', type: XComponentType.SURFACE, controller: this.surface })
        .width('100%')
        .height('100%')
        .onTouch((event: TouchEvent) => this.surface.onTouch(event))
    }.width('100%').height('100%')
  }
}
