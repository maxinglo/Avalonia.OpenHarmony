import bridge from 'libavalonia_ohos_bridge.so';
import { AvaloniaSurfaceHost } from '../components/AvaloniaSurfaceHost';

@Entry
@Component
struct CounterPage {
  onBackPress(): boolean {
    return bridge.requestBack();
  }

  build() {
    Column() {
      AvaloniaSurfaceHost().layoutWeight(1)
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
