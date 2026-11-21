import { AvaloniaSurfaceHost } from '../components/AvaloniaSurfaceHost';

@Entry({ useSharedStorage: true })
@Component
struct AvaloniaChildWindow {
  @LocalStorageProp('avaloniaChildWindowKey') private windowKey: number = 0;

  build() {
    Stack() {
      if (this.windowKey > 0) {
        AvaloniaSurfaceHost({ windowKey: this.windowKey })
      }
    }.width('100%').height('100%')
  }
}
