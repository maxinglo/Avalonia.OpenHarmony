import bridge from 'libavalonia_ohos_bridge.so';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { AvaloniaSurfaceHost } from '../components/AvaloniaSurfaceHost';

@Entry
@Component
struct CounterPage {
  private readonly enableAccessibility: boolean = false;
  private reportPastePosition(): void {
    try {
      const bounds = this.getUIContext().getComponentUtils().getRectangleById('system-paste');
      const x = Math.round(bounds.screenOffset.x + bounds.size.width / 2);
      const y = Math.round(bounds.screenOffset.y + bounds.size.height / 2);
      hilog.info(0x0202, 'AvaloniaOHOS', 'System Paste position: %{public}d,%{public}d', x, y);
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'System Paste position unavailable');
    }
  }

  onBackPress(): boolean {
    return bridge.requestBack();
  }

  build() {
    Column() {
      AvaloniaSurfaceHost({
        enableAccessibility: this.enableAccessibility,
        enableInboundDrop: true,
        enableTextDragSource: true
      }).layoutWeight(1)
      Row() {
        PasteButton()
          .id('system-paste')
          .onClick((_event: ClickEvent, result: PasteButtonOnClickResult): void => {
            hilog.info(0x0202, 'AvaloniaOHOS', 'System Paste authorization: %{public}d', result);
            if (result === PasteButtonOnClickResult.SUCCESS) {
              bridge.requestPaste();
            }
          })
      }.width('100%').justifyContent(FlexAlign.End).padding(12)
        .onAreaChange((_previous: Area, _next: Area): void => this.reportPastePosition())
    }.width('100%').height('100%')
  }
}
